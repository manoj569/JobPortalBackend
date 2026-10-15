using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class FresherAcquisitionPostgresTests
{
    private static readonly System.Text.Json.JsonSerializerOptions PayloadJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
    [FresherPostgresFact]
    public async Task JobAndReceiptCommitTogetherAndRollbackLeavesNeither()
    {
        var options = CreateOptions();
        await using var db = new JobPortalDbContext(options);
        await db.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(db);
        var store = new FresherAcquisitionStore(db);

        var failedRun = (await store.StartRunAsync(source.Id, "rollback-" + Guid.NewGuid().ToString("N"), "test",
            "a".PadLeft(64, 'a'), 1, default)).Run;
        var invalidCompanyJob = CreateJob(Guid.NewGuid(), Guid.NewGuid(), source.Id, "bad-external");
        var failedJobId = invalidCompanyJob.Id;
        await using (var chunk = await store.BeginChunkAsync([], default))
        {
            db.Jobs.Add(invalidCompanyJob);
            var item = Receipt(0, "b".PadLeft(64, 'b'), failedJobId, "bad-external");
            // Invalid company FK makes SaveChanges fail after both entities are staged.
            await Assert.ThrowsAsync<DbUpdateException>(() => chunk.CommitAsync(failedRun.Id, [item], default));
        }
        Assert.False(await db.Jobs.AnyAsync(x => x.Id == failedJobId));
        Assert.False(await db.AcquisitionImportItems.AnyAsync(x => x.AcquisitionImportRunId == failedRun.Id));

        var run = (await store.StartRunAsync(source.Id, "commit-" + Guid.NewGuid().ToString("N"), "test",
            "c".PadLeft(64, 'c'), 1, default)).Run;
        var job = CreateJob(Guid.NewGuid(), company.Id, source.Id, "external-1");
        db.Jobs.Add(job);
        await using (var chunk = await store.BeginChunkAsync([$"acquisition-job:{source.Id:N}:EXTERNAL-1"], default))
            await chunk.CommitAsync(run.Id, [Receipt(0, "d".PadLeft(64, 'd'), job.Id, "external-1")], default);

        var persisted = await db.AcquisitionImportItems.SingleAsync(x => x.AcquisitionImportRunId == run.Id);
        Assert.Equal(job.Id, persisted.JobId);
        Assert.True(await db.Jobs.AnyAsync(x => x.Id == persisted.JobId));
        Assert.Equal("https://jobs.example.test/job/1", persisted.DiscoveredUrl);
        Assert.Equal("https://jobs.example.test/canonical/1", persisted.CanonicalUrl);
        Assert.Equal("https://jobs.example.test/apply/1", persisted.ApplyUrl);
    }

    [FresherPostgresFact]
    public async Task ImportIdIsUniqueAndDifferentPayloadIsRejected()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, _) = await CreateSourceAndCompanyAsync(db);
        var store = new FresherAcquisitionStore(db);
        var importId = "same-" + Guid.NewGuid().ToString("N");
        var fingerprint = "a".PadLeft(64, 'a');
        var first = await store.StartRunAsync(source.Id, importId, "test", fingerprint, 2, default);
        var replay = await store.StartRunAsync(source.Id, importId, "test", fingerprint, 2, default);
        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Run.Id, replay.Run.Id);
        await Assert.ThrowsAsync<ImportIdPayloadConflictException>(() =>
            store.StartRunAsync(source.Id, importId, "test", "b".PadLeft(64, 'b'), 2, default));
        Assert.Equal(1, await db.AcquisitionImportRuns.CountAsync(x => x.AcquisitionSourceId == source.Id && x.BatchId == importId));
    }

    [FresherPostgresFact]
    public async Task ConcurrentSameImportIdRequestsResolveToOneRun()
    {
        var options = CreateOptions();
        await using var setup = new JobPortalDbContext(options);
        await setup.Database.MigrateAsync();
        var (source, _) = await CreateSourceAndCompanyAsync(setup);
        var importId = "race-" + Guid.NewGuid().ToString("N");
        var fingerprint = "e".PadLeft(64, 'e');
        async Task<Guid> StartAsync()
        {
            await using var context = new JobPortalDbContext(options);
            var result = await new FresherAcquisitionStore(context).StartRunAsync(source.Id, importId, "race", fingerprint, 1, default);
            return result.Run.Id;
        }
        var ids = await Task.WhenAll(StartAsync(), StartAsync());
        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(1, await setup.AcquisitionImportRuns.CountAsync(x => x.AcquisitionSourceId == source.Id && x.BatchId == importId));
    }

    [FresherPostgresFact]
    public async Task ConcurrentExternalIdentityInsertIsProtectedByPartialUniqueIndex()
    {
        var options = CreateOptions();
        await using var setup = new JobPortalDbContext(options);
        await setup.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(setup);
        var externalId = "race-external-" + Guid.NewGuid().ToString("N");
        async Task<bool> InsertAsync()
        {
            await using var context = new JobPortalDbContext(options);
            context.Jobs.Add(CreateJob(Guid.NewGuid(), company.Id, source.Id, externalId));
            try { await context.SaveChangesAsync(); return true; }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException pg &&
                pg.SqlState == PostgresErrorCodes.UniqueViolation &&
                pg.ConstraintName == "IX_Jobs_AcquisitionSourceId_AcquisitionExternalId") { return false; }
        }
        var inserted = await Task.WhenAll(InsertAsync(), InsertAsync());
        Assert.Single(inserted, x => x);
        Assert.Equal(1, await setup.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id && x.AcquisitionExternalId == externalId));
    }

    [FresherPostgresFact]
    public async Task ImportResumesByIdentityAfterReorderingAndDoesNotDoubleCount()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(db);
        var importer = CreateImporter(db, chunkSize: 2);
        var categoryId = await db.Categories.Select(x => x.Id).FirstAsync();
        var candidates = Enumerable.Range(1, 5)
            .Select(i => Candidate($"resume-{i}", company.Name, categoryId, [$"Acquisition skill {i}"]))
            .ToArray();
        var first = await importer.ImportAsync(Request("resume-batch", source.ProviderKey, candidates));
        var replay = await importer.ImportAsync(Request("resume-batch", source.ProviderKey, candidates.Reverse().ToArray()));

        Assert.Equal(first.RunId, replay.RunId);
        Assert.Equal(5, replay.Received);
        Assert.Equal(5, replay.Created);
        Assert.Equal(5, await db.AcquisitionImportItems.CountAsync(x => x.AcquisitionImportRunId == first.RunId));
        Assert.Equal(5, await db.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id));
        var persistedJobs = await db.Jobs.AsNoTracking().Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .Where(x => x.AcquisitionSourceId == source.Id).ToArrayAsync();
        Assert.All(persistedJobs, job => Assert.Equal(company.Id, job.CompanyId));
        Assert.All(persistedJobs, job => Assert.Equal(categoryId, job.CategoryId));
        Assert.Equal(5, persistedJobs.Sum(x => x.JobSkills.Count));
        Assert.Contains(persistedJobs.SelectMany(x => x.JobSkills), skill => skill.Skill.Name == "Acquisition skill 1");
        Assert.Equal(Enumerable.Range(0, 5), replay.Items.Select(x => x.Index));
    }

    [FresherPostgresFact]
    public async Task RequestDuplicatesCountOnceAndDifferentCandidateSetConflicts()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(db);
        var importer = CreateImporter(db, chunkSize: 2);
        var candidate = Candidate("duplicate-one", company.Name);
        var response = await importer.ImportAsync(Request("duplicate-batch", source.ProviderKey, [candidate, candidate]));
        Assert.Equal(1, response.Received);
        Assert.Equal(1, response.Created);
        Assert.Equal("Duplicate", response.Items[1].Outcome);
        await Assert.ThrowsAsync<ImportIdPayloadConflictException>(() => importer.ImportAsync(
            Request("duplicate-batch", source.ProviderKey, [Candidate("different-set", company.Name)])));
        Assert.Equal(1, await db.AcquisitionImportRuns.CountAsync(x => x.AcquisitionSourceId == source.Id && x.BatchId == "duplicate-batch"));
    }

    [FresherPostgresFact]
    public async Task PublicationFailureKeepsJobAndReceiptAndRetryReusesJob()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(db);
        source.AutoPublishEnabled = true;
        await db.SaveChangesAsync();
        var publisher = new FailOncePublisher();
        var importer = CreateImporter(db, chunkSize: 2, publisher);
        var request = Request("publish-retry", source.ProviderKey, [Candidate("publish-one", company.Name)]);
        var first = await importer.ImportAsync(request);
        var firstJobId = Assert.Single(first.Items).JobId;
        Assert.Equal(1, await db.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id));
        Assert.Equal(1, await db.AcquisitionImportItems.CountAsync(x => x.AcquisitionImportRunId == first.RunId && x.JobId == firstJobId));

        var retry = await importer.ImportAsync(request);
        Assert.Equal(first.RunId, retry.RunId);
        Assert.Equal(1, retry.Created);
        Assert.Equal(1, retry.Published);
        Assert.Equal(1, await db.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id));
        Assert.Equal(firstJobId, (await db.AcquisitionImportItems.SingleAsync(x => x.AcquisitionImportRunId == first.RunId)).JobId);
        Assert.Equal("Published", (await db.AcquisitionImportItems.SingleAsync(x => x.AcquisitionImportRunId == first.RunId)).PublicationStatus);
    }


    [RetainedPayloadPostgresFact]
    public async Task RetainedThirtyJobPayloadWithProductionRetryConfiguration()
    {
        var payloadPath = Environment.GetEnvironmentVariable("FRESHER_RETAINED_PAYLOAD")!;
        var bytes = await File.ReadAllBytesAsync(payloadPath);
        Assert.Equal(83060, bytes.Length);
        Assert.Equal("05cebf2d5c8f0797a63a8020224efc7e84e1ce7cf86e62fd4157905f2c9ba879",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
        var request = System.Text.Json.JsonSerializer.Deserialize<FresherBulkImportRequest>(bytes,
            PayloadJsonOptions)!;
        Assert.Equal(30, request.Jobs.Count);
        Assert.Equal("ch-v1-03d13df0fb5a5b6fbaa601a4ffd391ca1f340524ca87ba63e8bcc02126a745b6", request.ImportId);
        await using var setup = new JobPortalDbContext(CreateOptions());
        await setup.Database.MigrateAsync();
        var source = await setup.AcquisitionSources.SingleAsync(x => x.ProviderKey == request.SourceType && x.IsEnabled);
        Assert.Equal("SerpApi Google Jobs", source.Name);
        Assert.All(request.Jobs, item => Assert.Equal(84, item.ExternalJobId!.Length));
        var priorRun = await setup.AcquisitionImportRuns.SingleOrDefaultAsync(x => x.AcquisitionSourceId == source.Id && x.BatchId == request.ImportId);
        if (priorRun is null)
        {
            // Reproduce the durable legacy run left before the first failed chunk.
            var identities = request.Jobs.Select(x => FresherCandidateIdentity.Create(source.Id, x));
            priorRun = (await new FresherAcquisitionStore(setup).StartRunAsync(source.Id, request.ImportId,
                "pre-fix-attempt", FresherCandidateIdentity.Fingerprint(identities), 30, default)).Run;
        }
        var retryOptions = new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES"), pg => pg.EnableRetryOnFailure(3)).Options;
        await using var db = new JobPortalDbContext(retryOptions);
        var importer = CreateImporter(db, 50);
        var response = await importer.ImportAsync(request);
        Assert.Equal(priorRun.Id, response.RunId);
        Assert.Equal(30, response.Received);
        var replay = await importer.ImportAsync(request);
        Assert.Equal(response.RunId, replay.RunId);
        Assert.Equal(30, await db.AcquisitionImportItems.CountAsync(x => x.AcquisitionImportRunId == response.RunId));
    }


    [FresherPostgresFact]
    public async Task ThirtyExistingCompanyJobsShareStagedSkillsAndReplay()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(db);
        var skill = "Shared acquisition " + Guid.NewGuid().ToString("N");
        var candidates = Enumerable.Range(0, 30).Select(i => Candidate("batch-" + i, company.Name, skills: [skill])).ToArray();
        var request = Request("thirty", source.ProviderKey, candidates);
        var importer = CreateImporter(db, 50);
        var response = await importer.ImportAsync(request);
        Assert.Equal(30, response.Created);
        Assert.Equal(1, await db.Skills.CountAsync(x => x.Name == skill));
        Assert.Equal(30, await db.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id));
        var replay = await importer.ImportAsync(request);
        Assert.Equal(response.RunId, replay.RunId);
        Assert.Equal(30, replay.Created);
    }

    [FresherPostgresFact]
    public async Task UnresolvedCompanyCategoryAndOversizedItemHaveDurableReceipts()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(db);
        var missingCompany = Candidate("missing-company", "Missing " + Guid.NewGuid().ToString("N"));
        var missingCategory = Candidate("missing-category", company.Name, Guid.NewGuid());
        var oversized = new FresherImportCandidate { ExternalJobId = new string('x', 400),
            Title = new string('x', 400), CompanyName = new string('x', 400), Description = "invalid oversized fields" };
        var valid = Candidate("valid", company.Name);
        var response = await CreateImporter(db, 50).ImportAsync(Request("rejections", source.ProviderKey,
            [missingCompany, missingCategory, oversized, valid]));
        Assert.Equal("COMPANY_NOT_RESOLVED", response.Items[0].ReasonCode);
        Assert.Equal("CATEGORY_RESOLUTION_FAILED", response.Items[1].ReasonCode);
        Assert.Equal("RejectedInvalidData", response.Items[2].Outcome);
        Assert.Equal("Created", response.Items[3].Outcome);
        Assert.Equal(4, await db.AcquisitionImportItems.CountAsync(x => x.AcquisitionImportRunId == response.RunId));
    }

    [FresherPostgresFact]
    public async Task MissingSourceReturnsDeterministicFailureWithoutCreatingRun()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var response = await CreateImporter(db, 50).ImportAsync(Request("missing-source", "missing-" + Guid.NewGuid().ToString("N"), [Candidate("one")]));
        Assert.Null(response.RunId);
        Assert.Equal("ACQUISITION_SOURCE_DISABLED", Assert.Single(response.Items).ReasonCode);
    }


    [FresherPostgresFact]
    public async Task ProductionRetryStrategyAllowsChunkWhenInsideDurableBoundary()
    {
        await using (var setup = new JobPortalDbContext(CreateOptions()))
            await setup.Database.MigrateAsync();
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES"), pg => pg.EnableRetryOnFailure(3)).Options;
        await using var db = new JobPortalDbContext(options);
        var store = new FresherAcquisitionStore(db);
        await using (var chunk = await store.BeginChunkAsync(["reproduce-production-retry"], default))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                db.Companies.SingleOrDefaultAsync(x => x.Name == "first-item-company-resolution"));
            Assert.Contains("NpgsqlRetryingExecutionStrategy", exception.Message);
            Assert.Contains("does not support user-initiated transactions", exception.Message);
        }
        var succeeded = await store.ExecuteImportAsync(async token =>
        {
            await using var chunk = await store.BeginChunkAsync(["reproduce-production-retry"], token);
            await db.AcquisitionSources.AnyAsync(token);
            return true;
        }, default);
        Assert.True(succeeded);
        Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);

        // Exercise the real FresherBulkImport -> IngestWithoutSave -> ResolveCompany
        // path with retries enabled, rather than only a direct repository query.
        var (source, company) = await CreateSourceAndCompanyAsync(db);
        var importer = CreateImporter(db, 50);
        var request = Request("production-strategy", source.ProviderKey, [Candidate("company-resolution", company.Name)]);
        var first = await importer.ImportAsync(request);
        Assert.Equal(1, first.Created);
        var replay = await importer.ImportAsync(request);
        Assert.Equal(first.RunId, replay.RunId);
        Assert.Equal(1, replay.Created);
        Assert.Equal(1, await db.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id));
        Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [FresherPostgresFact]
    public async Task NewRunRejectsChangedContentWithSameExternalIdentity()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(db);
        var importer = CreateImporter(db, 50);
        var candidate = Candidate("same-id", company.Name);
        await importer.ImportAsync(Request("content-conflict", source.ProviderKey, [candidate]));
        await Assert.ThrowsAsync<ImportIdPayloadConflictException>(() => importer.ImportAsync(
            Request("content-conflict", source.ProviderKey, [Candidate("same-id", company.Name)])));
    }


    [FresherPostgresFact]
    public async Task InterruptedAfterCommittedChunkResumesWithSameImportId()
    {
        await using var setup = new JobPortalDbContext(CreateOptions());
        await setup.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(setup);
        source.AutoPublishEnabled = true;
        await setup.SaveChangesAsync();
        var candidates = Enumerable.Range(0, 5).Select(i => Candidate("partial-" + i, company.Name)).ToArray();
        var request = Request("partial", source.ProviderKey, candidates);
        using var cancellation = new CancellationTokenSource();
        await using (var firstDb = new JobPortalDbContext(CreateOptions()))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CreateImporter(firstDb, 2, new CancelAfterCommitPublisher(cancellation)).ImportAsync(request, cancellation.Token));
        }
        var run = await setup.AcquisitionImportRuns.AsNoTracking().SingleAsync(x => x.AcquisitionSourceId == source.Id && x.BatchId == request.ImportId);
        Assert.Equal(2, await setup.AcquisitionImportItems.CountAsync(x => x.AcquisitionImportRunId == run.Id));
        Assert.Equal(2, await setup.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id));
        await using var recovery = new JobPortalDbContext(CreateOptions());
        var response = await CreateImporter(recovery, 2, new SuccessfulPublisher()).ImportAsync(request);
        Assert.Equal(run.Id, response.RunId);
        Assert.Equal(5, response.Created);
        Assert.Equal(5, await recovery.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id));
        Assert.Equal(5, await recovery.AcquisitionImportItems.CountAsync(x => x.AcquisitionImportRunId == run.Id));
    }

    private sealed class SuccessfulPublisher : IJobAutoPublishService
    {
        public Task<JobAutoPublishResult> TryPublishAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobAutoPublishResult { Outcome = JobAutoPublishOutcome.Published });
    }

    private sealed class CancelAfterCommitPublisher(CancellationTokenSource cancellation) : IJobAutoPublishService
    {
        public Task<JobAutoPublishResult> TryPublishAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return Task.FromCanceled<JobAutoPublishResult>(cancellation.Token);
        }
    }

    private static FresherBulkImportService CreateImporter(JobPortalDbContext db, int chunkSize,
        IJobAutoPublishService? publisher = null)
    {
        var connection = Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        var jobs = new JobRepository(db);
        var companies = new CompanyManagementRepository(db);
        var categories = new CategoryManagementRepository(db);
        var fingerprint = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var locks = new PostgresExternalJobCreationLock(configuration);
        var dedupe = new JobDeduplicationService(jobs, fingerprint, canonicalizer);
        var ingestion = new JobIngestionService(jobs, companies, categories, dedupe, fingerprint, new UnitOfWork(db),
            TimeProvider.System, locks, canonicalizer);
        return new FresherBulkImportService(ingestion, NullLogger<FresherBulkImportService>.Instance,
            new FresherAcquisitionStore(db), publisher, categories: categories,
            options: Options.Create(new FresherAcquisitionOptions { ChunkSize = chunkSize }));
    }

    private static FresherBulkImportRequest Request(string importId, string provider, IReadOnlyList<FresherImportCandidate> candidates) => new()
    { ImportId = importId, SourceType = provider, SourceName = "ignored display name", Jobs = candidates };

    private static FresherImportCandidate Candidate(string externalId, string companyName = "Acquisition Test",
        Guid? categoryId = null, IReadOnlyList<string>? skills = null)
    {
        // Keep each fixture vacancy distinct both within and across test runs; the
        // generic cross-source deduper intentionally matches shared URLs/fingerprints.
        var vacancyToken = $"{externalId}-{Guid.NewGuid():N}";
        return new FresherImportCandidate
    {
            ExternalJobId = externalId, CompanyName = companyName, Title = $"Graduate Trainee {vacancyToken}",
            Description = "An entry-level graduate role with India eligibility.", Country = "India", LocationText = "Pune, India",
            CategoryId = categoryId, Skills = skills ?? [],
            ExperienceText = "0-2 years", ApplyUrl = $"https://jobs.example.test/apply/{vacancyToken}",
            CanonicalJobUrl = $"https://jobs.example.test/canonical/{vacancyToken}",
            DiscoveredUrl = $"https://jobs.example.test/discovered/{vacancyToken}"
        };
    }

    private static DbContextOptions<JobPortalDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES"),
                pg => pg.MigrationsAssembly("JobPortal.Persistence.Postgres"))
            .Options;

    private static async Task<(AcquisitionSource Source, Company Company)> CreateSourceAndCompanyAsync(JobPortalDbContext db)
    {
        var roleId = await db.Roles.Select(x => x.Id).FirstAsync();
        var owner = new User { Email = $"{Guid.NewGuid():N}@example.test", PasswordHash = "test-only", FirstName = "Test", LastName = "Owner", RoleId = roleId };
        owner.NormalizedEmail = owner.Email.ToUpperInvariant();
        var companyName = "Acquisition test " + Guid.NewGuid().ToString("N");
        var company = new Company { Name = companyName, NormalizedName = companyName.ToUpperInvariant(), Slug = companyName.Replace(' ', '-').ToLowerInvariant(), OwnerUserId = owner.Id };
        var source = new AcquisitionSource { ProviderKey = "test-" + Guid.NewGuid().ToString("N"), Name = "Test", IsEnabled = true, AutoPublishEnabled = false };
        db.AddRange(owner, company, source);
        await db.SaveChangesAsync();
        return (source, company);
    }

    private static Job CreateJob(Guid id, Guid companyId, Guid sourceId, string externalId) => new()
    {
        Id = id, ReferenceNumber = "ACQ-" + Guid.NewGuid().ToString("N"), Title = "Graduate Engineer",
        Slug = "graduate-engineer-" + Guid.NewGuid().ToString("N"), Description = "Test job description",
        ApplicationUrl = "https://jobs.example.test/apply/1", DiscoveredUrl = "https://jobs.example.test/job/1",
        CanonicalJobUrl = "https://jobs.example.test/canonical/1", CompanyId = companyId,
        AcquisitionSourceId = sourceId, AcquisitionExternalId = externalId, Location = "Pune, India",
        ExperienceLevel = ExperienceLevel.Entry, MinimumExperienceYears = 0
    };

    private static FresherReceiptInput Receipt(int index, string identity, Guid jobId, string externalId) => new(index,
        new FresherImportCandidate { ExternalJobId = externalId, CompanyName = "Acquisition Test", Title = "Graduate Engineer",
            Description = "Test job description", DiscoveredUrl = "https://jobs.example.test/job/1",
            CanonicalJobUrl = "https://jobs.example.test/canonical/1", ApplyUrl = "https://jobs.example.test/apply/1" },
        identity, "IndiaEligible:Eligible", new(index, "Created", "CREATED", jobId), DateTime.UtcNow);

    private sealed class FailOncePublisher : IJobAutoPublishService
    {
        private int _calls;
        public Task<JobAutoPublishResult> TryPublishAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1) throw new InvalidOperationException("simulated publish failure");
            return Task.FromResult(new JobAutoPublishResult { Outcome = JobAutoPublishOutcome.Published });
        }
    }

    private sealed class RetainedPayloadPostgresFactAttribute : FactAttribute
    {
        public RetainedPayloadPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FRESHER_RETAINED_PAYLOAD")))
                Skip = "Requires disposable localhost PostgreSQL and explicit retained payload path.";
        }
    }

    private sealed class FresherPostgresFactAttribute : FactAttribute
    {
        public FresherPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES")))
                Skip = "Requires explicit disposable localhost careerharbor_generic_india_test PostgreSQL; never reads application settings.";
        }
    }
}
