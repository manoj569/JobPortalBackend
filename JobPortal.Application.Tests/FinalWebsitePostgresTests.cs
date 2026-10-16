using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class FinalWebsitePostgresTests
{
    [WebsitePostgresFact]
    public async Task NewCompanyAndUnknownEmployerPersistPublishAndRefreshWithRealForeignKeys()
    {
        var connection = Environment.GetEnvironmentVariable("CAREERHARBOR_WEBSITE_TEST_POSTGRES")!;
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        Assert.Contains(parsed.Host, new[] { "localhost", "127.0.0.1" });
        Assert.EndsWith("_test", parsed.Database, StringComparison.Ordinal);
        await using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql(connection, pg => pg.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        await db.Database.MigrateAsync();
        var source = await db.AcquisitionSources.SingleAsync(x => x.ProviderKey == "website-crawler");
        source.IsEnabled = source.AutoPublishEnabled = true; // Disposable local test only.
        var owner = new User { Email = Guid.NewGuid().ToString("N") + "@example.test", PasswordHash = "test-only",
            FirstName = "Test", LastName = "Administrator", RoleId = SystemRoleIds.Administrator, Status = UserStatus.Active };
        owner.NormalizedEmail = owner.Email.ToUpperInvariant();
        db.Add(owner);
        await db.SaveChangesAsync();
        var store = new FresherAcquisitionStore(db);
        var jobs = new JobRepository(db);
        var companies = new CompanyManagementRepository(db);
        var categories = new CategoryManagementRepository(db);
        var unit = new UnitOfWork(db);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        var locks = new PostgresExternalJobCreationLock(configuration);
        var fingerprint = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, companies, categories, new JobDeduplicationService(jobs, fingerprint, canonicalizer),
            fingerprint, unit, TimeProvider.System, locks, canonicalizer, acquisitionSources: store);
        var jobService = new JobService(jobs, unit, new Audit(), new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
            new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), TimeProvider.System, acquisitionSources: store);
        var publisher = new JobAutoPublishService(jobs, new JobQualityGate(), jobService, Options.Create(new JobAggregationOptions()),
            TimeProvider.System, new JobSourceRepository(db), publicationLock: locks, acquisitionSources: store);
        var importer = new FresherBulkImportService(ingestion, NullLogger<FresherBulkImportService>.Instance, store, publisher, categories: categories);
        var token = Guid.NewGuid().ToString("N");
        var domain = "publisher-" + token + ".example.test";
        var companyName = "Website fixture – " + token;
        FresherImportCandidate Candidate(string? company, string? id) => new()
        {
            Title = "Senior Engineer", CompanyName = company, ExternalJobId = id, MinimumExperienceYears = company is null ? null : 10,
            CompanyWebsiteUrl = company is null ? null : "https://employer-" + token + ".example.test",
            LocationText = company is null ? null : "London, UK", Description = company is null ? "" : "Responsibilities: build reliable systems. Qualifications: engineering experience.",
            DiscoveredUrl = "https://" + domain + "/engineer/", ApplyUrl = "https://" + domain + "/engineer/",
            SourceMetadata = JsonSerializer.SerializeToElement(new { provider = "website-crawler", sourceDomain = domain, extractionConfidence = "LOW", destinationVerified = false, autoPublishRequired = true })
        };
        async Task<FresherBulkImportResponse> Import(params FresherImportCandidate[] rows) => await importer.ImportAsync(new()
            { ImportId = "offline-" + Guid.NewGuid().ToString("N"), SourceType = "website-crawler", SourceName = "Test", Jobs = rows });
        var first = await Import(Candidate(companyName, "one-" + token), Candidate(companyName, "two-" + token), Candidate(null, null));
        Assert.Equal(3, first.Created);
        Assert.Equal(3, first.Published);
        var rows = await db.Jobs.Where(x => x.DiscoveredUrl!.Contains(domain)).Include(x => x.Company).ToArrayAsync();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, x => Assert.Equal(JobStatus.Published, x.Status));
        Assert.Equal(2, rows.Select(x => x.CompanyId).Distinct().Count());
        Assert.Equal(1, await db.Companies.CountAsync(x => x.Name == companyName));
        var employer = await db.Companies.SingleAsync(x => x.Name == companyName);
        Assert.False(employer.IsVerified);
        Assert.Equal("https://employer-" + token + ".example.test", employer.WebsiteUrl);
        var minimal = Assert.Single(rows, x => x.Company.Name == "");
        Assert.Null(minimal.Location);
        Assert.Null(minimal.MinimumExperienceYears);
        Assert.Empty(minimal.Description);
        var again = await Import(Candidate(companyName, "one-" + token), Candidate(companyName, "two-" + token), Candidate(null, null));
        Assert.Equal(3, again.Updated + again.Existing);
        Assert.Equal(0, again.Created);
        Assert.Equal(3, await db.Jobs.CountAsync(x => x.DiscoveredUrl!.Contains(domain)));
        var publicJobs = await new PublicJobRepository(db, TimeProvider.System).SearchAsync(new PublicJobQuery { Search = "Senior Engineer" });
        Assert.True(publicJobs.TotalCount >= 3);
        // Mixed durable receipts resume publication only, including terminal rejection.
        var invalidNode = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Candidate(companyName, "invalid-" + token)))!;
        invalidNode["Title"] = "";
        var replayRequest = new FresherBulkImportRequest { ImportId = "offline-publish-" + Guid.NewGuid().ToString("N"),
            SourceType = "website-crawler", SourceName = "Test", Jobs = [Candidate(companyName, "pending-one-" + token),
                Candidate(companyName, "pending-two-" + token), JsonSerializer.Deserialize<FresherImportCandidate>(invalidNode.ToJsonString())!] };
        var failingPublisher = new FailingPublisher();
        var deferred = await new FresherBulkImportService(ingestion, NullLogger<FresherBulkImportService>.Instance, store, failingPublisher, categories: categories).ImportAsync(replayRequest);
        Assert.True(deferred.PublicationPending);
        Assert.Equal(2, deferred.Created);
        Assert.Equal(1, deferred.RejectedInvalidData);
        Assert.Equal(6, failingPublisher.Calls);
        Assert.Equal("AwaitingPublication", (await db.AcquisitionImportRuns.SingleAsync(x => x.Id == deferred.RunId)).Status);
        var jobCount = await db.Jobs.CountAsync(x => x.DiscoveredUrl!.Contains(domain));
        var forbiddenIngestion = new ForbiddenIngestion();
        var recovered = await new FresherBulkImportService(forbiddenIngestion, NullLogger<FresherBulkImportService>.Instance,
            store, publisher, categories: categories).ImportAsync(replayRequest);
        Assert.False(recovered.PublicationPending);
        Assert.Equal(deferred.RunId, recovered.RunId);
        Assert.Equal(2, recovered.Published);
        Assert.Equal(1, recovered.RejectedInvalidData);
        Assert.Equal(0, forbiddenIngestion.Calls);
        Assert.Equal(jobCount, await db.Jobs.CountAsync(x => x.DiscoveredUrl!.Contains(domain)));
        Assert.Equal("Completed", (await db.AcquisitionImportRuns.SingleAsync(x => x.Id == recovered.RunId)).Status);

    }
    private sealed class FailingPublisher : IJobAutoPublishService
    {
        public int Calls { get; private set; }
        public Task<JobAutoPublishResult> TryPublishAsync(Guid jobId, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Offline publication fault"); }
    }
    private sealed class ForbiddenIngestion : IJobIngestionService
    {
        public int Calls { get; private set; }
        public Task<JobIngestionResult> IngestAsync(RawExternalJob rawJob, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Durable receipts must bypass ingestion"); }
    }
    private sealed class Audit : IAuditWriter
    { public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class WebsitePostgresFactAttribute : FactAttribute
    {
        public WebsitePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CAREERHARBOR_WEBSITE_TEST_POSTGRES")))
                Skip = "Requires an explicitly configured disposable localhost database ending in _test.";
        }
    }
}
