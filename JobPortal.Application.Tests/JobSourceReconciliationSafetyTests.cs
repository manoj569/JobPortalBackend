using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Tests.GenericIndiaJobIngestionTests;

namespace JobPortal.Application.Tests;

public sealed class JobSourceReconciliationSafetyTests
{
    [Theory]
    [InlineData(AtsType.Workday)]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    [InlineData(AtsType.SuccessFactors)]
    public async Task CompleteCensusPersistsOnlyIndiaJobsAndClosesOnlyTrulyAbsentJobs(AtsType ats)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = ats;
        var eligible = Enumerable.Range(1, 44).Select(Vacancy).ToArray();
        // REQ45 will be held next time; REQ999 is genuinely absent next time.
        var provider = new SnapshotProvider(ats, [.. eligible, Vacancy(45), Vacancy(999)]);
        var runner = Runner(f, provider);
        var initial = await runner.RunAsync(f.Source.Id);
        Assert.Equal(46, initial.Created);
        var originalIds = await f.Context.Jobs.ToDictionaryAsync(x => x.ExternalJobId!, x => x.Id);
        f.Source.ConsecutiveFailures = 3;
        f.Source.LastError = "Previous incomplete scan";
        await f.Context.SaveChangesAsync();
        provider.Jobs = [.. eligible,
            Vacancy(45) with { Location = "Unknown" },
            .. Enumerable.Range(46, 88).Select(id => Vacancy(id) with { Location = "Boston", CountryCodes = ["US"] }),
            .. Enumerable.Range(134, 43).Select(id => Vacancy(id) with { Location = "Remote", WorkplaceTypeText = "remote" })];

        var result = await runner.RunAsync(f.Source.Id);
        Assert.True(result.ScanComplete);
        Assert.Equal(ats != AtsType.Workday, result.ReconciliationSafe);
        Assert.True(result.Succeeded);
        if (ats == AtsType.Workday) Assert.Contains("WorkdayNonAuthoritative", result.ReconciliationBlockReasons);
        else Assert.Empty(result.ReconciliationBlockReasons);
        Assert.Equal(176, result.TotalReceived);
        Assert.Equal(0, result.Created);
        Assert.Equal(44, result.Existing);
        Assert.Equal(132, result.SelectionSkipped);
        Assert.Equal(88, result.NonIndiaRejected);
        Assert.Equal(44, result.AmbiguousLocationHeld);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Published);
        Assert.Equal(0, result.Closed);
        if (ats != AtsType.Workday)
        {
            var confirmedMissing = await runner.RunAsync(f.Source.Id);
            Assert.Equal(1, confirmedMissing.Closed);
        }
        var inventory = await f.Context.Jobs.ToArrayAsync();
        Assert.Equal(46, inventory.Length);
        Assert.All(inventory.Where(job => originalIds.ContainsKey(job.ExternalJobId!)), job => Assert.Equal(originalIds[job.ExternalJobId!], job.Id));
        Assert.Equal(JobStatus.Published, inventory.Single(x => x.ExternalJobId == "REQ45").Status);
        Assert.Equal(ats == AtsType.Workday ? JobStatus.Published : JobStatus.Closed, inventory.Single(x => x.ExternalJobId == "REQ999").Status);
        Assert.Null(f.Source.LastError);
        Assert.Equal(0, f.Source.ConsecutiveFailures);
        Assert.NotNull(f.Source.LastSuccessfulRunAtUtc);
        var repeated = await runner.RunAsync(f.Source.Id);
        Assert.True(repeated.Succeeded);
        Assert.Equal(44, repeated.Existing);
        Assert.Equal(0, repeated.Created);
        Assert.Equal(0, repeated.Published);
        Assert.Equal(0, repeated.Closed);

        // Same JSON path used by the durable store and Admin run-status response.
        var response = JobSourceRunResponse.From(new JobSourceRun
        {
            JobSourceId = f.Source.Id, Status = JobSourceRunStatus.Succeeded,
            DiagnosticsJson = JsonSerializer.Serialize(result)
        });
        Assert.True(response.Succeeded);
        Assert.Equal(ats != AtsType.Workday, response.Diagnostics!.ReconciliationSafe);
        if (ats == AtsType.Workday) Assert.Contains("WorkdayNonAuthoritative", response.Diagnostics.ReconciliationBlockReasons);
        else Assert.Empty(response.Diagnostics.ReconciliationBlockReasons);
        Assert.Equal(44, response.Existing);
    }

    [Theory]
    [InlineData("incomplete", "ProviderSnapshotIncomplete")]
    [InlineData("detailSkipped", "ProviderSkippedItems")]
    [InlineData("duplicateForeignIdentity", "IncompleteOrDuplicateSourceIdentities")]
    [InlineData("missingForeignIdentity", "IncompleteOrDuplicateSourceIdentities")]
    [InlineData("testCap", "SelectionSnapshotIncomplete")]
    [InlineData("invalidEligible", "SkippedItems")]
    [InlineData("persistenceFailed", "FailedItems")]
    public async Task UnsafeCompleteOrPartialCensusStillBlocksClosuresAndPreservesSuccessTimestamp(string scenario, string reason)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Workday;
        var seed = new SnapshotProvider(AtsType.Workday, [Vacancy(1), Vacancy(999)]);
        await Runner(f, seed).RunAsync(f.Source.Id);
        var lastSuccess = f.Source.LastSuccessfulRunAtUtc;
        RawExternalJob foreign = Vacancy(2) with { Location = "Boston", CountryCodes = ["US"] };
        RawExternalJob[] jobs = scenario switch
        {
            "duplicateForeignIdentity" => [Vacancy(1), foreign, foreign],
            "missingForeignIdentity" => [Vacancy(1), foreign with { ExternalId = null }],
            "invalidEligible" => [Vacancy(1), Vacancy(3) with { Title = " " }],
            _ => [Vacancy(1), foreign with { Location = "Remote", CountryCodes = [] }]
        };
        var snapshot = new ExternalJobSourceSnapshot(jobs, scenario == "detailSkipped" ? 1 : 0, scenario != "incomplete");
        var options = new JobAggregationOptions();
        if (scenario == "testCap")
            options.SourceApprovals[f.Source.Id.ToString("D")] = new() { TestImportLimit = 1 };
        var result = await CreateRunner(f, new FixedSnapshot(snapshot), options, scenario == "persistenceFailed" ? new FailedIngestion() : null).RunAsync(f.Source.Id);
        Assert.False(result.Succeeded);
        Assert.False(result.ReconciliationSafe);
        Assert.Contains(reason, result.ReconciliationBlockReasons);
        Assert.Equal(scenario != "incomplete", result.ScanComplete);
        Assert.Equal(0, result.Closed);
        Assert.Equal(lastSuccess, f.Source.LastSuccessfulRunAtUtc);
        Assert.Equal(1, f.Source.ConsecutiveFailures);
        Assert.Equal(JobStatus.Published, (await f.Context.Jobs.SingleAsync(x => x.ExternalJobId == "REQ999")).Status);
        var response = JobSourceRunResponse.From(new JobSourceRun
        {
            Status = JobSourceRunStatus.Failed, ErrorCode = "source_run_failed",
            DiagnosticsJson = JsonSerializer.Serialize(result)
        });
        Assert.False(response.Succeeded);
        Assert.Contains(reason, response.Diagnostics!.ReconciliationBlockReasons);
    }

    [Fact]
    public void HistoricalDiagnosticsWithoutBlockingReasonsRemainReadable()
    {
        var response = JobSourceRunResponse.From(new JobSourceRun
        {
            Status = JobSourceRunStatus.Failed,
            DiagnosticsJson = "{\"ScanComplete\":true,\"ReconciliationSafe\":false,\"Unchanged\":44}"
        });
        Assert.False(response.Succeeded);
        Assert.True(response.Diagnostics!.ScanComplete);
        Assert.False(response.Diagnostics.ReconciliationSafe);
        Assert.Empty(response.Diagnostics.ReconciliationBlockReasons);
        Assert.Equal(44, response.Existing);
    }

    private static JobSourceRunner CreateRunner(JobSourceFixture f, ICompleteExternalJobProvider provider, JobAggregationOptions options, IJobIngestionService? ingestionOverride = null)
    {
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprint = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprint, canonical), fingerprint, unit, TimeProvider.System, f.Locks, canonical);
        return new(f.Repository, [provider], ingestionOverride ?? ingestion, unit, TimeProvider.System, f.Resolver, new ExternalJobNormalizer(),
            autoPublishService: Publisher(f), jobRepository: jobs,
            publicationPolicy: new JobSourcePublicationPolicy(Options.Create(options), TimeProvider.System));
    }

    private sealed class FailedIngestion : IJobIngestionService
    {
        public Task<JobIngestionResult> IngestAsync(RawExternalJob rawJob, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobIngestionResult { Outcome = JobIngestionOutcome.Failed });
    }

    private sealed class FixedSnapshot(ExternalJobSourceSnapshot snapshot) : ICompleteExternalJobProvider
    {
        public AtsType AtsType => AtsType.Workday;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot.Jobs);
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }
}
