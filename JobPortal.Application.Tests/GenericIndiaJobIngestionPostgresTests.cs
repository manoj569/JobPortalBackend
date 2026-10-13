using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class GenericIndiaJobIngestionPostgresTests
{
    [DisposablePostgresFact]
    public async Task ActualMigrationsNullableCategoryDiscoverAndConcurrentPublicationUsePostgresSafely()
    {
        var connection = Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES")!;
        var settings = new NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("127.0.0.1", settings.Host);
        Assert.Equal("careerharbor_generic_india_test", settings.Database);
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(connection,
            pg => pg.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options;
        await using var setup = new JobPortalDbContext(options);
        await setup.Database.MigrateAsync();
        Assert.Empty(await setup.Database.GetPendingMigrationsAsync());
        var name = "Generic India " + Guid.NewGuid().ToString("N");
        var owner = new User { Email = $"{Guid.NewGuid():N}@example.test", PasswordHash = "test-only", FirstName = "Test", LastName = "Admin", RoleId = await setup.Roles.Select(r => r.Id).FirstAsync() };
        owner.NormalizedEmail = owner.Email.ToUpperInvariant();
        setup.Users.Add(owner);
        var company = new Company { Name = name, NormalizedName = name.ToUpperInvariant(), Slug = name.Replace(' ', '-').ToLowerInvariant(), OwnerUserId = owner.Id };
        var sources = Enumerable.Range(0, 8).Select(i => new JobSource { Company = company, CompanyId = company.Id,
            AtsType = AtsType.Greenhouse, AtsIdentifier = company.Slug + i, CareerPageUrl = "https://example.test/careers", AutoPublishEnabled = true }).ToArray();
        setup.AddRange(sources); await setup.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        var locks = new PostgresExternalJobCreationLock(configuration);
        var url = $"https://example.test/jobs/{Guid.NewGuid():N}";
        async Task<JobSourceRunResult> RunSource(JobSource source)
        {
            await using var db = new JobPortalDbContext(options);
            var jobs = new JobRepository(db); var companies = new CompanyManagementRepository(db);
            var categories = new CategoryManagementRepository(db); var sourceRepository = new JobSourceRepository(db);
            var unit = new UnitOfWork(db); var fingerprint = new JobFingerprintService(); var canonical = new UrlCanonicalizer();
            var deduplication = new JobDeduplicationService(jobs, fingerprint, canonical);
            var service = new JobService(jobs, unit, new AuditWriterTestDouble(), new CreateJobRequestValidator(),
                new UpdateJobRequestValidator(), new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(),
                TimeProvider.System, companies, categories, sourceRepository, locks, deduplication);
            var publisher = new JobAutoPublishService(jobs, new JobQualityGate(), service,
                Options.Create(new JobAggregationOptions { AutoPublishEnabled = false }), TimeProvider.System, sourceRepository, locks);
            var raw = GenericIndiaJobIngestionTests.Vacancy(1) with { Location = null, CountryCodes = ["IN"], ApplicationUrl = url, CompanyName = company.Name };
            var provider = new GenericIndiaJobIngestionTests.SnapshotProvider(AtsType.Greenhouse, [raw]);
            var resolver = new JobSourceCategoryResolver(new Monitor(), categories, new ExternalJobCategoryClassifier());
            var ingestion = new JobIngestionService(jobs, companies, categories, deduplication, fingerprint, unit, TimeProvider.System, locks, canonical);
            return await new JobSourceRunner(sourceRepository, [provider], ingestion, unit, TimeProvider.System, resolver,
                new ExternalJobNormalizer(), autoPublishService: publisher, jobRepository: jobs).RunAsync(source.Id);
        }
        var results = await Task.WhenAll(sources.Select(RunSource));
        Assert.Equal(1, results.Sum(r => r.Created));
        Assert.Equal(7, results.Sum(r => r.Existing));
        Assert.Equal(1, results.Sum(r => r.Published));
        Assert.Equal(0, results.Sum(r => r.Failed));
        setup.ChangeTracker.Clear();
        var saved = Assert.Single(await setup.Jobs.Where(j => j.CompanyId == company.Id).ToArrayAsync());
        Assert.Null(saved.CategoryId);
        Assert.Equal("Location not specified", saved.Location);
        Assert.NotNull(saved.SourceMetadataJson);
        var discover = await new PublicJobRepository(setup, TimeProvider.System).SearchAsync(new(CompanyId: company.Id));
        Assert.Equal(saved.Id, Assert.Single(discover.Items).Id);
        var repeated = await RunSource(sources.Single(s => s.Id == saved.JobSourceId));
        Assert.Equal(0, repeated.Created);
        Assert.Equal(1, repeated.Unchanged);
        Assert.Equal(0, repeated.Updated);
        Assert.Equal(0, repeated.Published);
        // Durable diagnostics are persisted as actual PostgreSQL jsonb and exposed by the status contract.
        var store = new JobSourceRunStore(setup);
        var queued = await store.EnqueueAsync(sources[0].Id, null, DateTime.UtcNow, default);
        var now = DateTime.UtcNow;
        var claimed = await store.TryClaimAsync(queued.Id, Guid.NewGuid(), now, now.AddMinutes(2), default);
        Assert.NotNull(claimed);
        var progress = new JobSourceRunProgressSnapshot("Finalizing", 1, results[0]);
        Assert.True(await store.FinishAsync(claimed, JobSourceRunStatus.Succeeded, now, now, progress, default));
        var status = JobSourceRunResponse.From((await store.GetAsync(sources[0].Id, queued.Id, default))!);
        Assert.Equal(0, status.Diagnostics!.MissingLocationAccepted);
        Assert.Equal(1, status.Diagnostics.IndiaEligible);
        Assert.Equal(1, status.Diagnostics.Parsed);
    }

    private sealed class Monitor : IOptionsMonitor<JobAggregationOptions>
    {
        public JobAggregationOptions CurrentValue { get; } = new();
        public JobAggregationOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<JobAggregationOptions, string?> listener) => null;
    }
    private sealed class DisposablePostgresFactAttribute : FactAttribute
    {
        public DisposablePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES")))
                Skip = "Requires explicit disposable localhost careerharbor_generic_india_test PostgreSQL; never reads application settings.";
        }
    }
}
