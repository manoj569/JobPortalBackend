using System.Runtime.CompilerServices;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JobPortal.Application.Tests.GenericIndiaJobIngestionTests;

namespace JobPortal.Application.Tests;

public sealed class RippleHireMemoryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(1213)]
    public async Task BatchesPreserveCountersAndBoundTrackingThroughPublication(int count)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.RippleHire;
        await f.Context.SaveChangesAsync();
        var feed = new Feed(count);
        var boundaries = 0;
        var peakWorkingSetMB = 0d;
        var peakTracked = 0;
        feed.AfterBatch = () =>
        {
            boundaries++;
            peakWorkingSetMB = Math.Max(peakWorkingSetMB, Environment.WorkingSet / 1048576d);
            peakTracked = Math.Max(peakTracked, f.Context.ChangeTracker.Entries().Count());
            Assert.InRange(f.Context.ChangeTracker.Entries().Count(), 0, 4);
            Assert.Empty(f.Context.ChangeTracker.Entries<Job>());
            Assert.InRange(f.Company.Jobs.Count, 0, 50);
        };
        var runner = Runner(f, feed);
        var result = await runner.RunAsync(f.Source.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(count, result.TotalReceived);
        Assert.Equal(count / 10, result.SelectionSkipped);
        Assert.Equal(count - count / 10, result.Created);
        Assert.Equal(result.Created, result.Published);
        Assert.Equal((count + 49) / 50, boundaries);
        output.WriteLine($"Received={count} Batches={boundaries} Created={result.Created} Published={result.Published} Rejected={result.SelectionSkipped} PeakCommittedTracked={peakTracked} PeakWorkingSetMB={peakWorkingSetMB:F1}");
        Assert.Equal(result.Created, await f.Context.Jobs.CountAsync());
        var publishedAt = await f.Context.Jobs.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.PublishedAtUtc);
        var retry = await runner.RunAsync(f.Source.Id);
        Assert.True(retry.Succeeded);
        Assert.Equal(0, retry.Created);
        Assert.Equal(0, retry.Published);
        Assert.Equal(result.Created, retry.Unchanged);
        Assert.Equal(publishedAt, await f.Context.Jobs.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.PublishedAtUtc));
    }

    [Fact]
    public async Task SourceWideSelectionCapDoesNotRestartForEachBatch()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.RippleHire;
        await f.Context.SaveChangesAsync();
        var options = new JobAggregationOptions();
        options.SourceApprovals[f.Source.Id.ToString("D")] = new JobSourcePublicationApproval { TestImportLimit = 73 };
        var policy = new JobSourcePublicationPolicy(Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System);
        var result = await DurableRunner(f, new Feed(1213), new JobSourceRunCheckpoint(f.Context, TimeProvider.System), policy).RunAsync(f.Source.Id);
        Assert.Equal(1213, result.TotalReceived);
        Assert.Equal(73, result.Created);
        Assert.Equal(73, result.Published);
        Assert.Equal(1140, result.SelectionSkipped);
        Assert.Equal(1019, result.SelectionReasonCounts!["TestImportLimit"]);
        Assert.Equal(73, await f.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task IdentityReceiptsSurviveInterruptionReorderingAndPublicationGap()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.RippleHire;
        await f.Context.SaveChangesAsync();
        var run = new JobSourceRun { JobSourceId = f.Source.Id, Status = JobSourceRunStatus.Running };
        f.Context.Add(run);
        await f.Context.SaveChangesAsync();
        var checkpoint = new JobSourceRunCheckpoint(f.Context, TimeProvider.System);
        await checkpoint.BindAsync(run, default);
        var feed = new Feed(1213);
        using var cancel = new CancellationTokenSource();
        feed.AfterBatch = () => cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DurableRunner(f, feed, checkpoint).RunAsync(f.Source.Id, cancel.Token));
        Assert.Equal(45, await f.Context.Jobs.CountAsync());
        Assert.Equal(45, await f.Context.Set<JobSourceRunItem>().CountAsync());
        // Recreate scoped services and reorder the live feed: ordinal replay would be unsafe.
        f.Context.ChangeTracker.Clear();
        checkpoint = new JobSourceRunCheckpoint(f.Context, TimeProvider.System);
        await checkpoint.BindAsync(run, default);
        feed.Reverse = true;
        feed.AfterBatch = null;
        var result = await DurableRunner(f, feed, checkpoint).RunAsync(f.Source.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(1092, result.Created);
        Assert.Equal(1092, result.Published);
        Assert.Equal(1213, result.TotalReceived);
        Assert.Equal(121, result.SelectionSkipped);
        Assert.Equal(1092, await f.Context.Jobs.CountAsync());
        Assert.Equal(1092, await f.Context.Set<JobSourceRunItem>().CountAsync());
        Assert.Equal(1092, f.Audit.Events.Count(x => x.Action == JobPortal.Domain.Enums.AuditAction.Publish));
        Assert.Equal(1092, await f.Context.Jobs.Select(x => x.ExternalJobId).Distinct().CountAsync());
    }

    [Fact]
    public async Task RetryCompletesPublicationWhenIngestionCommittedBeforeProcessExit()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.RippleHire;
        await f.Context.SaveChangesAsync();
        var run = new JobSourceRun { JobSourceId = f.Source.Id, Status = JobSourceRunStatus.Running };
        f.Context.Add(run);
        await f.Context.SaveChangesAsync();
        var checkpoint = new JobSourceRunCheckpoint(f.Context, TimeProvider.System);
        await checkpoint.BindAsync(run, default);
        checkpoint.Enable(f.Source);
        var jobs = new JobRepository(f.Context);
        var fingerprint = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprint, canonical), fingerprint, new UnitOfWork(f.Context), TimeProvider.System, f.Locks, canonical, checkpoint);
        var raw = Vacancy(1) with { JobSourceId = f.Source.Id, CompanyId = f.Company.Id, Description = "Build reliable systems." };
        Assert.Equal(JobIngestionOutcome.Created, (await ingestion.IngestAsync(raw)).Outcome);
        Assert.Null((await f.Context.Set<JobSourceRunItem>().AsNoTracking().SingleAsync()).PublicationOutcome);
        Assert.Equal(JobStatus.Draft, (await f.Context.Jobs.AsNoTracking().SingleAsync()).Status);
        f.Context.ChangeTracker.Clear();
        checkpoint = new JobSourceRunCheckpoint(f.Context, TimeProvider.System);
        await checkpoint.BindAsync(run, default);
        var runner = DurableRunner(f, new Feed(1), checkpoint);
        var recovered = await runner.RunAsync(f.Source.Id);
        Assert.Equal(1, recovered.Created);
        Assert.Equal(1, recovered.Published);
        Assert.Equal(JobStatus.Published, (await f.Context.Jobs.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, (await runner.RunAsync(f.Source.Id)).Published); // Cumulative receipt, no new transition.
        Assert.Single(f.Audit.Events, x => x.Action == AuditAction.Publish);
        Assert.Single(await f.Context.Jobs.ToArrayAsync());
    }

    [Fact]
    public async Task CleanupReleasesSavedPublicationGraphAndAuditRowsAcrossBatches()
    {
        using var f = new JobSourceFixture();
        var unit = new UnitOfWork(f.Context);
        for (var batch = 0; batch < 25; batch++)
        {
            for (var index = 0; index < 50; index++)
                f.Context.Add(new Job { Company = f.Company, CompanyId = f.Company.Id,
                    Category = f.Category, CategoryId = f.Category.Id, Title = "Engineer",
                    Description = new string('x', 8000), ApplicationUrl = "https://example.test/" + batch + "/" + index });
            f.Context.Add(new AuditLog { EntityName = "Job", EntityId = Guid.NewGuid().ToString(),
                ActorRole = "System", CorrelationId = Guid.NewGuid().ToString() });
            await f.Context.SaveChangesAsync();
            Assert.InRange(unit.ReleaseCommittedAggregationBatch(), 0, 3);
            Assert.Empty(f.Context.ChangeTracker.Entries<Job>());
            Assert.Empty(f.Context.ChangeTracker.Entries<AuditLog>());
            Assert.Empty(f.Company.Jobs);
            Assert.Empty(f.Category.Jobs);
        }
    }

    [Fact]
    public async Task CleanupRefusesToDiscardPendingChanges()
    {
        using var f = new JobSourceFixture();
        f.Company.Name = "Pending change";
        Assert.Throws<InvalidOperationException>(() => new UnitOfWork(f.Context).ReleaseCommittedAggregationBatch());
        Assert.True(f.Context.ChangeTracker.HasChanges());
        await f.Context.SaveChangesAsync();
    }

    private static JobSourceRunner DurableRunner(JobSourceFixture f, IExternalJobProvider feed, IJobSourceRunCheckpoint checkpoint, IJobSourcePublicationPolicy? policy = null)
    {
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, f.Locks, canonical, checkpoint);
        var publisher = new JobAutoPublishService(jobs, new JobQualityGate(), ManualService(f),
            Microsoft.Extensions.Options.Options.Create(new JobAggregationOptions()), TimeProvider.System, f.Repository, f.Locks, checkpoint);
        return new(f.Repository, [feed], ingestion, unit, TimeProvider.System, f.Resolver, new ExternalJobNormalizer(),
            autoPublishService: publisher, jobRepository: jobs, checkpoint: checkpoint, publicationPolicy: policy);
    }

    private sealed class Feed(int count) : IBatchedExternalJobProvider
    {
        public Action? AfterBatch { get; set; }
        public bool Reverse { get; set; }
        public AtsType AtsType => AtsType.RippleHire;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(JobSource source,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var ids = Reverse ? Enumerable.Range(1, count).Reverse() : Enumerable.Range(1, count);
            var received = 0;
            foreach (var chunk in ids.Chunk(50))
            {
                cancellationToken.ThrowIfCancellationRequested();
                received += chunk.Length;
                yield return new(chunk.Select(id => Vacancy(id) with
                {
                    Title = $"Software Engineer {id}", Location = id % 10 == 0 ? "USA" : "Pune, India",
                    Description = "<p>Build reliable systems. " + new string('x', 8000) + "</p>", DescriptionIsHtml = true
                }).ToArray(), 0, false) { AcquisitionSucceeded = received == count, ReportedListingTotal = count };
                AfterBatch?.Invoke();
                await Task.Yield();
            }
        }
    }
}
