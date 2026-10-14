using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class GenericIndiaJobIngestionTests
{
    [Theory]
    [InlineData("Pune", null, null, "IndiaLocationConfirmed")]
    [InlineData("Bengaluru", "Hybrid", null, "IndiaLocationConfirmed")]
    [InlineData("Hyderabad", null, null, "IndiaLocationConfirmed")]
    [InlineData("India-wide remote", "Remote", null, "ExplicitIndiaRemote")]
    [InlineData("Remote", "Remote", "Remote hiring from India", "ExplicitIndiaRemote")]
    public async Task EligibleJobsArePublishedAndVisibleInDiscoverWithOptionalMetadataAbsent(string? location, string? mode, string? eligibility, string reason)
    {
        using var f = new JobSourceFixture();
        var raw = Vacancy(1) with { Location = location, WorkplaceTypeText = mode, RemoteEligibilityText = eligibility };
        var provider = new SnapshotProvider(f.Source.AtsType, [raw]);
        var result = await Runner(f, provider).RunAsync(f.Source.Id);
        Assert.Equal(1, result.Created);
        Assert.True(result.Published == 1, System.Text.Json.JsonSerializer.Serialize(result));
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.EligibilityReasonCounts![reason]);
        var saved = Assert.Single(await f.Context.Jobs.ToArrayAsync());
        Assert.Null(saved.CategoryId);
        Assert.Null(saved.MinimumSalary);
        Assert.Null(saved.MaximumSalary);
        Assert.Null(saved.MinimumExperienceYears);
        Assert.Null(saved.ExpiresAtUtc);
        Assert.Empty(saved.JobSkills);
        Assert.Equal(location ?? "Location not specified", saved.Location);
        var discover = await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new PublicJobQuery());
        Assert.Equal(saved.Id, Assert.Single(discover.Items).Id);
        Assert.Equal(1, discover.TotalCount);
        Assert.Null(Assert.Single(discover.Items).CategoryId);
        Assert.Equal(JobStatus.Published, saved.Status);
        Assert.Equal(0, result.AutoPublishDisabled);
    }

    [Theory]
    [InlineData("New York, United States", "US", false)]
    [InlineData("UK-only remote", "GB", false)]
    [InlineData("Remote", null, true)]
    [InlineData("London", "IN", true)]
    [InlineData("Pune", "US", true)]
    [InlineData("Remote excluding India", null, false)]
    [InlineData("Not India", null, false)]
    [InlineData("Worldwide remote; India excluded", null, false)]
    [InlineData("Unknown", null, true)]
    [InlineData("Brazil", null, false)]
    public async Task ForeignAndAmbiguousJobsArePersistedBeforeExistingPublicationReview(string location, string? country, bool held)
    {
        using var f = new JobSourceFixture();
        var provider = new SnapshotProvider(f.Source.AtsType, [Vacancy(1) with { Location = location, CountryCodes = country is null ? [] : [country] }]);
        var result = await Runner(f, provider).RunAsync(f.Source.Id);
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Published);
        Assert.Equal(0, result.AmbiguousLocationHeld); Assert.Equal(0, result.NonIndiaRejected);
        Assert.Equal(1,result.NotPublished); Assert.True(result.Succeeded);
        var saved = Assert.Single(await f.Context.Jobs.ToArrayAsync());
        Assert.Equal(JobStatus.Draft,saved.Status);
        Assert.Equal(held,ImportedJobIntegrity.Eligibility(saved).Decision == IndiaJobEligibilityDecision.Review);
        Assert.Equal(1,result.NeedsReview);
        Assert.Equal(0, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
        Assert.Equal(0, result.Closed);
    }

    [Fact]
    public void AnAmbiguousAdditionalLocationCannotHideAPrimaryCountryContradiction()
    {
        var result = IndiaJobEligibilityPolicy.Evaluate(Vacancy(1) with
            { Location = "Pune", CountryCodes = ["US"], AdditionalLocations = ["Remote"] });
        Assert.Equal(IndiaJobEligibilityDecision.Review, result.Decision);
        Assert.Equal("ContradictoryLocation", result.ReasonCode);
    }

    [Fact]
    public async Task MultipleLocationsKeepOriginalFactsWithoutRestrictingIngestion()
    {
        using var f = new JobSourceFixture();
        var provider = new SnapshotProvider(f.Source.AtsType, [
            Vacancy(1) with { Location = "London", AdditionalLocations = ["Paris", "Pune, India"], CountryCodes = ["GB", "FR", "IN"] },
            Vacancy(2) with { Location = "London", AdditionalLocations = ["Paris"], CountryCodes = ["GB", "FR"] }]);
        var result = await Runner(f, provider).RunAsync(f.Source.Id);
        Assert.Equal(2, result.Created);
        Assert.True(result.Published == 1, System.Text.Json.JsonSerializer.Serialize(result));
        Assert.Equal(0, result.NonIndiaRejected);
        var job = await f.Context.Jobs.SingleAsync(job => job.ExternalJobId == "REQ1");
        Assert.Equal("London", job.Location);
        Assert.Contains("Pune, India", job.SourceMetadataJson!);
        Assert.Contains("FR", job.SourceMetadataJson!);
        Assert.Equal(1, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
    }

    [Theory]
    [InlineData(AtsType.Workday)]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    [InlineData(AtsType.SuccessFactors)]
    public async Task EveryProviderUsesTheSamePolicyAndRepeatedImportsAreIdempotent(AtsType ats)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = ats;
        await f.Context.SaveChangesAsync();
        var provider = new SnapshotProvider(ats, [Vacancy(1), Vacancy(1), Vacancy(2) with { Location = "US", CountryCodes = ["US"] }]);
        var runner = Runner(f, provider);
        var first = await runner.RunAsync(f.Source.Id);
        Assert.Equal(2, first.Created);
        Assert.Equal(1, first.Existing);
        Assert.Equal(1, first.Published);
        Assert.Equal(0, first.NonIndiaRejected);
        var id = (await f.Context.Jobs.SingleAsync(job => job.ExternalJobId == "REQ1")).Id;
        var second = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, second.Created);
        Assert.Equal(3, second.Existing);
        Assert.Equal(0, second.Published);
        Assert.Equal(id, (await f.Context.Jobs.SingleAsync(job => job.ExternalJobId == "REQ1")).Id);
        Assert.Equal(1, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
    }

    [Fact]
    public async Task DistinctRequisitionsKeepTheirIdentitiesAndIncompleteRunsNeverCloseJobs()
    {
        using var f = new JobSourceFixture();
        var provider = new SnapshotProvider(f.Source.AtsType, [Vacancy(1), Vacancy(2)]);
        var runner = Runner(f, provider);
        var first = await runner.RunAsync(f.Source.Id);
        Assert.Equal(2, first.Created);
        Assert.Equal(2, first.Published);
        Assert.Equal(2, await f.Context.Jobs.Select(x => x.ExternalJobId).Distinct().CountAsync());
        provider.Jobs = [Vacancy(1)]; provider.Complete = false;
        var second = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, second.Closed);
        Assert.False(second.ScanComplete);
        Assert.Equal(2, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
    }

    [Fact]
    public async Task CrossProviderCanonicalUrlPreventsDuplicatesWithoutPublishingUnrelatedManualDrafts()
    {
        using var f = new JobSourceFixture();
        var first = await Runner(f, new SnapshotProvider(f.Source.AtsType, [Vacancy(1)])).RunAsync(f.Source.Id);
        Assert.Equal(1, first.Created);
        var source = new JobSource { Company = f.Company, CompanyId = f.Company.Id, AtsType = AtsType.Lever,
            AtsIdentifier = "acme-second", CareerPageUrl = "https://example.test/careers" };
        f.Context.JobSources.Add(source); await f.Context.SaveChangesAsync();
        var duplicate = Vacancy(99) with { ApplicationUrl = Vacancy(1).ApplicationUrl + "?utm_source=second" };
        var second = await Runner(f, new SnapshotProvider(AtsType.Lever, [duplicate])).RunAsync(source.Id);
        Assert.Equal(0, second.Created); Assert.Equal(1, second.Existing); Assert.Equal(0, second.Published);
        var manual = await ManualService(f).CreateAsync(ManualRequest(f, "https://example.test/manual", null));
        var publisher = Publisher(f);
        Assert.Equal(JobAutoPublishOutcome.NotImported, (await publisher.TryPublishAsync(manual.Id)).Outcome);
        Assert.Equal(JobStatus.Draft, (await f.Context.Jobs.SingleAsync(x => x.Id == manual.Id)).Status);
        Assert.Equal(1, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
        await Assert.ThrowsAsync<ConflictException>(() => ManualService(f).CreateAsync(ManualRequest(f, Vacancy(1).ApplicationUrl!, "Pune")));
        await Assert.ThrowsAsync<BadRequestException>(() => ManualService(f).CreateAsync(ManualRequest(f, "https://example.test/foreign", "London, UK")));
    }

    [Fact]
    public async Task ExistingSourceOwnedDraftIsPublishedAndResetInventoryRecreatesRows()
    {
        using var f = new JobSourceFixture();
        var provider = new SnapshotProvider(f.Source.AtsType, [Vacancy(1)]);
        var runner = Runner(f, provider);
        await runner.RunAsync(f.Source.Id);
        var saved = Assert.Single(await f.Context.Jobs.ToArrayAsync());
        saved.Status = JobStatus.Draft; saved.PublishedAtUtc = null;
        await f.Context.SaveChangesAsync();
        var repeated = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, repeated.Created); Assert.Equal(1, repeated.Existing); Assert.Equal(1, repeated.Published);
        f.Context.ChangeTracker.Clear();
        var removed = await f.Context.Jobs.SingleAsync(x => x.Id == saved.Id);
        f.Context.Jobs.Remove(removed); await f.Context.SaveChangesAsync();
        var reset = await runner.RunAsync(f.Source.Id);
        Assert.Equal(1, reset.Created); Assert.Equal(0, reset.Existing); Assert.Equal(1, reset.Published);
    }

    [Fact]
    public async Task CountryEvidenceWithoutACityKeepsTheMissingCityDisplayLabel()
    {
        using var f = new JobSourceFixture();
        var raw = Vacancy(1) with { Location = null, CountryCodes = ["IN"] };
        var result = await Runner(f, new SnapshotProvider(f.Source.AtsType, [raw])).RunAsync(f.Source.Id);
        Assert.Equal(1, result.Published);
        Assert.Equal(1, result.IndiaEligible);
        Assert.Equal(0, result.MissingLocationAccepted);
        Assert.Equal("Location not specified", Assert.Single(await f.Context.Jobs.ToArrayAsync()).Location);
    }

    [Fact]
    public async Task ManualCompositionWithoutOptionalFieldsDoesNotConflateBlankUrls()
    {
        using var f = new JobSourceFixture();
        var service = ManualService(f);
        foreach (var title in new[] { "Research Engineer", "Tax Accountant" })
        {
            var result = await service.ComposeAsync(Guid.NewGuid(), new(new(title), new(ExistingId: f.Company.Id)));
            Assert.Equal(JobStatus.Draft, result.Status);
            Assert.Null(result.Category);
        }
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
        Assert.All(await f.Context.Jobs.ToArrayAsync(), job => Assert.Equal("Location not specified", job.Location));
        Assert.Equal(0, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
    }

    [Theory]
    [InlineData(" ", "https://example.test/jobs/1")]
    [InlineData("Engineer", "ftp://example.test/jobs/1")]
    [InlineData("Engineer", "https://127.0.0.1/jobs/1")]
    public async Task InvalidRequiredDataNeverPersists(string title, string url)
    {
        using var f = new JobSourceFixture();
        var result = await Runner(f, new SnapshotProvider(f.Source.AtsType, [Vacancy(1) with { Title = title, ApplicationUrl = url }])).RunAsync(f.Source.Id);
        Assert.Equal(0, result.Created); Assert.Equal(1, result.InvalidRequiredDataRejected); Assert.Equal(0, result.Published);
    }

    internal static CreateJobRequest ManualRequest(JobSourceFixture f, string url, string? location) =>
        new("Associate Engineer", "", f.Company.Id, null, url, null, null, null, location, null, null, "INR", default, default, default, null);
    internal static RawExternalJob Vacancy(int id) => new() { ExternalId = $"REQ{id}", Title = "Associate Engineer", CompanyName = "Acme",
        Location = "Pune", ApplicationUrl = $"https://example.test/jobs/REQ{id}" };
    internal static JobService ManualService(JobSourceFixture f)
    {
        var jobs = new JobRepository(f.Context); var fingerprint = new JobFingerprintService(); var canonical = new UrlCanonicalizer();
        return new(jobs, new UnitOfWork(f.Context), f.Audit, new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
            new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), TimeProvider.System,
            new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context), f.Repository, f.Locks,
            new JobDeduplicationService(jobs, fingerprint, canonical));
    }
    internal static JobAutoPublishService Publisher(JobSourceFixture f) => new(new JobRepository(f.Context), new JobQualityGate(),
        ManualService(f), Options.Create(new JobAggregationOptions { AutoPublishEnabled = false }), TimeProvider.System, f.Repository, f.Locks);
    internal static JobSourceRunner Runner(JobSourceFixture f, IExternalJobProvider provider,
        IJobSourceRunProgress? progress = null, Microsoft.Extensions.Logging.ILogger<JobSourceRunner>? logger = null,
        JobPortal.Application.Abstractions.Persistence.IJobSourceRepository? sources = null)
    {
        var jobs = new JobRepository(f.Context); var unit = new UnitOfWork(f.Context); var fingerprint = new JobFingerprintService(); var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprint, canonical), fingerprint, unit, TimeProvider.System, f.Locks, canonical);
        return new(sources ?? f.Repository, [provider], ingestion, unit, TimeProvider.System, f.Resolver, new ExternalJobNormalizer(), logger,
            autoPublishService: Publisher(f), jobRepository: jobs, runProgress: progress);
    }
    internal sealed class SnapshotProvider(AtsType ats, RawExternalJob[] jobs) : ICompleteExternalJobProvider
    {
        public RawExternalJob[] Jobs { get; set; } = jobs;
        public bool Complete { get; set; } = true;
        public AtsType AtsType => ats;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<RawExternalJob>>(Jobs);
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) => Task.FromResult(new ExternalJobSourceSnapshot(Jobs, 0, Complete));
    }
}
