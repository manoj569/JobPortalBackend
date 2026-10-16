using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class WebsiteCrawlerContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
    private static string Payload => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "WebsiteCrawler", "import-contract.json"));
    private static FresherBulkImportRequest Request(string? change = null)
    {
        var node = JsonNode.Parse(Payload)!;
        var job = node["jobs"]![0]!;
        switch (change)
        {
            case "company": job["companyName"] = ""; break;
            case "url": job["applyUrl"] = "http://localhost/apply"; break;
            case "id": job["externalJobId"] = new string('x', 256); break;
            case "expired": job["expiresAt"] = "2000-01-01T00:00:00Z"; break;
            case "unverified": job["sourceMetadata"]!["destinationVerified"] = false; break;
            case "medium": job["sourceMetadata"]!["extractionConfidence"] = "MEDIUM"; break;
            case "provenance": job["sourceMetadata"]!["sourceDomain"] = "wrong.example.org"; break;
        }
        return JsonSerializer.Deserialize<FresherBulkImportRequest>(node.ToJsonString(), WebJson)!;
    }

    [Fact]
    public async Task ExactPythonPayloadUsesRealDtoValidationMapperAndSourceWithoutPublishing()
    {
        var request = Request();
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
        Assert.True(Validator.TryValidateObject(request.Jobs[0], new ValidationContext(request.Jobs[0]), [], true));
        var ingestion = new Recorder();
        var store = new MemoryStore();
        var publisher = new Publisher();
        var service = new FresherBulkImportService(ingestion, NullLogger<FresherBulkImportService>.Instance, store, publisher, new FixedClock());
        var response = await service.ImportAsync(request);
        Assert.Equal(1, response.Created);
        Assert.Equal(0, response.Published);
        Assert.Equal(0, publisher.Calls);
        var raw = Assert.Single(ingestion.Jobs);
        Assert.Equal("National Highways Authority of India (NHAI)", raw.CompanyName);
        Assert.Null(raw.CompanyWebsiteUrl);
        Assert.Equal(request.Jobs[0].DiscoveredUrl, raw.DiscoveredUrl);
        Assert.Equal(request.Jobs[0].ApplyUrl, raw.ApplicationUrl);
        Assert.Equal(store.Source.Id, raw.AcquisitionSourceId);
        Assert.Equal("Multiple project locations across India", raw.Location);
        Assert.Contains("IN", raw.CountryCodes!);
        Assert.Equal(JobPortal.Domain.Enums.EmploymentType.Internship, raw.EmploymentType);
        using var metadata = JsonDocument.Parse(raw.SourceMetadataJson!);
        var discovery = metadata.RootElement.GetProperty("discovery");
        Assert.Equal("website-crawler", discovery.GetProperty("provider").GetString());
        Assert.Equal("placementkit.in", discovery.GetProperty("sourceDomain").GetString());
        Assert.Equal("HIGH", discovery.GetProperty("extractionConfidence").GetString());
        Assert.False(Assert.Single(store.Inputs).PublicationRequested);
        var replay = await service.ImportAsync(request);
        Assert.Equal(response.RunId, replay.RunId);
        Assert.Single(ingestion.Jobs);
        Assert.Equal(response.Created, replay.Created);
    }

    [Theory]
    [InlineData("Multiple project locations across india", "Multiple project locations across india")]
    [InlineData("Pune", "Pune, India")]
    [InlineData("IndiaTech campus", "IndiaTech campus, India")]
    public async Task WebsiteLocationOmitsOnlyAnAlreadyPresentCountryToken(string location, string expected)
    {
        var node = JsonNode.Parse(Payload)!;
        node["jobs"]![0]!["locationText"] = location;
        var request = JsonSerializer.Deserialize<FresherBulkImportRequest>(node.ToJsonString(), WebJson)!;
        var ingestion = new Recorder();
        var service = new FresherBulkImportService(ingestion, NullLogger<FresherBulkImportService>.Instance,
            new MemoryStore(), clock: new FixedClock());
        await service.ImportAsync(request);
        Assert.Equal(expected, Assert.Single(ingestion.Jobs).Location);
    }

    [Theory]
    [InlineData("id", "FIELD_LENGTH_EXCEEDED")]
    [InlineData("provenance", "WEBSITE_PROVENANCE_INVALID")]
    public async Task UnsafeWebsiteRowsAreRejectedBeforeIngestion(string change, string reason)
    {
        var ingestion = new Recorder();
        var service = new FresherBulkImportService(ingestion, NullLogger<FresherBulkImportService>.Instance, clock: new FixedClock());
        var result = await service.ImportAsync(Request(change));
        Assert.Empty(ingestion.Jobs);
        Assert.Equal(reason, Assert.Single(result.Items).ReasonCode);
    }

    [Fact]
    public async Task DifferentProviderSameCanonicalApplicationDoesNotCreateAnotherJob()
    {
        using var fixture = new LocalFixture();
        var jobs = new JobRepository(fixture.Context);
        var fingerprints = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(fixture.Context),
            new CategoryManagementRepository(fixture.Context), new JobDeduplicationService(jobs, fingerprints, canonicalizer),
            fingerprints, new UnitOfWork(fixture.Context), new FixedClock(), fixture.Locks, canonicalizer);
        var serp = new AcquisitionSource { ProviderKey = "serpapi-google-jobs", Name = "SerpApi" };
        var website = new AcquisitionSource { ProviderKey = "website-crawler", Name = "Generic Website Crawler" };
        fixture.Context.AddRange(serp, website);
        await fixture.Context.SaveChangesAsync();
        var raw = new RawExternalJob { Title = "Graduate Engineer", CompanyId = fixture.Company.Id,
            CompanyName = fixture.Company.Name, Description = "Graduate opportunity in India.", Location = "Pune, India", CountryCodes = ["IN"],
            ApplicationUrl = "https://careers.example.com/jobs/123", CanonicalJobUrl = "https://careers.example.com/jobs/123",
            AcquisitionSourceId = serp.Id, AcquisitionExternalId = "existing-serp-id" };
        var first = await ingestion.IngestAsync(raw);
        var second = await ingestion.IngestAsync(raw with { AcquisitionSourceId = website.Id, AcquisitionExternalId = "website-crawler:new-id" });
        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal(1, await fixture.Context.Jobs.CountAsync());
        Assert.Equal(serp.Id, (await fixture.Context.Jobs.SingleAsync()).AcquisitionSourceId);
    }

    [Fact]
    public void SourceMigrationEnablesAcquisitionButDoesNotEnablePublication()
    {
        var migration = new JobPortal.Persistence.Postgres.Migrations.AddWebsiteCrawlerAcquisitionSource();
        var insert = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.InsertDataOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal("AcquisitionSources", insert.Table);
        Assert.Equal("website-crawler", insert.Values[0, 1]);
        Assert.Equal("Generic Website Crawler", insert.Values[0, 2]);
        Assert.Equal(true, insert.Values[0, 3]);
        Assert.Equal(false, insert.Values[0, 4]);
    }

    [Fact]
    public async Task CompanyResolutionUsesSourceCompanyIdentityNotPublisherOrPortalHost()
    {
        using var fixture = new LocalFixture();
        var store = new FresherAcquisitionStore(fixture.Context);
        var candidate = new FresherImportCandidate { CompanyName = fixture.Company.Name,
            DiscoveredUrl = "https://publisher.example.org/internship", ApplyUrl = "https://portal.example.org/" };
        var result = await store.ResolveCompanyAsync(candidate, default);
        Assert.Equal(fixture.Company.Id, result.CompanyId);
        Assert.Null(fixture.Company.WebsiteUrl);
        Assert.Single(await fixture.Context.Companies.ToArrayAsync());
    }

    [Fact]
    public async Task ActualAutoPublishServiceRefusesDisabledWebsitePublication()
    {
        using var fixture = new LocalFixture();
        var jobs = new JobRepository(fixture.Context);
        var fingerprints = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(fixture.Context),
            new CategoryManagementRepository(fixture.Context), new JobDeduplicationService(jobs, fingerprints, canonicalizer),
            fingerprints, new UnitOfWork(fixture.Context), new FixedClock(), fixture.Locks, canonicalizer);
        var store = new MemoryStore();
        fixture.Context.Add(store.Source);
        await fixture.Context.SaveChangesAsync();
        var created = await ingestion.IngestAsync(new RawExternalJob { Title = "Graduate Internship", CompanyName = fixture.Company.Name,
            CompanyId = fixture.Company.Id, Description = "Graduate internship in India.", Location = "India", CountryCodes = ["IN"],
            ApplicationUrl = "https://careers.example.com/jobs/456", AcquisitionSourceId = store.Source.Id, AcquisitionExternalId = "website-test" });
        var publisher = new JobAutoPublishService(jobs, null!, null!,
            Microsoft.Extensions.Options.Options.Create(new JobPortal.Application.Features.JobAggregation.JobAggregationOptions()),
            new FixedClock(), new JobSourceRepository(fixture.Context), acquisitionSources: store);
        var result = await publisher.TryPublishAsync(created.JobId!.Value);
        Assert.Equal(JobAutoPublishOutcome.Disabled, result.Outcome);
        Assert.Equal(JobPortal.Domain.Enums.JobStatus.Draft, (await fixture.Context.Jobs.SingleAsync()).Status);
    }

    private sealed class LocalFixture : IDisposable
    {
        public JobPortal.Persistence.Context.JobPortalDbContext Context { get; } = new(new DbContextOptionsBuilder<JobPortal.Persistence.Context.JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Company Company { get; } = new() { Name = "Acme", NormalizedName = "ACME", Slug = "acme" };
        public LocalLocks Locks { get; } = new();
        public LocalFixture()
        {
            Context.Add(Company);
            Context.SaveChanges();
        }
        public void Dispose() => Context.Dispose();
    }
    private sealed class LocalLocks : IExternalJobCreationLock
    {
        public Task<IAsyncDisposable> AcquireAsync(string? canonicalUrl, string fingerprintHash, CancellationToken cancellationToken = default) => Task.FromResult<IAsyncDisposable>(NoopAsyncDisposable.Instance);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Recorder : IJobIngestionService
    {
        public List<RawExternalJob> Jobs { get; } = [];
        public Task<JobIngestionResult> IngestAsync(RawExternalJob rawJob, CancellationToken cancellationToken = default)
        {
            Jobs.Add(rawJob);
            return Task.FromResult(new JobIngestionResult { JobId = Guid.NewGuid(), Outcome = JobIngestionOutcome.Created });
        }
    }
    private sealed class Publisher : IJobAutoPublishService
    {
        public int Calls { get; private set; }
        public Task<JobAutoPublishResult> TryPublishAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Website source must not request publication.");
        }
    }
    private sealed class MemoryStore : IFresherAcquisitionStore, IFresherAcquisitionChunk
    {
        public AcquisitionSource Source { get; } = new() { ProviderKey = "website-crawler", Name = "Generic Website Crawler", AutoPublishEnabled = false };
        public List<FresherReceiptInput> Inputs { get; } = [];
        private readonly AcquisitionImportRun run = new();
        private FresherBulkImportResponse? response;
        public Task<FresherCompanyResolution> ResolveCompanyAsync(FresherImportCandidate candidate, CancellationToken cancellationToken) => Task.FromResult(new FresherCompanyResolution(Guid.NewGuid()));
        public Task<AcquisitionSource?> GetEnabledSourceAsync(string providerKey, CancellationToken cancellationToken) => Task.FromResult<AcquisitionSource?>(providerKey == Source.ProviderKey ? Source : null);
        public Task<AcquisitionSource?> GetSourceByIdAsync(Guid sourceId, CancellationToken cancellationToken) => Task.FromResult<AcquisitionSource?>(Source);
        public Task<(AcquisitionImportRun Run, bool IsReplay)> StartRunAsync(Guid sourceId, string batchId, string correlationId, string requestFingerprint, int received, CancellationToken cancellationToken) => Task.FromResult((run, response is not null));
        public Task CompleteRunAsync(Guid runId, FresherBulkImportResponse result, CancellationToken cancellationToken)
        {
            response = result;
            run.Status = "Completed";
            return Task.CompletedTask;
        }
        public Task<FresherBulkImportResponse?> GetReplayResponseAsync(Guid runId, string batchId, CancellationToken cancellationToken) => Task.FromResult(response);
        public Task<IReadOnlyDictionary<string, FresherImportReceiptSnapshot>> GetReceiptsAsync(Guid runId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, FresherImportReceiptSnapshot>>(Inputs.ToDictionary(x => x.IdentityKey, x => new FresherImportReceiptSnapshot(x.Outcome, "NotRequested", null)));
        public Task<IFresherAcquisitionChunk> BeginChunkAsync(IReadOnlyCollection<string> externalIdentityLocks, CancellationToken cancellationToken) => Task.FromResult<IFresherAcquisitionChunk>(this);
        public Task CommitAsync(Guid runId, IReadOnlyCollection<FresherReceiptInput> receipts, CancellationToken cancellationToken)
        {
            Inputs.AddRange(receipts);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
