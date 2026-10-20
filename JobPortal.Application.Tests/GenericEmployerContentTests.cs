using System.Net;
using System.Net.Http.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Tests.EmployerDirectIntegrationTests;

namespace JobPortal.Application.Tests;

public sealed class GenericEmployerContentTests
{
    [Theory]
    [InlineData(15792)]
    [InlineData(26734)]
    [InlineData(27000)]
    [InlineData(11641)]
    public async Task GenericWorkerContentRoundTripsAndUpdatesWithoutDuplication(int length)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Custom;
        f.Source.AtsIdentifier = GenericEmployerContract.Identifier;
        await f.Context.SaveChangesAsync();
        await ExerciseAsync(f.Context, f.Source, length);
    }

    [Fact]
    public void LongFormColumnsAreTextAndSnapshotMatchesModel()
    {
        using var db = new PostgresJobPortalDbContextFactory().CreateDbContext([]);
        var entity = db.Model.FindEntityType(typeof(Job))!;
        foreach (var name in new[] { "Description", "Responsibilities", "Requirements", "Benefits" })
        {
            Assert.Equal("text", entity.FindProperty(name)!.GetColumnType());
            Assert.Null(entity.FindProperty(name)!.GetMaxLength());
        }
        Assert.False(db.Database.HasPendingModelChanges());
        var migration = db.GetService<IMigrationsAssembly>().Migrations.Single(x => x.Key.EndsWith("_ExpandEmployerJobContentToText", StringComparison.Ordinal));
        var instance = db.GetService<IMigrationsAssembly>().CreateMigration(migration.Value, db.Database.ProviderName!);
        var rollback = Assert.IsType<SqlOperation>(instance.DownOperations[0]).Sql;
        Assert.Contains("LOCK TABLE", rollback);
        Assert.Contains("RAISE EXCEPTION", rollback);
        Assert.DoesNotContain("substring", rollback, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, instance.UpOperations.Count);
        foreach (var operation in instance.UpOperations)
        {
            var alter = Assert.IsType<AlterColumnOperation>(operation);
            Assert.Equal("Jobs", alter.Table);
            Assert.Contains(alter.Name, new[] { "Responsibilities", "Requirements", "Benefits" });
            Assert.Equal("text", alter.ColumnType);
            Assert.Null(alter.MaxLength);
            Assert.True(alter.IsNullable);
        }
    }

    [Theory]
    [InlineData("Title", 250)]
    [InlineData("Location", 250)]
    [InlineData("ApplicationUrl", 2048)]
    [InlineData("DiscoveredUrl", 2048)]
    [InlineData("CanonicalJobUrl", 2048)]
    [InlineData("ExternalId", 255)]
    [InlineData("AcquisitionExternalId", 255)]
    [InlineData("EducationRequirement", 200)]
    [InlineData("CurrencyCode", 3)]
    public async Task BoundedFieldsAreRejectedBeforePersistenceWithoutTruncation(string field, int limit)
    {
        using var f = new JobSourceFixture();
        var raw = Vacancy(f.Source, 27000);
        var property = typeof(RawExternalJob).GetProperty(field)!;
        var value = field.EndsWith("Url", StringComparison.Ordinal) ? "https://example.test/" + new string('x', limit) : new string('x', limit + 1);
        property.SetValue(raw, value);
        var result = await Ingestion(f.Context).IngestAsync(raw);
        Assert.Equal(JobIngestionOutcome.Invalid, result.Outcome);
        Assert.Contains(field, result.Message);
        Assert.Equal(value, property.GetValue(raw));
        Assert.Empty(await f.Context.Jobs.ToListAsync());
        Assert.Equal(JobIngestionOutcome.Created, (await Ingestion(f.Context).IngestAsync(Vacancy(f.Source, 27000))).Outcome);
    }

    [Theory]
    [InlineData("DiscoveredUrl")]
    [InlineData("CanonicalJobUrl")]
    public async Task StoredUrlLengthIncludesSurroundingWhitespace(string field)
    {
        using var f = new JobSourceFixture();
        var raw = Vacancy(f.Source, 27000);
        typeof(RawExternalJob).GetProperty(field)!.SetValue(raw, new string(' ', 2050) + "https://example.test/job");
        var result = await Ingestion(f.Context).IngestAsync(raw);
        Assert.Equal(JobIngestionOutcome.Invalid, result.Outcome);
        Assert.Contains(field, result.Message);
        Assert.Empty(await f.Context.Jobs.ToListAsync());
    }

    [Fact]
    public async Task OversizedSkillIsRejectedWithoutCreatingPartialRows()
    {
        using var f = new JobSourceFixture();
        var result = await Ingestion(f.Context).IngestAsync(Vacancy(f.Source, 27000) with { Skills = [new string('s', 151)] });
        Assert.Equal(JobIngestionOutcome.Invalid, result.Outcome);
        Assert.Empty(await f.Context.Set<Skill>().ToListAsync());
        Assert.Empty(await f.Context.Jobs.ToListAsync());
    }

    internal static RawExternalJob Vacancy(JobSource source, int length) => new()
    {
        CompanyId = source.CompanyId, CompanyName = source.Company.Name, JobSourceId = source.Id,
        ExternalId = "content-1", Title = "Software Engineer", Location = "Pune, India",
        ApplicationUrl = "https://example.test/jobs/content-1", Description = new string('d', 30001),
        Responsibilities = new string('r', length), Requirements = new string('q', 19001), Benefits = new string('b', 11001)
    };

    internal static JobIngestionService Ingestion(JobPortalDbContext db)
    {
        var jobs = new JobRepository(db); var fingerprint = new JobFingerprintService(); var canonical = new UrlCanonicalizer();
        return new(jobs, new CompanyManagementRepository(db), new CategoryManagementRepository(db),
            new JobDeduplicationService(jobs, fingerprint, canonical), fingerprint, new UnitOfWork(db), TimeProvider.System,
            new TestAggregationLocks(), canonical);
    }

    internal static async Task ExerciseAsync(JobPortalDbContext db, JobSource source, int length)
    {
        var raw = Vacancy(source, length);
        var run = new JobSourceRun { JobSourceId = source.Id };
        var checkpoint = Stub.Create<IJobSourceRunCheckpoint>((_, _) => (Guid?)run.Id);
        var handler = new WorkerHandler(source, run.Id, raw);
        var factory = Stub.Create<IHttpClientFactory>((_, _) => new HttpClient(handler, false));
        var scanner = new GenericEmployerScanner(factory, Options.Create(new EmployerAcquisitionOptions
            { Endpoint = "https://worker.example.test", Token = new string('t', 32) }), checkpoint);
        var provider = new EmployerDirectSnapshotProvider(scanner: scanner);
        var snapshot = await provider.FetchSnapshotAsync(source);
        Assert.False(snapshot.IsComplete);
        var received = Assert.Single(snapshot.Jobs);
        Assert.Equal(raw.Description, received.Description);
        Assert.Equal(raw.Responsibilities, received.Responsibilities);
        Assert.Equal(raw.Requirements, received.Requirements);
        Assert.Equal(raw.Benefits, received.Benefits);
        var ingestion = Ingestion(db);
        var created = await ingestion.IngestAsync(received);
        Assert.Equal(JobIngestionOutcome.Created, created.Outcome);
        var saved = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == created.JobId);
        Assert.Equal(raw.Description, saved.Description);
        Assert.Equal(raw.Responsibilities, saved.Responsibilities);
        Assert.Equal(raw.Requirements, saved.Requirements);
        Assert.Equal(raw.Benefits, saved.Benefits);
        Assert.Equal(JobStatus.Draft, saved.Status);
        foreach (var property in db.Model.FindEntityType(typeof(Job))!.GetProperties().Where(x => x.ClrType == typeof(string)))
        {
            var value = (string?)property.PropertyInfo!.GetValue(saved);
            if (property.GetMaxLength() is { } maximum) Assert.True(value is null || value.Length <= maximum, property.Name);
        }
        var repeated = await ingestion.IngestAsync(received);
        Assert.Equal(created.JobId, repeated.JobId);
        Assert.Equal(JobIngestionOutcome.Unchanged, repeated.Outcome);
        var updated = await ingestion.IngestAsync(received with { Responsibilities = raw.Responsibilities + " updated" });
        Assert.Equal(JobIngestionOutcome.Updated, updated.Outcome);
        Assert.Equal(created.JobId, updated.JobId);
        Assert.Equal(1, await db.Jobs.CountAsync(x => x.JobSourceId == source.Id));
        Assert.Equal(raw.Responsibilities + " updated", (await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == created.JobId)).Responsibilities);
    }

    private sealed class WorkerHandler(JobSource source, Guid runId, RawExternalJob raw) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                runId, sourceId = source.Id, source.CompanyId, source.CareerPageUrl, observedAtUtc = DateTimeOffset.UtcNow,
                isComplete = false, completionReason = "INCOMPLETE", status = "partial", jobs = new[] { raw },
                providerRoutes = Array.Empty<object>(), strategy = new { }, metrics = new { }, errors = Array.Empty<object>()
            }) });
    }
}

public sealed class GenericEmployerContentPostgresTests(WorkdayCheckpointDatabase database) : IClassFixture<WorkdayCheckpointDatabase>
{
    [LocalPostgresFact]
    public async Task ActualPostgresPersistsAllLongFormContentAndDeduplicatesGenericJobs()
    {
        await using var db = database.Open();
        var owner = new User { Email = Guid.NewGuid().ToString("N") + "@fixture.test", FirstName = "Test", LastName = "Owner",
            RoleId = await db.Roles.Select(x => x.Id).FirstAsync() };
        owner.NormalizedEmail = owner.Email.ToUpperInvariant();
        var company = new Company { Name = "Content fixture", NormalizedName = "CONTENT FIXTURE", Slug = Guid.NewGuid().ToString("N"), OwnerUserId = owner.Id };
        var source = new JobSource { Company = company, CompanyId = company.Id, AtsType = AtsType.Custom,
            AtsIdentifier = GenericEmployerContract.Identifier, CareerPageUrl = "https://example.test/careers" };
        db.AddRange(owner, source); await db.SaveChangesAsync();
        await GenericEmployerContentTests.ExerciseAsync(db, source, 27000);
    }

    [LocalPostgresFact]
    public async Task UpgradePreservesExistingRowsAndRollbackRefusesToTruncate()
    {
        // Own schema, independent of test ordering and the class fixture's jobs.
        var isolated = new WorkdayCheckpointDatabase();
        await isolated.InitializeAsync();
        try
        {
            await using var db = isolated.Open();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261019175240_AddReferralContactValues");
            var owner = new User { Email = Guid.NewGuid().ToString("N") + "@fixture.test", FirstName = "Test", LastName = "Owner",
                RoleId = await db.Roles.Select(x => x.Id).FirstAsync() };
            owner.NormalizedEmail = owner.Email.ToUpperInvariant();
            var company = new Company { Name = "Upgrade fixture", NormalizedName = "UPGRADE FIXTURE", Slug = Guid.NewGuid().ToString("N"), OwnerUserId = owner.Id };
            var job = new Job { Company = company, CompanyId = company.Id, Title = "Software Engineer",
                ReferenceNumber = Guid.NewGuid().ToString("N"), Slug = Guid.NewGuid().ToString("N"),
                ApplicationUrl = "https://example.test/job", Description = new string('d', 30001),
                Responsibilities = new string('r', 8000), Requirements = new string('q', 8000), Benefits = new string('b', 4000) };
            db.AddRange(owner, job); await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            var saved = await db.Jobs.SingleAsync(x => x.Id == job.Id);
            Assert.Equal(job.Description, saved.Description);
            Assert.Equal(job.Responsibilities, saved.Responsibilities);
            Assert.Equal(job.Requirements, saved.Requirements);
            Assert.Equal(job.Benefits, saved.Benefits);
            saved.Responsibilities = new string('r', 27000);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => migrator.MigrateAsync("20261019175240_AddReferralContactValues"));
            db.ChangeTracker.Clear();
            Assert.Equal(27000, (await db.Jobs.SingleAsync(x => x.Id == job.Id)).Responsibilities!.Length);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        finally { await isolated.DisposeAsync(); }
    }

    private sealed class LocalPostgresFactAttribute : FactAttribute
    {
        public LocalPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires disposable localhost careerharbor_workday_test PostgreSQL.";
        }
    }
}
