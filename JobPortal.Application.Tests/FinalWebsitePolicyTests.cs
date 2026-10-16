using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class FinalWebsitePolicyTests
{
    [Theory]
    [InlineData(null, null, null, "LOW")]
    [InlineData("London, UK", 10, "FullTime", "MEDIUM")]
    [InlineData("Remote", 3, "Contract", "LOW")]
    [InlineData("India", 0, "Internship", "HIGH")]
    [InlineData("New York, US", null, "Apprenticeship", "LOW")]
    public async Task BulkImportPersistsPublishesAndIsPublicWithoutApproval(string? location, int? experience, string? employment, string confidence)
    {
        using var f = new Fixture();
        var candidate = Candidate(location, experience, employment, confidence);
        var request = Request(candidate);
        var result = await f.Import.ImportAsync(request);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Published);
        var job = await f.Db.Jobs.SingleAsync();
        Assert.Equal(JobStatus.Published, job.Status);
        Assert.Equal(location, job.Location);
        Assert.Equal(experience, job.MinimumExperienceYears);
        if (employment == "FullTime") Assert.Equal(EmploymentType.FullTime, job.EmploymentType);
        Assert.False(job.Company.IsVerified);
        Assert.Null(job.CategoryId);
        Assert.Contains("Responsibilities", job.Description, StringComparison.Ordinal);
        var page = await new PublicJobRepository(f.Db, TimeProvider.System).SearchAsync(new PublicJobQuery());
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(job.Id, Assert.Single(page.Items).Id);
        Assert.Single(f.Store.Receipts);
        Assert.True(f.Store.Receipts[0].PublicationRequested);
    }

    [Fact]
    public async Task ExplicitForeignCountryCodeIsPreservedWithoutAnIndiaDefault()
    {
        using var f = new Fixture();
        var candidate = Change(Candidate("London", 5, "FullTime", "LOW"), nameof(FresherImportCandidate.Country), "GB");
        await f.Import.ImportAsync(Request(candidate));
        var job = await f.Db.Jobs.SingleAsync();
        using var metadata = JsonDocument.Parse(job.SourceMetadataJson!);
        Assert.Equal("GB", Assert.Single(metadata.RootElement.GetProperty("countryCodes").EnumerateArray()).GetString());
        Assert.Equal(JobStatus.Published, job.Status);
    }

    [Fact]
    public async Task TwoVacanciesSharingAnArticleAreDistinctAndRefreshInPlace()
    {
        using var f = new Fixture();
        var first = Candidate(null, null, null, "LOW");
        var second = Change(Candidate(null, null, null, "LOW"), nameof(FresherImportCandidate.ExternalJobId), "second-vacancy");
        await f.Import.ImportAsync(Request(first, second));
        Assert.Equal(2, await f.Db.Jobs.CountAsync());
        // A later observation has another payload ID but the same vacancy identities.
        var again = await f.Import.ImportAsync(Request(Change(first, nameof(FresherImportCandidate.Description), "Updated source description."), second));
        Assert.Equal(2, await f.Db.Jobs.CountAsync());
        Assert.True(again.Updated >= 1);
        Assert.Equal(2, again.Updated + again.Existing);
    }

    [Fact]
    public async Task LegacyWebsiteIdentityRefreshesOneJobWithoutAlteringReceipts()
    {
        using var f = new Fixture();
        var company = await f.Db.Companies.SingleAsync();
        var legacy = await f.Ingestion.IngestAsync(new RawExternalJob { Title = "Software Engineer", CompanyName = company.Name,
            CompanyId = company.Id, AcquisitionSourceId = f.Store.Source.Id, AcquisitionProviderKey = "website-crawler",
            AcquisitionExternalId = "WEBSITE-CRAWLER:LEGACY", Description = "Old description.",
            DiscoveredUrl = "https://publisher.example/software-engineer/", ApplicationUrl = "https://publisher.example/software-engineer/" });
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Candidate(null, null, null, "LOW")))!;
        node["SourceMetadata"]!["legacyExternalId"] = "website-crawler:legacy";
        var result = await f.Import.ImportAsync(Request(JsonSerializer.Deserialize<FresherImportCandidate>(node.ToJsonString())!));
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Created);
        Assert.Equal(legacy.JobId, (await f.Db.Jobs.SingleAsync()).Id);
    }

    [Theory]
    [InlineData("COMPANY_OWNER_UNAVAILABLE")]
    [InlineData("COMPANY_CREATION_CONFLICT")]
    [InlineData("COMPANY_DOMAIN_AMBIGUOUS")]
    public async Task WebsiteCompanyInfrastructureFailureIsUncommittedAndRecoverable(string reason)
    {
        using var f = new Fixture();
        f.Store.CompanyFailureReason = reason;
        var result = await f.Import.ImportAsync(Request(Candidate(null, null, null, "LOW")));
        Assert.Equal("ITEM_NOT_COMMITTED", Assert.Single(result.Items).ReasonCode);
        Assert.Empty(f.Store.Receipts);
        Assert.False(f.Store.Completed);
        Assert.Empty(await f.Db.Jobs.ToArrayAsync());
    }

    [Fact]
    public async Task GenericCompanyNotResolvedFailureRemainsTerminalAndReceipted()
    {
        using var f = new Fixture();
        f.Store.CompanyFailureReason = "COMPANY_NOT_RESOLVED";
        var result = await f.Import.ImportAsync(Request(Candidate(null, null, null, "LOW")));

        Assert.Equal("COMPANY_NOT_RESOLVED", Assert.Single(result.Items).ReasonCode);
        Assert.Single(f.Store.Receipts);
        Assert.True(f.Store.Completed);
        Assert.Empty(await f.Db.Jobs.ToArrayAsync());
    }

    [Fact]
    public async Task PublicationReceiptWriteFailureKeepsCommittedJobOutcomeAndPayloadRecoverable()
    {
        using var f = new Fixture();
        f.Store.FailPublicationReceiptWrites = true;
        var result = await f.Import.ImportAsync(Request(Candidate(null, null, null, "LOW")));
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Failed);
        Assert.True(result.PublicationPending);
        Assert.Single(await f.Db.Jobs.ToArrayAsync());
        Assert.Equal("Created", Assert.Single(f.Store.Receipts).Outcome.Outcome);
    }

    [Theory]
    [InlineData(false, true, "ACQUISITION_AUTOPUBLISH_DISABLED")]
    [InlineData(true, false, "ACQUISITION_PUBLICATION_UNAVAILABLE")]
    public async Task RequiredPublicationConfigurationFailsBeforePersistence(bool enabled, bool available, string reason)
    {
        using var f = new Fixture();
        f.Store.Source.AutoPublishEnabled = enabled;
        var importer = new FresherBulkImportService(f.Ingestion, NullLogger<FresherBulkImportService>.Instance, f.Store, available ? f.Automatic : null);
        var result = await importer.ImportAsync(Request(Candidate(null, null, null, "LOW")));
        Assert.Null(result.RunId);
        Assert.Equal(reason, Assert.Single(result.Items).ReasonCode);
        Assert.Empty(await f.Db.Jobs.ToArrayAsync());
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(10, 3, 0)]
    public async Task PublicationFailureRetriesAreBoundedAndNeverReingest(int failures, int attempts, int published)
    {
        using var f = new Fixture();
        var retry = new RetryPublisher(f.Automatic, failures);
        var importer = new FresherBulkImportService(f.Ingestion, NullLogger<FresherBulkImportService>.Instance, f.Store, retry);
        var result = await importer.ImportAsync(Request(Candidate(null, null, null, "LOW")));
        Assert.Equal(1, result.Created);
        Assert.Equal(published, result.Published);
        Assert.Equal(published == 0, result.PublicationPending);
        Assert.Equal(attempts, retry.Calls);
        Assert.Single(await f.Db.Jobs.ToArrayAsync());
    }

    [Fact]
    public async Task ExplicitEditorialSuppressionIsNotReportedAsPublished()
    {
        using var f = new Fixture();
        await f.Import.ImportAsync(Request(Candidate(null, null, null, "LOW")));
        var job = await f.Db.Jobs.SingleAsync();
        job.IsHidden = true;
        await f.Db.SaveChangesAsync();
        Assert.Equal(JobAutoPublishOutcome.NeedsReview, (await f.Automatic.TryPublishAsync(job.Id)).Outcome);
        Assert.True((await f.Db.Jobs.SingleAsync()).IsHidden);
    }

    [Fact]
    public async Task ExistingCategoryIsMappedWithoutCreatingCategories()
    {
        using var f = new Fixture();
        var category = new Category { Name = "Software Engineering", Slug = "software-engineering" };
        f.Db.Add(category);
        await f.Db.SaveChangesAsync();
        await f.Import.ImportAsync(Request(Candidate(null, null, null, "LOW")));
        Assert.Equal(category.Id, (await f.Db.Jobs.SingleAsync()).CategoryId);
    }

    [Fact]
    public async Task MissingCompanyAndDescriptionRemainUnknownWithoutInventedFacts()
    {
        using var f = new Fixture(unknownCompany: true);
        var result = await f.Import.ImportAsync(Request(Change(Change(Candidate(null, null, null, "LOW"), nameof(FresherImportCandidate.CompanyName), null), nameof(FresherImportCandidate.Description), "")));
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Published);
        var job = await f.Db.Jobs.SingleAsync();
        Assert.Equal("", job.Company.Name);
        Assert.Equal("", job.Description);
        Assert.Null(job.Location);
    }

    [Fact]
    public async Task FailedItemDoesNotRollBackOtherWebsiteItemsOrClaimCompletion()
    {
        using var f = new Fixture();
        var failing = new FailOne(f.Ingestion);
        var importer = new FresherBulkImportService(failing, NullLogger<FresherBulkImportService>.Instance, f.Store, f.Automatic);
        var result = await importer.ImportAsync(Request(Change(Candidate(null, null, null, "LOW"), nameof(FresherImportCandidate.Title), "Bad item"), Candidate(null, null, null, "LOW")));
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Failed);
        Assert.Equal("ITEM_NOT_COMMITTED", result.Items[0].ReasonCode);
        Assert.Single(await f.Db.Jobs.ToArrayAsync());
        Assert.Single(f.Store.Receipts);
        Assert.False(f.Store.Completed);
    }

    [Fact]
    public async Task UnrelatedProviderStillEnforcesExistingContentPolicy()
    {
        using var f = new Fixture();
        f.Store.Source.ProviderKey = "serpapi-google-jobs";
        var request = Change(Request(Candidate("London, UK", 10, "FullTime", "LOW")), nameof(FresherBulkImportRequest.SourceType), "serpapi-google-jobs");
        var result = await f.Import.ImportAsync(request);
        Assert.Equal(1, result.RejectedGeography);
        Assert.Empty(await f.Db.Jobs.ToArrayAsync());
    }

    private static T Change<T>(T value, string property, string? replacement)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(value))!;
        node[property] = replacement;
        return JsonSerializer.Deserialize<T>(node.ToJsonString())!;
    }

    private static FresherBulkImportRequest Request(params FresherImportCandidate[] jobs) => new()
    { ImportId = "offline-" + Guid.NewGuid().ToString("N"), SourceType = "website-crawler", SourceName = "Test", Jobs = jobs };

    private static FresherImportCandidate Candidate(string? location, int? experience, string? employment, string confidence) => new()
    {
        Title = "Software Engineer", CompanyName = "Example", LocationText = location, MinimumExperienceYears = experience,
        EmploymentType = employment, Description = "Build applications. Responsibilities: testing and deployment.",
        ApplyUrl = "https://publisher.example/software-engineer/", DiscoveredUrl = "https://publisher.example/software-engineer/",
        SourceMetadata = JsonSerializer.SerializeToElement(new { provider = "website-crawler", sourceDomain = "publisher.example", extractionConfidence = confidence, destinationVerified = false, autoPublishRequired = true })
    };

    private sealed class Fixture : IDisposable
    {
        public JobPortalDbContext Db { get; } = new(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Store Store { get; }
        public JobIngestionService Ingestion { get; }
        public JobAutoPublishService Automatic { get; }
        public FresherBulkImportService Import { get; }
        public Fixture(bool unknownCompany = false)
        {
            var company = new Company { Name = unknownCompany ? "" : "Example", NormalizedName = "EXAMPLE", Slug = "example" };
            Store = new(Db, company.Id);
            Db.AddRange(company, Store.Source);
            Db.SaveChanges();
            var jobs = new JobRepository(Db);
            var companies = new CompanyManagementRepository(Db);
            var categories = new CategoryManagementRepository(Db);
            var unit = new UnitOfWork(Db);
            var fingerprint = new JobFingerprintService();
            var canonicalizer = new UrlCanonicalizer();
            Ingestion = new(jobs, companies, categories, new JobDeduplicationService(jobs, fingerprint, canonicalizer), fingerprint, unit, TimeProvider.System, new Locks(), canonicalizer, acquisitionSources: Store);
            var service = new JobService(jobs, unit, new Audit(), new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
                new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), TimeProvider.System, acquisitionSources: Store);
            Automatic = new(jobs, new JobQualityGate(), service, Options.Create(new JobAggregationOptions()), TimeProvider.System, new JobSourceRepository(Db), acquisitionSources: Store);
            Import = new(Ingestion, NullLogger<FresherBulkImportService>.Instance, Store, Automatic, categories: categories);
        }
        public void Dispose() => Db.Dispose();
    }
    private sealed class Audit : IAuditWriter
    { public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class Locks : IExternalJobCreationLock
    { public Task<IAsyncDisposable> AcquireAsync(string? canonicalUrl, string fingerprintHash, CancellationToken cancellationToken = default) => Task.FromResult<IAsyncDisposable>(NoopAsyncDisposable.Instance); }
    private sealed class FailOne(IJobIngestionService inner) : IJobIngestionService
    {
        public Task<JobIngestionResult> IngestAsync(RawExternalJob rawJob, CancellationToken cancellationToken = default) => rawJob.Title == "Bad item"
            ? throw new InvalidOperationException("Offline failure") : inner.IngestAsync(rawJob, cancellationToken);
    }
    private sealed class RetryPublisher(IJobAutoPublishService inner, int failures) : IJobAutoPublishService
    {
        public int Calls { get; private set; }
        public Task<JobAutoPublishResult> TryPublishAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls <= failures) throw new InvalidOperationException("Offline publication fault");
            return inner.TryPublishAsync(jobId, cancellationToken);
        }
    }
    private sealed class Store(JobPortalDbContext db, Guid companyId) : IFresherAcquisitionStore, IFresherAcquisitionChunk
    {
        public AcquisitionSource Source { get; } = new() { ProviderKey = "website-crawler", Name = "Website", IsEnabled = true, AutoPublishEnabled = true };
        public List<FresherReceiptInput> Receipts { get; } = [];
        public bool Completed { get; private set; }
        public bool FailPublicationReceiptWrites { get; set; }
        public string? CompanyFailureReason { get; set; }
        public Task UpdatePublicationAsync(Guid runId, string candidateIdentityKey, string status, string? error, CancellationToken cancellationToken)
        {
            if (FailPublicationReceiptWrites) throw new InvalidOperationException("Offline receipt-write fault");
            return Task.CompletedTask;
        }
        public Task<FresherCompanyResolution> ResolveCompanyAsync(FresherImportCandidate candidate, CancellationToken cancellationToken) => Task.FromResult(CompanyFailureReason is null ? new FresherCompanyResolution(companyId) : new FresherCompanyResolution(null, CompanyFailureReason));
        public Task<AcquisitionSource?> GetEnabledSourceAsync(string providerKey, CancellationToken cancellationToken) => Task.FromResult<AcquisitionSource?>(Source.IsEnabled && providerKey == Source.ProviderKey ? Source : null);
        public Task<AcquisitionSource?> GetSourceByIdAsync(Guid sourceId, CancellationToken cancellationToken) => Task.FromResult<AcquisitionSource?>(sourceId == Source.Id ? Source : null);
        public Task<(AcquisitionImportRun Run, bool IsReplay)> StartRunAsync(Guid sourceId, string batchId, string correlationId, string requestFingerprint, int received, CancellationToken cancellationToken) => Task.FromResult((new AcquisitionImportRun(), false));
        public Task CompleteRunAsync(Guid runId, FresherBulkImportResponse response, CancellationToken cancellationToken) { Completed = true; return Task.CompletedTask; }
        public Task<FresherBulkImportResponse?> GetReplayResponseAsync(Guid runId, string batchId, CancellationToken cancellationToken) => Task.FromResult<FresherBulkImportResponse?>(null);
        public Task<IReadOnlyDictionary<string, FresherImportReceiptSnapshot>> GetReceiptsAsync(Guid runId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, FresherImportReceiptSnapshot>>(new Dictionary<string, FresherImportReceiptSnapshot>());
        public Task<IFresherAcquisitionChunk> BeginChunkAsync(IReadOnlyCollection<string> externalIdentityLocks, CancellationToken cancellationToken) => Task.FromResult<IFresherAcquisitionChunk>(this);
        public async Task CommitAsync(Guid runId, IReadOnlyCollection<FresherReceiptInput> receipts, CancellationToken cancellationToken) { await db.SaveChangesAsync(cancellationToken); Receipts.AddRange(receipts); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
