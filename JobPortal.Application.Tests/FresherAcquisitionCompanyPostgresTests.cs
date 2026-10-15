using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Application.Services;
using JobPortal.Domain.Common;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class FresherAcquisitionPostgresTests
{
    private static FresherImportCandidate CompanyCandidate(string id, string name, string? website = null, Guid? companyId = null)
    {
        var job = Candidate(id, name);
        return new() { ExternalJobId = id, CompanyName = name, CompanyId = companyId, CompanyWebsiteUrl = website,
            CompanyWebsiteVerified = website is not null,
            Title = job.Title, Description = job.Description, Country = job.Country, LocationText = job.LocationText,
            ExperienceText = job.ExperienceText, ApplyUrl = job.ApplyUrl, CanonicalJobUrl = job.CanonicalJobUrl,
            DiscoveredUrl = job.DiscoveredUrl };
    }

    [FresherPostgresFact]
    public async Task FresherResolvesExistingCompanyByDomainThenNormalizedName()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, company) = await CreateSourceAndCompanyAsync(db);
        company.WebsiteUrl = $"https://employer-{Guid.NewGuid():N}.example.test";
        await db.SaveChangesAsync();
        var request = Request("company-existing", source.ProviderKey,
            [CompanyCandidate("domain", "Employer alias", company.WebsiteUrl),
             CompanyCandidate("name", "  " + company.Name.ToUpperInvariant().Replace(" ", "   ") + "  ")]);
        var response = await CreateImporter(db, 50).ImportAsync(request);
        Assert.Equal(2, response.Created);
        var jobs = await db.Jobs.Where(x => x.AcquisitionSourceId == source.Id).ToArrayAsync();
        Assert.All(jobs, x => Assert.Equal(company.Id, x.CompanyId));
    }

    [FresherPostgresFact]
    public async Task FresherCreatesOneMinimalCompanyForTwoJobsAndReplay()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, _) = await CreateSourceAndCompanyAsync(db);
        var name = "New employer " + Guid.NewGuid().ToString("N");
        var request = Request("company-new", source.ProviderKey,
            [CompanyCandidate("one", name), CompanyCandidate("two", "  " + name.ToUpperInvariant() + "  ")]);
        var importer = CreateImporter(db, 50);
        var first = await importer.ImportAsync(request);
        Assert.Equal(2, first.Created);
        var company = await db.Companies.SingleAsync(x => x.NormalizedName == name.ToLowerInvariant());
        Assert.False(company.IsVerified);
        Assert.Null(company.WebsiteUrl);
        Assert.Null(company.NormalizedWebsiteDomain);
        Assert.Null(company.LogoUrl);
        Assert.All(await db.Jobs.Where(x => x.AcquisitionSourceId == source.Id).ToArrayAsync(), x => Assert.Equal(company.Id, x.CompanyId));
        var replay = await importer.ImportAsync(request);
        Assert.Equal(first.RunId, replay.RunId);
        Assert.Equal(2, await db.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id));
    }

    [FresherPostgresFact]
    public async Task ConcurrentFresherImportsCreateOneCompanyByNameOrDomain()
    {
        await using var setup = new JobPortalDbContext(CreateOptions());
        await setup.Database.MigrateAsync();
        var (source, _) = await CreateSourceAndCompanyAsync(setup);
        var name = "Concurrent employer " + Guid.NewGuid().ToString("N");
        async Task<FresherBulkImportResponse> Import(string id, string companyName, string? website)
        {
            var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(
                Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES"), p => p.EnableRetryOnFailure(3)).Options;
            await using var db = new JobPortalDbContext(options);
            return await CreateImporter(db, 50).ImportAsync(Request(id, source.ProviderKey,
                [CompanyCandidate(id, companyName, website)]));
        }
        var names = await Task.WhenAll(Import("name-one", name, null), Import("name-two", name.ToUpperInvariant(), null));
        Assert.All(names, x => Assert.Equal(1, x.Created));
        Assert.Equal(1, await setup.Companies.CountAsync(x => x.NormalizedName == name.ToLowerInvariant()));
        var domain = $"employer-{Guid.NewGuid():N}.example.test";
        var domains = await Task.WhenAll(Import("domain-one", "Brand " + Guid.NewGuid().ToString("N"), "https://" + domain),
            Import("domain-two", "Alias " + Guid.NewGuid().ToString("N"), "https://" + domain));
        Assert.All(domains, x => Assert.Equal(1, x.Created));
        Assert.Equal(1, await setup.Companies.CountAsync(x => x.NormalizedWebsiteDomain == domain));
    }

    [FresherPostgresFact]
    public async Task FresherNeverSavesAggregatorWebsiteOrInfersItFromApplyUrl()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, _) = await CreateSourceAndCompanyAsync(db);
        foreach (var website in new[] { "https://www.linkedin.com/company/example", "https://in.indeed.com", "https://jobs.google.com", null })
        {
            var name = "Website employer " + Guid.NewGuid().ToString("N");
            var response = await CreateImporter(db, 50).ImportAsync(Request(name, source.ProviderKey, [CompanyCandidate(name, name, website)]));
            Assert.Equal(1, response.Created);
            var company = await db.Companies.SingleAsync(x => x.NormalizedName == name.ToLowerInvariant());
            Assert.Null(company.WebsiteUrl);
            Assert.Null(company.NormalizedWebsiteDomain);
            Assert.Null(company.LogoUrl);
        }
    }

    [FresherPostgresFact]
    public async Task FresherInvalidNamesAndInvalidExplicitIdsAreDeterministic()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, _) = await CreateSourceAndCompanyAsync(db);
        var request = Request("company-invalid", source.ProviderKey,
            [CompanyCandidate("blank", "  "), CompanyCandidate("html", "<script>Employer</script>"),
             CompanyCandidate("placeholder", "Confidential"), CompanyCandidate("explicit", "Legitimate name", companyId: Guid.NewGuid())]);
        var result = await CreateImporter(db, 50).ImportAsync(request);
        Assert.Equal("RejectedInvalidData", result.Items[0].Outcome);
        Assert.Equal("COMPANY_NAME_INVALID", result.Items[1].ReasonCode);
        Assert.Equal("COMPANY_NAME_INVALID", result.Items[2].ReasonCode);
        Assert.Equal("COMPANY_ID_NOT_FOUND", result.Items[3].ReasonCode);
        Assert.Equal(0, result.Created);
    }

    [FresherPostgresFact]
    public async Task AtsIngestionStillDoesNotCreateUnknownCompany()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var name = "Unknown ATS employer " + Guid.NewGuid().ToString("N");
        var jobs = new JobRepository(db);
        var fingerprint = new JobFingerprintService();
        var urls = new UrlCanonicalizer();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("GENERIC_INDIA_TEST_POSTGRES") }).Build();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(db), new CategoryManagementRepository(db),
            new JobDeduplicationService(jobs, fingerprint, urls), fingerprint, new UnitOfWork(db), TimeProvider.System,
            new JobPortal.Persistence.Postgres.PostgresExternalJobCreationLock(config), urls);
        var result = await ingestion.IngestAsync(new RawExternalJob { JobSourceId = Guid.NewGuid(), ExternalId = "ats-identity",
            Title = "Graduate engineer", CompanyName = name, Description = "Graduate engineering role in Pune India",
            Location = "Pune, India", CountryCodes = ["IN"], ApplicationUrl = "https://jobs.example.test/ats" });
        Assert.Equal(JobIngestionOutcome.CompanyNotFound, result.Outcome);
        Assert.False(await db.Companies.AnyAsync(x => x.Name == name));
    }

    [FresherPostgresFact]
    public async Task ThirtyFailedCompanyReceiptsRemainAuditableWhileNewDeterministicRunSucceeds()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, _) = await CreateSourceAndCompanyAsync(db);
        var token = Guid.NewGuid().ToString("N");
        var candidates = Enumerable.Range(0, 30).Select(i => CompanyCandidate("retry-" + i, $"New employer {token} {i % 10}")).ToArray();
        var original = Request("failed-original", source.ProviderKey, candidates);
        var store = new FresherAcquisitionStore(db);
        var run = (await store.StartPayloadRunAsync(source.Id, original.ImportId, "test",
            FresherCandidateIdentity.PayloadFingerprint(original), "legacy", 30, default)).Run;
        await using (var chunk = await store.BeginChunkAsync([], default))
            await chunk.CommitAsync(run.Id, candidates.Select((item, i) => new FresherReceiptInput(i, item,
                FresherCandidateIdentity.Create(source.Id, item), "IndiaEligible:Eligible",
                new(i, "Failed", "COMPANY_NOT_RESOLVED"), DateTime.UtcNow)).ToArray(), default);
        await store.CompleteRunAsync(run.Id, new(original.ImportId, 30, 0, 0, 0, 0, 0, 0, 0, 0, 30, []), default);
        var importer = CreateImporter(db, 50);
        Assert.Equal(30, (await importer.ImportAsync(original)).Failed);
        var retry = Request(RetryId(original.ImportId, JsonSerializer.SerializeToUtf8Bytes(original)), source.ProviderKey, candidates);
        var recovered = await importer.ImportAsync(retry);
        Assert.NotEqual(run.Id, recovered.RunId);
        Assert.Equal(30, recovered.Created);
        Assert.Equal(10, await db.Companies.CountAsync(x => x.Name.StartsWith("New employer " + token)));
        Assert.All(await db.AcquisitionImportItems.AsNoTracking().Where(x => x.AcquisitionImportRunId == run.Id).ToArrayAsync(),
            x => { Assert.Equal("COMPANY_NOT_RESOLVED", x.ReasonCode); Assert.Null(x.JobId); });
        Assert.Equal("Completed", (await db.AcquisitionImportRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id)).Status);
        Assert.Equal(recovered.RunId, (await importer.ImportAsync(retry)).RunId);
        Assert.Equal(30, await db.Jobs.CountAsync(x => x.AcquisitionSourceId == source.Id));
    }

    [RetainedPayloadPostgresFact]
    public async Task ExactCachedThirtyJobsProceedBeyondCompanyResolutionInNewRecoveryRun()
    {
        var bytes = await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("FRESHER_RETAINED_PAYLOAD")!);
        Assert.Equal("05cebf2d5c8f0797a63a8020224efc7e84e1ce7cf86e62fd4157905f2c9ba879", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var original = JsonSerializer.Deserialize<FresherBulkImportRequest>(bytes, PayloadJsonOptions)!;
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        await CreateSourceAndCompanyAsync(db); // A valid administrative owner, no pre-created payload employers.
        var retry = new FresherBulkImportRequest { ImportId = RetryId(original.ImportId, bytes), SourceType = original.SourceType, SourceName = original.SourceName, Jobs = original.Jobs };
        var result = await CreateImporter(db, 50).ImportAsync(retry);
        Assert.Equal(30, result.Received);
        Assert.True(result.Accepted > 0);
        Assert.Equal(0, result.RejectedInvalidData);
        Assert.DoesNotContain(result.Items, x => x.ReasonCode is "COMPANY_NOT_RESOLVED" or "CATEGORY_RESOLUTION_FAILED");
        Assert.All(result.Items.Where(x => x.Outcome == "Failed"), x => Assert.Equal("COMPANY_NAME_INVALID", x.ReasonCode));
        Assert.Equal(result.RunId, (await CreateImporter(db, 50).ImportAsync(retry)).RunId);
    }

    private static string RetryId(string originalId, byte[] payload)
    {
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return "ch-cr1-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"company-resolution-v1\n{originalId}\n{hash}"))).ToLowerInvariant();
    }

    [FresherPostgresFact]
    public async Task NewlyCreatedFresherCompanyRollsBackWithFailedChunk()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        await CreateSourceAndCompanyAsync(db);
        var candidate = CompanyCandidate("rollback-company", "Rollback employer " + Guid.NewGuid().ToString("N"));
        var store = new FresherAcquisitionStore(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteImportAsync<int>(async token =>
        {
            await using var chunk = await store.BeginChunkAsync(FresherCompanyIdentity.Locks(candidate).ToArray(), token);
            Assert.NotNull((await store.ResolveCompanyAsync(candidate, token)).CompanyId);
            throw new InvalidOperationException("Simulated later item failure");
        }, default));
        Assert.False(await db.Companies.AnyAsync(x => x.Name == candidate.CompanyName));
    }

    [FresherPostgresFact]
    public async Task UnverifiedWebsiteClaimDoesNotBecomeNewCompanyDomain()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (source, _) = await CreateSourceAndCompanyAsync(db);
        var name = "Unverified site employer " + Guid.NewGuid().ToString("N");
        var job = CompanyCandidate("unverified-site", name);
        var candidate = new FresherImportCandidate { ExternalJobId = job.ExternalJobId, CompanyName = name,
            Title = job.Title, Description = job.Description, Country = job.Country, LocationText = job.LocationText,
            ExperienceText = job.ExperienceText, ApplyUrl = job.ApplyUrl, CompanyWebsiteUrl = "https://unproven.example.test" };
        var response = await CreateImporter(db, 50).ImportAsync(Request("unverified-site", source.ProviderKey, [candidate]));
        Assert.Equal(1, response.Created);
        var company = await db.Companies.SingleAsync(x => x.Name == name);
        Assert.Null(company.WebsiteUrl);
        Assert.Null(company.NormalizedWebsiteDomain);
    }
}
