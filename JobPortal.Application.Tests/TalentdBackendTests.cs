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

public sealed class TalentdBackendTests
{
    [Fact]
    public async Task DisabledTalentdSourceDoesNotPersist()
    {
        using var f = new Fixture(); f.Store.Source.IsEnabled = false;
        var result = await f.Import.ImportAsync(Request(Candidate(null, null, null, "LOW")));
        Assert.Equal("ACQUISITION_SOURCE_DISABLED", Assert.Single(result.Items).ReasonCode);
        Assert.Empty(await f.Db.Jobs.ToArrayAsync());
    }

    [Theory]
    [InlineData("ExternalJobId", null, "TALENTD_IDENTITY_REQUIRED")]
    [InlineData("ApplyUrl", "http://127.0.0.1/private", "TALENTD_UNSAFE_URL")]
    [InlineData("CanonicalJobUrl", "javascript:alert(1)", "TALENTD_UNSAFE_URL")]
    public async Task TechnicalIdentityAndUrlValidationStillApply(string property, string? value, string reason)
    {
        using var f = new Fixture();
        var result = await f.Import.ImportAsync(Request(Change(Candidate(null, null, null, "LOW"), property, value)));
        Assert.Equal(reason, Assert.Single(result.Items).ReasonCode);
        Assert.Empty(await f.Db.Jobs.ToArrayAsync());
    }

    [Theory]
    [InlineData(false, true, "ACQUISITION_AUTOPUBLISH_DISABLED")]
    [InlineData(true, false, "ACQUISITION_PUBLICATION_UNAVAILABLE")]
    public async Task PublicationConfigurationIsRequired(bool enabled, bool available, string reason)
    {
        using var f = new Fixture(); f.Store.Source.AutoPublishEnabled = enabled;
        var importer = new FresherBulkImportService(f.Ingestion, NullLogger<FresherBulkImportService>.Instance, f.Store, available ? f.Automatic : null);
        var result = await importer.ImportAsync(Request(Candidate(null, null, null, "LOW")));
        Assert.Equal(reason, Assert.Single(result.Items).ReasonCode);
        Assert.Empty(await f.Db.Jobs.ToArrayAsync());
    }

    [Fact]
    public async Task RichEvidencePersistsAndSkillsRefreshWithoutDuplicates()
    {
        using var f = new Fixture();
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Candidate("London", 4, "FULL_TIME", "LOW")))!;
        node["Skills"] = JsonSerializer.SerializeToNode(new[] { " C# ", "c#", "SQL" });
        node["SourceMetadata"] = JsonSerializer.SerializeToNode(new { provider = "talentd", talentd = new {
            degree = "B.Tech", responsibilities = "<b>Build services</b>", qualifications = "Degree required", benefits = "Health coverage",
            salary_text = "20K–30K a month", salary = new { currency = "INR", value = new { minValue = 20000, maxValue = 30000, unitText = "MONTH" } },
            batch_years = new[] { 2025, 2026 }, updated_at = "2026-10-15" } });
        var candidate = JsonSerializer.Deserialize<FresherImportCandidate>(node.ToJsonString())!;
        var result = await f.Import.ImportAsync(Request(candidate));
        Assert.Equal(1, result.Published);
        var job = await f.Db.Jobs.SingleAsync();
        Assert.Equal(20000m, job.MinimumSalary); Assert.Equal(30000m, job.MaximumSalary);
        Assert.Equal("INR", job.CurrencyCode); Assert.Equal(SalaryPeriod.Monthly, job.SalaryPeriod);
        Assert.Equal("B.Tech", job.EducationRequirement);
        Assert.Equal("Build services", job.Responsibilities);
        Assert.Equal("Degree required", job.Requirements); Assert.Equal("Health coverage", job.Benefits);
        Assert.Equal(2, job.JobSkills.Count);
        Assert.Contains("batch_years", job.SourceMetadataJson); Assert.Contains("salary_text", job.SourceMetadataJson);
        node["Skills"] = JsonSerializer.SerializeToNode(new[] { "SQL", "Python" });
        await f.Import.ImportAsync(Request(JsonSerializer.Deserialize<FresherImportCandidate>(node.ToJsonString())!));
        Assert.Equal(3, job.JobSkills.Count);
        Assert.Equal(JobStatus.Published, job.Status);
    }

    [Theory]
    [InlineData("remote")]
    [InlineData("hybrid")]
    [InlineData("onsite")]
    public async Task AllArrangementsPublish(string arrangement)
    {
        using var f = new Fixture();
        var result = await f.Import.ImportAsync(Request(Change(Candidate("London", 10, "Contract", "LOW"), nameof(FresherImportCandidate.WorkplaceType), arrangement)));
        Assert.Equal(1, result.Published);
    }

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
        Assert.StartsWith(location ?? "", job.Location ?? "");
        Assert.Equal(candidate.ApplyUrl, job.ApplicationUrl);
        Assert.Equal(candidate.CanonicalJobUrl, job.CanonicalJobUrl);
        Assert.Equal(candidate.DiscoveredUrl, job.DiscoveredUrl);
        Assert.Null(job.MinimumSalary);
        Assert.Null(job.Company.LogoUrl);
        Assert.Null(job.EducationRequirement);
        Assert.Null(job.ExpiresAtUtc);
        Assert.Empty(job.JobSkills);
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
        var receipt = Assert.Single(await f.Store.GetReceiptsAsync(result.RunId!.Value, default));
        Assert.Equal("Published", receipt.Value.PublicationStatus);
        Assert.Null(receipt.Value.PublicationError);
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
    public async Task PayloadReplayIsDurableAndConflictIsRejected()
    {
        using var f = new Fixture();
        var request = Request(Candidate("London", 7, "Contract", "LOW"));
        var first = await f.Import.ImportAsync(request);
        var replay = await f.Import.ImportAsync(request);
        Assert.Equal(first.RunId, replay.RunId);
        Assert.Equal(first.Published, replay.Published);
        Assert.Equal(first.Items, replay.Items);
        var different = new FresherBulkImportRequest { ImportId = request.ImportId, SourceType = "talentd", SourceName = "Test",
            Jobs = [Change(request.Jobs[0], nameof(FresherImportCandidate.Description), "Changed payload")] };
        await Assert.ThrowsAsync<ImportIdPayloadConflictException>(() => f.Import.ImportAsync(different));
        Assert.Single(await f.Db.Jobs.ToArrayAsync());
    }

    private static T Change<T>(T value, string property, string? replacement)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(value))!;
        node[property] = replacement;
        return JsonSerializer.Deserialize<T>(node.ToJsonString())!;
    }

    private static FresherBulkImportRequest Request(params FresherImportCandidate[] jobs) => new()
    { ImportId = "offline-" + Guid.NewGuid().ToString("N"), SourceType = "talentd", SourceName = "Test", Jobs = jobs };

    private static FresherImportCandidate Candidate(string? location, int? experience, string? employment, string confidence) => new()
    {
        ExternalJobId = "talentd:test:1", Title = "Software Engineer", CompanyName = "Example", LocationText = location, MinimumExperienceYears = experience,
        EmploymentType = employment, Description = "Build applications. Responsibilities: testing and deployment.",
        CanonicalJobUrl = "https://new-employer.example/apply/42", ApplyUrl = "https://new-employer.example/apply/42", DiscoveredUrl = "https://publisher.example/software-engineer/",
        SourceMetadata = JsonSerializer.SerializeToElement(new { provider = "talentd", sourceDomain = "publisher.example", extractionConfidence = confidence, destinationVerified = false, autoPublishRequired = true })
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
        private readonly Dictionary<string, (string Fingerprint, AcquisitionImportRun Run)> runs = [];
        private readonly Dictionary<Guid, FresherBulkImportResponse> responses = [];
        private readonly Dictionary<Guid, Dictionary<string, FresherImportReceiptSnapshot>> receiptsByRun = [];
        public Task<(AcquisitionImportRun Run, bool IsReplay)> StartPayloadRunAsync(Guid sourceId, string batchId,
            string correlationId, string payloadFingerprint, string legacyFingerprint, int received, CancellationToken cancellationToken)
        {
            if (runs.TryGetValue(batchId, out var prior))
            {
                if (prior.Fingerprint != payloadFingerprint) throw new ImportIdPayloadConflictException();
                return Task.FromResult((prior.Run, true));
            }
            var run = new AcquisitionImportRun { BatchId = batchId };
            runs[batchId] = (payloadFingerprint, run); receiptsByRun[run.Id] = [];
            return Task.FromResult((run, false));
        }
        public AcquisitionSource Source { get; } = new() { ProviderKey = "talentd", Name = "Website", IsEnabled = true, AutoPublishEnabled = true };
        public List<FresherReceiptInput> Receipts { get; } = [];
        public bool Completed { get; private set; }
        public bool FailPublicationReceiptWrites { get; set; }
        public string? CompanyFailureReason { get; set; }
        public Task UpdatePublicationAsync(Guid runId, string candidateIdentityKey, string status, string? error, CancellationToken cancellationToken)
        {
            if (FailPublicationReceiptWrites) throw new InvalidOperationException("Offline receipt-write fault");
            receiptsByRun[runId][candidateIdentityKey] = receiptsByRun[runId][candidateIdentityKey] with { PublicationStatus = status, PublicationError = error };
            return Task.CompletedTask;
        }
        public Task<FresherCompanyResolution> ResolveCompanyAsync(FresherImportCandidate candidate, CancellationToken cancellationToken) => Task.FromResult(CompanyFailureReason is null ? new FresherCompanyResolution(companyId) : new FresherCompanyResolution(null, CompanyFailureReason));
        public Task<AcquisitionSource?> GetEnabledSourceAsync(string providerKey, CancellationToken cancellationToken) => Task.FromResult<AcquisitionSource?>(Source.IsEnabled && providerKey == Source.ProviderKey ? Source : null);
        public Task<AcquisitionSource?> GetSourceByIdAsync(Guid sourceId, CancellationToken cancellationToken) => Task.FromResult<AcquisitionSource?>(sourceId == Source.Id ? Source : null);
        public Task<(AcquisitionImportRun Run, bool IsReplay)> StartRunAsync(Guid sourceId, string batchId, string correlationId, string requestFingerprint, int received, CancellationToken cancellationToken) => Task.FromResult((new AcquisitionImportRun(), false));
        public Task CompleteRunAsync(Guid runId, FresherBulkImportResponse response, CancellationToken cancellationToken) { Completed = true; responses[runId] = response; runs[response.ImportId].Run.Status = "Completed"; return Task.CompletedTask; }
        public Task<FresherBulkImportResponse?> GetReplayResponseAsync(Guid runId, string batchId, CancellationToken cancellationToken) => Task.FromResult(responses.GetValueOrDefault(runId));
        public Task<IReadOnlyDictionary<string, FresherImportReceiptSnapshot>> GetReceiptsAsync(Guid runId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, FresherImportReceiptSnapshot>>(receiptsByRun[runId]);
        public Task<IFresherAcquisitionChunk> BeginChunkAsync(IReadOnlyCollection<string> externalIdentityLocks, CancellationToken cancellationToken) => Task.FromResult<IFresherAcquisitionChunk>(this);
        public async Task CommitAsync(Guid runId, IReadOnlyCollection<FresherReceiptInput> receipts, CancellationToken cancellationToken) { await db.SaveChangesAsync(cancellationToken); Receipts.AddRange(receipts);
            foreach (var receipt in receipts) receiptsByRun[runId][receipt.IdentityKey] = new(receipt.Outcome, receipt.PublicationRequested ? "Pending" : "NotRequested", null);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
