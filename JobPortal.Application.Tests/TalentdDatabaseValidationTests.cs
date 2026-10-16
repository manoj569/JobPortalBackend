using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

[CollectionDefinition("Talentd local PostgreSQL")]
public sealed class TalentdLocalPostgresCollection { }

[Collection("Talentd local PostgreSQL")]
public sealed partial class TalentdDatabaseValidationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static string LocalConnection(string variable)
    {
        var parsed = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(variable));
        if (parsed.Host is not ("127.0.0.1" or "localhost") || parsed.Database is null || !parsed.Database.EndsWith("_test", StringComparison.Ordinal))
            throw new InvalidOperationException("Talentd database validation requires an explicit loopback database ending in _test.");
        return parsed.ConnectionString;
    }
    private static JobPortalDbContext Database(string variable = "GENERIC_INDIA_TEST_POSTGRES") =>
        new(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(LocalConnection(variable),
            pg => pg.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
    private static FresherBulkImportRequest Fixture()
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Talentd", "approved-worker-request.json")))!;
        var suffix = Guid.NewGuid().ToString("N");
        node["importId"] = "talentd-local-" + suffix;
        node["jobs"]![0]!["externalJobId"] = "talentd:local:" + suffix;
        return JsonSerializer.Deserialize<FresherBulkImportRequest>(node.ToJsonString(), JsonOptions)!;
    }
    private static FresherImportCandidate Change(FresherImportCandidate job, string property, JsonNode? value)
    {
        var node = JsonSerializer.SerializeToNode(job, JsonOptions)!; node[property] = value;
        return JsonSerializer.Deserialize<FresherImportCandidate>(node.ToJsonString(), JsonOptions)!;
    }
    private static FresherBulkImportRequest Request(string id, params FresherImportCandidate[] jobs) => new()
        { ImportId = id, SourceType = "talentd", SourceName = "Talentd", Jobs = jobs };
    private static async Task SeedOwner(JobPortalDbContext db)
    {
        if (await db.Users.AnyAsync(x => x.RoleId == SystemRoleIds.Administrator && x.Status == UserStatus.Active)) return;
        var email = "talentd-local-" + Guid.NewGuid().ToString("N") + "@example.test";
        db.Users.Add(new User { Email = email, NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "test-only",
            FirstName = "Local", LastName = "Fixture", RoleId = SystemRoleIds.Administrator, Status = UserStatus.Active });
        await db.SaveChangesAsync();
    }
    private static (FresherBulkImportService Importer, FresherAcquisitionStore Store) Services(JobPortalDbContext db, TimingLogger logger, Func<IJobAutoPublishService, IJobAutoPublishService>? decorate = null)
    {
        var store = new FresherAcquisitionStore(db); var jobs = new JobRepository(db);
        var companies = new CompanyManagementRepository(db); var categories = new CategoryManagementRepository(db);
        var unit = new UnitOfWork(db); var fingerprint = new JobFingerprintService(); var canonical = new UrlCanonicalizer();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = LocalConnection("GENERIC_INDIA_TEST_POSTGRES") }).Build();
        var locks = new PostgresExternalJobCreationLock(configuration);
        var ingestion = new JobIngestionService(jobs, companies, categories, new JobDeduplicationService(jobs, fingerprint, canonical),
            fingerprint, unit, TimeProvider.System, locks, canonical, acquisitionSources: store);
        var service = new JobService(jobs, unit, new Audit(), new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
            new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), TimeProvider.System, acquisitionSources: store);
        var publisher = new JobAutoPublishService(jobs, new JobQualityGate(), service, Options.Create(new JobAggregationOptions()),
            TimeProvider.System, new JobSourceRepository(db), publicationLock: locks, acquisitionSources: store);
        return (new FresherBulkImportService(ingestion, logger, store, decorate?.Invoke(publisher) ?? publisher, categories: categories), store);
    }

    [TalentdPostgresFact]
    public async Task ApprovedWorkerContractPersistsPublishesUpsertsAndReconcilesOnPostgres()
    {
        await using var db = Database(); await db.Database.MigrateAsync(); await SeedOwner(db);
        var (importer, store) = Services(db, new TimingLogger()); var request = Fixture(); var candidate = request.Jobs[0];
        Assert.Null(await store.GetImportStatusAsync("talentd", request.ImportId, default));
        var result = await importer.ImportAsync(request);
        Assert.Equal(1, result.Created); Assert.Equal(1, result.Published); Assert.Equal(0, result.Failed);
        db.ChangeTracker.Clear();
        var job = await db.Jobs.Include(x => x.Company).Include(x => x.JobSkills).SingleAsync(x => x.Id == result.Items[0].JobId);
        Assert.Equal(JobStatus.Published, job.Status); Assert.False(job.Company.IsVerified);
        Assert.Equal(candidate.CompanyName, job.Company.Name);
        Assert.Equal("https://apply.workable.com/example/j/ABC123/", job.ApplicationUrl);
        Assert.Equal("https://www.talentd.in/jobs/example", job.DiscoveredUrl);
        Assert.Equal(candidate.CanonicalJobUrl, job.CanonicalJobUrl);
        Assert.Equal(5, job.MinimumExperienceYears); Assert.Equal(8, job.MaximumExperienceYears);
        Assert.Equal(EmploymentType.FullTime, job.EmploymentType); Assert.Equal(WorkplaceType.Hybrid, job.WorkplaceType);
        Assert.Equal(20000m, job.MinimumSalary); Assert.Equal(30000m, job.MaximumSalary);
        Assert.Equal("INR", job.CurrencyCode); Assert.Equal(SalaryPeriod.Monthly, job.SalaryPeriod);
        Assert.Equal("Engineering degree", job.EducationRequirement); Assert.Equal("Build reliable services", job.Responsibilities);
        Assert.Equal("Engineering degree required", job.Requirements); Assert.Equal("Health coverage", job.Benefits);
        Assert.Equal("https://assets.example/logo.png", job.Company.LogoUrl);
        Assert.Equal(2, job.JobSkills.Count); Assert.Equal(candidate.PostedAt, job.SourcePostedAtUtc);
        Assert.Equal(candidate.ExpiresAt, job.ExpiresAtUtc); Assert.Equal(candidate.SourceLastSeenAt, job.LastVerifiedAtUtc);
        using (var metadata = JsonDocument.Parse(job.SourceMetadataJson!))
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(candidate.SourceMetadata!.Value.GetRawText()),
                JsonNode.Parse(metadata.RootElement.GetProperty("discovery").GetProperty("extracted").GetRawText())));
        var receipt = await db.AcquisitionImportItems.AsNoTracking().SingleAsync(x => x.AcquisitionImportRunId == result.RunId);
        Assert.Equal("Published", receipt.PublicationStatus); Assert.Null(receipt.PublicationError);
        Assert.Equal(candidate.ApplyUrl, receipt.ApplyUrl); Assert.Equal(candidate.DiscoveredUrl, receipt.DiscoveredUrl);
        var completed = await store.GetImportStatusAsync("talentd", request.ImportId, default);
        Assert.Equal("COMPLETED", completed!.Status); Assert.Equal(1, completed.PublishedCount); Assert.Single(completed.Receipt!.Items);
        var replay = await importer.ImportAsync(request);
        Assert.Equal(result.RunId, replay.RunId); Assert.Equal(result.Items, replay.Items);
        var changed = Change(candidate, "description", JsonValue.Create("Updated source description."));
        await Assert.ThrowsAsync<ImportIdPayloadConflictException>(() => importer.ImportAsync(Request(request.ImportId, changed)));
        var updated = await importer.ImportAsync(Request(request.ImportId + "-update", changed));
        Assert.Equal(0, updated.Created); Assert.Equal(1, updated.Updated); Assert.Equal(1, updated.Published);
        Assert.Equal(job.Id, updated.Items[0].JobId);
        var second = Change(candidate, "externalJobId", JsonValue.Create(candidate.ExternalJobId + ":distinct"));
        second = Change(second, "applyUrl", JsonValue.Create("https://unfamiliar-employer.example/vacancies/ABC123"));
        second = Change(second, "canonicalJobUrl", JsonValue.Create(second.ApplyUrl));
        var distinct = await importer.ImportAsync(Request(request.ImportId + "-distinct", second));
        Assert.Equal(1, distinct.Created); Assert.Equal(1, distinct.Published); Assert.NotEqual(job.Id, distinct.Items[0].JobId);
        Assert.Equal(2, await db.Jobs.CountAsync(x => x.AcquisitionExternalId == FresherCandidateIdentity.Normalize(candidate.ExternalJobId) ||
            x.AcquisitionExternalId == FresherCandidateIdentity.Normalize(second.ExternalJobId)));
        var running = await store.StartPayloadRunAsync(job.AcquisitionSourceId!.Value, request.ImportId + "-running", "local-test",
            new string('a', 64), new string('b', 64), 1, default);
        Assert.Equal("IN_PROGRESS", (await store.GetImportStatusAsync("talentd", running.Run.BatchId, default))!.Status);
        var tracked = db.ChangeTracker.Entries().Count();
        await store.GetImportStatusAsync("talentd", request.ImportId, default);
        Assert.Equal(tracked, db.ChangeTracker.Entries().Count());
    }

    [TalentdPostgresFact]
    public async Task TenDeterministicJobsHaveDurablePublicationAndPhaseTimings()
    {
        await using var db = Database(); await db.Database.MigrateAsync(); await SeedOwner(db);
        var logger = new TimingLogger(); var (importer, store) = Services(db, logger); var fixture = Fixture();
        var jobs = Enumerable.Range(0, 10).Select(index => Change(fixture.Jobs[0], "externalJobId",
            JsonValue.Create(fixture.Jobs[0].ExternalJobId + ":" + index))).ToArray();
        var timer = Stopwatch.StartNew(); var result = await importer.ImportAsync(Request(fixture.ImportId, jobs)); timer.Stop();
        Assert.Equal(10, result.Created); Assert.Equal(10, result.Published); Assert.Equal(0, result.Failed);
        Assert.Equal(10, (await store.GetImportStatusAsync("talentd", fixture.ImportId, default))!.PublishedCount);
        output.WriteLine("LOCAL NON-PRODUCTION: TotalMs={0:F3}; AverageItemMs={1:F3}", timer.Elapsed.TotalMilliseconds, timer.Elapsed.TotalMilliseconds / 10);
        foreach (var phase in logger.Values.GroupBy(x => x.Phase)) output.WriteLine("{0}: count={1}; sumMs={2}", phase.Key, phase.Count(), phase.Sum(x => x.Duration));
    }

    [TalentdMigrationPostgresFact]
    public async Task SourceMigrationAppliesRollsBackReappliesAndRepeatedSeedCreatesOneSource()
    {
        await using var db = Database("TALENTD_MIGRATION_TEST_POSTGRES"); var migrator = db.GetService<IMigrator>();
        await db.Database.MigrateAsync();
        var source = await db.AcquisitionSources.AsNoTracking().SingleAsync(x => x.ProviderKey == "talentd"); var id = source.Id;
        Assert.True(source.IsEnabled); Assert.True(source.AutoPublishEnabled); Assert.False(source.IsDeleted);
        await migrator.MigrateAsync("20261016000100_AddWebsiteCrawlerAcquisitionSource");
        source = await db.AcquisitionSources.AsNoTracking().SingleAsync(x => x.ProviderKey == "talentd");
        Assert.Equal(id, source.Id); Assert.False(source.IsEnabled); Assert.False(source.AutoPublishEnabled);
        await db.Database.MigrateAsync();
        foreach (var operation in new JobPortal.Persistence.Postgres.Migrations.AddTalentdAcquisitionSource().UpOperations.Cast<SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(operation.Sql);
        source = await db.AcquisitionSources.AsNoTracking().SingleAsync(x => x.ProviderKey == "talentd");
        Assert.Equal(id, source.Id); Assert.True(source.IsEnabled); Assert.True(source.AutoPublishEnabled); Assert.False(source.IsDeleted);
        Assert.Equal(1, await db.AcquisitionSources.CountAsync(x => x.ProviderKey == "talentd"));
    }

    private sealed class Audit : IAuditWriter
    { public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class TimingLogger : ILogger<FresherBulkImportService>
    {
        public Action<string>? OnPhase { get; init; }
        public List<(string Phase, long Duration)> Values { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != 8581 || state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
            var values = fields.ToDictionary(x => x.Key, x => x.Value);
            Values.Add(((string)values["Phase"]!, (long)values["DurationMs"]!));
            OnPhase?.Invoke((string)values["Phase"]!);
        }
    }
    private sealed class TalentdPostgresFactAttribute : FactAttribute
    { public TalentdPostgresFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES"))) Skip = "Requires explicit disposable loopback PostgreSQL _test database."; } }
    private sealed class TalentdMigrationPostgresFactAttribute : FactAttribute
    { public TalentdMigrationPostgresFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TALENTD_MIGRATION_TEST_POSTGRES"))) Skip = "Requires separate disposable loopback migration _test database."; } }
}
