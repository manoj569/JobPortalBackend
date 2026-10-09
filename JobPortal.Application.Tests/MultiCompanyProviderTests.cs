using System.Net;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

// Saved synthetic fixtures; never call live job feeds, production DBs, or AI providers.
public sealed class MultiCompanyProviderTests
{
    [Theory]
    [InlineData(AtsType.Greenhouse, "greenhouse.json", 2)]
    [InlineData(AtsType.Lever, "lever.json", 1)]
    [InlineData(AtsType.Ashby, "ashby.json", 1)]
    public async Task RealProviderPipelineImportsAndReusesJobsWithoutSourceApprovals(AtsType type, string fixture, int count)
    {
        using var f = new JobSourceFixture();
        var configured = Source(type);
        f.Source.AtsType = type;
        f.Source.AtsIdentifier = configured.AtsIdentifier;
        f.Source.CareerPageUrl = configured.CareerPageUrl;
        f.Map(f.Category.Id.ToString());
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PublicAts", fixture));
        using var factory = new Factory(_ => json);
        var repository = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprint = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(repository, new CompanyManagementRepository(f.Context),
            new CategoryManagementRepository(f.Context), new JobDeduplicationService(repository, fingerprint, canonicalizer),
            fingerprint, unit, new Clock(), f.Locks, canonicalizer);
        var policy = new JobSourcePublicationPolicy(Options.Create(new JobAggregationOptions()), new Clock());
        var runner = new JobSourceRunner(f.Repository, [Provider(type, factory)], ingestion, unit, new Clock(), f.Resolver,
            new ExternalJobNormalizer(), jobRepository: repository, publicationPolicy: policy);
        var first = await runner.RunAsync(f.Source.Id);
        Assert.True(first.Succeeded);
        Assert.Equal(count, first.Created);
        var second = await runner.RunAsync(f.Source.Id);
        Assert.True(second.Succeeded);
        Assert.Equal(0, second.Created);
        Assert.Equal(count, second.Unchanged);
        Assert.Equal(count, await f.Context.Jobs.CountAsync());
        Assert.NotNull(f.Source.LastSuccessfulRunAtUtc);
        Assert.Null(f.Company.LogoUrl);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse, "greenhouse.json", 2)]
    [InlineData(AtsType.Lever, "lever.json", 1)]
    [InlineData(AtsType.Ashby, "ashby.json", 1)]
    public async Task OfficialContractsYieldCompleteStableOwnedIdentities(AtsType type, string fixture, int count)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PublicAts", fixture));
        using var factory = new Factory(_ => json);
        var provider = Provider(type, factory);
        var source = Source(type);
        var first = await provider.FetchSnapshotAsync(source);
        var second = await provider.FetchSnapshotAsync(source);
        Assert.True(first.IsComplete);
        Assert.Equal(0, first.Skipped);
        Assert.Equal(count, first.Jobs.Count);
        Assert.Equal(first.Jobs.Select(x => x.ExternalId), second.Jobs.Select(x => x.ExternalId));
        Assert.All(first.Jobs, job => Assert.False(string.IsNullOrWhiteSpace(job.ExternalId)));
        var normalized = new ExternalJobNormalizer().Normalize(first.Jobs.First());
        Assert.DoesNotContain("unsafe()", normalized.Description);
        Assert.Null(normalized.ExperienceLevel); // Never invent experience from title/technology.
        if (type == AtsType.Lever)
        {
            Assert.Contains("SQL", normalized.Description);
            Assert.Contains("List<T>", normalized.Description);
            Assert.EndsWith("/apply", normalized.ApplicationUrl);
            Assert.Equal(WorkplaceType.Hybrid, normalized.WorkplaceType);
            Assert.Contains("IN", normalized.CountryCodes);
        }
        if (type == AtsType.Ashby)
        {
            Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), normalized.SourcePostedAtUtc);
            Assert.Contains("IND", normalized.CountryCodes);
            Assert.EndsWith("/application", normalized.ApplicationUrl);
        }
        if (type == AtsType.Greenhouse) Assert.Null(normalized.SourcePostedAtUtc); // updated_at is not posted_at.
    }

    [Fact]
    public async Task LeverReadsEveryPageIncludingTerminalEmptyPage()
    {
        using var factory = new Factory(uri => Page(uri, 200));
        var result = await new LeverExternalJobProvider(factory).FetchSnapshotAsync(Source(AtsType.Lever));
        Assert.True(result.IsComplete);
        Assert.Equal(200, result.Jobs.Count);
        Assert.Equal(3, factory.Requests.Count);
        Assert.Contains("skip=200&limit=100", factory.Requests.Last().Query);
    }

    [Fact]
    public async Task LeverRepeatedPageFailsClosedBeforeIngestion()
    {
        using var factory = new Factory(_ => LeverPage(0, 100));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LeverExternalJobProvider(factory).FetchSnapshotAsync(Source(AtsType.Lever)));
        Assert.Equal("lever_repeated_page_or_identity", error.Message);
        Assert.Equal(2, factory.Requests.Count);
    }

    [Fact]
    public async Task LeverEuUsesOnlyDocumentedEuApi()
    {
        using var factory = new Factory(_ => "[]");
        var source = Source(AtsType.Lever);
        source.CareerPageUrl = "https://jobs.eu.lever.co/acme";
        Assert.True((await new LeverExternalJobProvider(factory).FetchSnapshotAsync(source)).IsComplete);
        Assert.Equal("api.eu.lever.co", Assert.Single(factory.Requests).Host);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(-1)]
    public async Task GreenhouseTotalMismatchCannotCloseJobs(int total)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PublicAts", "greenhouse.json"));
        json = json.Replace("\"total\": 2", $"\"total\": {total}", StringComparison.Ordinal);
        using var factory = new Factory(_ => json);
        Assert.False((await new GreenhouseExternalJobProvider(factory).FetchSnapshotAsync(Source(AtsType.Greenhouse))).IsComplete);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task UnavailableGreenhouseDetailsCannotBeImportedOrReconciled(HttpStatusCode status)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PublicAts", "greenhouse.json"));
        using var factory = new Factory(_ => json, detailStatus: status);
        var snapshot = await new GreenhouseExternalJobProvider(factory).FetchSnapshotAsync(Source(AtsType.Greenhouse));
        Assert.False(snapshot.IsComplete);
        Assert.Empty(snapshot.Jobs);
        Assert.Equal(2, snapshot.Skipped);
    }

    [Fact]
    public async Task GreenhouseProspectPostsAreNotVacanciesOrCompleteSnapshots()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PublicAts", "greenhouse.json"));
        json = json.Replace("\"id\": 1001", "\"internal_job_id\": null, \"id\": 1001", StringComparison.Ordinal);
        using var factory = new Factory(_ => json);
        var snapshot = await new GreenhouseExternalJobProvider(factory).FetchSnapshotAsync(Source(AtsType.Greenhouse));
        Assert.False(snapshot.IsComplete);
        Assert.Single(snapshot.Jobs);
        Assert.Equal("1002", snapshot.Jobs.Single().ExternalId);
        Assert.Equal(1, snapshot.Skipped);
        Assert.DoesNotContain(factory.Requests, uri => uri.AbsolutePath.EndsWith("/jobs/1001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateGreenhouseIdentityIsExplicitlySkippedAndIncomplete()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PublicAts", "greenhouse.json"));
        json = json.Replace("1002", "1001", StringComparison.Ordinal);
        using var factory = new Factory(_ => json);
        var snapshot = await new GreenhouseExternalJobProvider(factory).FetchSnapshotAsync(Source(AtsType.Greenhouse));
        Assert.False(snapshot.IsComplete);
        Assert.Single(snapshot.Jobs);
        Assert.Equal(1, snapshot.Skipped);
    }

    [Theory]
    [InlineData("https://evil.example/acme/job")]
    [InlineData("https://jobs.ashbyhq.com/other/e2b68cda-f6f9-4f03-a302-68f1e0a3ed74")]
    [InlineData("https://jobs.ashbyhq.com/acme/not-a-posting-uuid")]
    [InlineData("http://jobs.ashbyhq.com/acme/e2b68cda-f6f9-4f03-a302-68f1e0a3ed74")]
    public async Task AshbyRejectsUnverifiableIdentityAndApplicationUrl(string url)
    {
        using var factory = new Factory(_ => JsonSerializer.Serialize(new
        { apiVersion = "1", jobs = new[] { new { title = "Engineer", isListed = true, descriptionPlain = "Build APIs.", jobUrl = url } } }));
        var result = await new AshbyExternalJobProvider(factory).FetchSnapshotAsync(Source(AtsType.Ashby));
        Assert.False(result.IsComplete);
        Assert.Empty(result.Jobs);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task MissingAshbyVisibilityCannotBecomeEmptyCompleteSnapshot()
    {
        using var factory = new Factory(_ => """{"apiVersion":"1","jobs":[{"title":"Unknown visibility"}]}""");
        Assert.False((await new AshbyExternalJobProvider(factory).FetchSnapshotAsync(Source(AtsType.Ashby))).IsComplete);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    public async Task CancellationAndRateLimitsNeverProduceCompleteEmptySnapshot(AtsType type)
    {
        using var factory = new Factory(_ => "{}", HttpStatusCode.TooManyRequests);
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(type, factory).FetchSnapshotAsync(Source(type)));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(type, factory).FetchSnapshotAsync(Source(type), cancel.Token));
        Assert.Single(factory.Requests);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(1000)]
    public async Task LimitedDurablePipelineIsIdempotentAndNeverReconciles(int limit)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Lever;
        f.Source.CareerPageUrl = "https://jobs.lever.co/acme";
        f.Company.IsVerified = true;
        f.Map(f.Category.Id.ToString());
        var approval = Approval(f.Source);
        approval.TestImportLimit = limit;
        approval.LogoUrl = "https://assets.acme.example/licensed-logo.png";
        approval.LogoRightsEvidence = "test-logo-permission";
        var policy = Policy(f.Source, approval);
        using var factory = new Factory(uri => Page(uri, 1500));
        var provider = new LeverExternalJobProvider(factory);
        var repository = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(repository, new CompanyManagementRepository(f.Context),
            new CategoryManagementRepository(f.Context), new JobDeduplicationService(repository, fingerprints, canonicalizer),
            fingerprints, unit, new Clock(), f.Locks, canonicalizer);
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit, new Clock(), f.Resolver,
            new ExternalJobNormalizer(), jobRepository: repository, publicationPolicy: policy);
        var first = await runner.RunAsync(f.Source.Id);
        Assert.Equal(limit, first.Created);
        Assert.False(first.Succeeded);
        Assert.Equal(0, first.Closed);
        Assert.Null(f.Source.LastSuccessfulRunAtUtc);
        var second = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, second.Created);
        Assert.Equal(limit, second.Unchanged);
        Assert.Equal(limit, await f.Context.Jobs.CountAsync());
        Assert.Equal(approval.LogoUrl, await f.Context.Companies.AsNoTracking()
            .Where(x => x.Id == f.Company.Id).Select(x => x.LogoUrl).SingleAsync());
        Assert.All(f.Context.Jobs, job =>
        {
            Assert.Equal(f.Source.Id, job.JobSourceId);
            Assert.Equal(f.Company.Id, job.CompanyId);
        });
    }

    [Fact]
    public async Task GeographicFilteringDoesNotCloseAnObservedExistingVacancy()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Lever;
        f.Company.IsVerified = true;
        f.Map(f.Category.Id.ToString());
        Job Existing(string id) => new()
        {
            Title = "Existing " + id, Slug = id, ReferenceNumber = id, Description = "Existing source vacancy.",
            CompanyId = f.Company.Id, Company = f.Company, CategoryId = f.Category.Id, Category = f.Category,
            JobSourceId = f.Source.Id, ExternalJobId = id, Status = JobStatus.Published, PublishedAtUtc = JobSourceFixture.Now
        };
        var foreign = Existing("foreign");
        var missing = Existing("missing");
        f.Context.Jobs.AddRange(foreign, missing);
        await f.Context.SaveChangesAsync();
        using var factory = new Factory(_ => JsonSerializer.Serialize(new[]
        {
            new { id = "india", text = "New Indian engineer", country = "IN", descriptionPlain = "Develop APIs.",
                hostedUrl = "https://jobs.lever.co/acme/india", categories = new { location = "Pune, India" } },
            new { id = "foreign", text = "Foreign engineer", country = "US", descriptionPlain = "Develop APIs.",
                hostedUrl = "https://jobs.lever.co/acme/foreign", categories = new { location = "New York" } }
        }));
        var repository = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(repository, new CompanyManagementRepository(f.Context),
            new CategoryManagementRepository(f.Context), new JobDeduplicationService(repository, fingerprints, canonicalizer),
            fingerprints, unit, new Clock(), f.Locks, canonicalizer);
        var runner = new JobSourceRunner(f.Repository, [new LeverExternalJobProvider(factory)], ingestion, unit, new Clock(),
            f.Resolver, new ExternalJobNormalizer(), jobRepository: repository, publicationPolicy: Policy(f.Source));
        var result = await runner.RunAsync(f.Source.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Closed);
        Assert.Equal(JobStatus.Published, (await f.Context.Jobs.AsNoTracking().SingleAsync(x => x.Id == foreign.Id)).Status);
        Assert.Equal(JobStatus.Closed, (await f.Context.Jobs.AsNoTracking().SingleAsync(x => x.Id == missing.Id)).Status);
    }

    [Fact]
    public void MissingOrExpiredRightsDoNotBlockImportSelection()
    {
        var source = Source(AtsType.Greenhouse);
        var policy = new JobSourcePublicationPolicy(Options.Create(new JobAggregationOptions()), new Clock());
        var snapshot = new ExternalJobSourceSnapshot([new RawExternalJob { Location = "India" }], 0, true);
        Assert.Single(policy.Select(source, snapshot).Jobs);
        var approved = Approval(source);
        approved.RightsExpireAtUtc = new(JobSourceFixture.Now);
        Assert.Single(Policy(source, approved).Select(source, snapshot).Jobs);
        approved.RightsExpireAtUtc = new(JobSourceFixture.Now.AddDays(1));
        source.AtsIdentifier = "another-board";
        Assert.Single(Policy(source, approved).Select(source, snapshot).Jobs);
    }

    [Fact]
    public void OptionalTestCapWithoutRightsRemainsIncompleteAndMissingLogoIsHarmless()
    {
        var source = Source(AtsType.Greenhouse);
        var settings = new JobSourcePublicationApproval { TestImportLimit = 1 };
        var policy = Policy(source, settings);
        var selected = policy.Select(source, new([
            new RawExternalJob { ExternalId = "1", Location = "India" },
            new RawExternalJob { ExternalId = "2", Location = "India" }], 0, true));
        Assert.Single(selected.Jobs);
        Assert.False(selected.IsComplete);
        policy.ApplyLicensedLogo(source);
        Assert.Null(source.Company.LogoUrl);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void InvalidOptionalImportCapStillFailsClosed(int limit)
    {
        var source = Source(AtsType.Greenhouse);
        var error = Assert.Throws<BadRequestException>(() => Policy(source, limit).Select(source, new([], 0, true)));
        Assert.Equal("invalid_test_import_limit", error.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task SourceUpdateWithoutDeadlinePreservesExistingExpiryAndNeverResurrectsExpiredJob(int days)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Lever;
        f.Company.IsVerified = true;
        f.Map(f.Category.Id.ToString());
        var expiry = JobSourceFixture.Now.AddDays(days);
        var existing = new Job
        {
            Title = "Engineer 0", Slug = "engineer-0", ReferenceNumber = "existing-0", Description = "Existing role.",
            CompanyId = f.Company.Id, Company = f.Company, CategoryId = f.Category.Id, Category = f.Category,
            JobSourceId = f.Source.Id, ExternalJobId = "posting-0", ApplicationUrl = "https://jobs.lever.co/acme/posting-0/apply",
            Status = JobStatus.Published, PublishedAtUtc = JobSourceFixture.Now.AddDays(-5), ExpiresAtUtc = expiry
        };
        f.Context.Jobs.Add(existing);
        await f.Context.SaveChangesAsync();
        using var factory = new Factory(_ => LeverPage(0, 1));
        var repository = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprint = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(repository, new CompanyManagementRepository(f.Context),
            new CategoryManagementRepository(f.Context), new JobDeduplicationService(repository, fingerprint, canonicalizer),
            fingerprint, unit, new Clock(), f.Locks, canonicalizer);
        var runner = new JobSourceRunner(f.Repository, [new LeverExternalJobProvider(factory)], ingestion, unit, new Clock(),
            f.Resolver, new ExternalJobNormalizer(), jobRepository: repository, publicationPolicy: Policy(f.Source));
        Assert.True((await runner.RunAsync(f.Source.Id)).Succeeded);
        Assert.Equal(expiry, (await f.Context.Jobs.AsNoTracking().SingleAsync()).ExpiresAtUtc);
        Assert.Equal(days < 0 ? 0 : 1, (await new PublicJobRepository(f.Context, new Clock()).SearchAsync(new())).TotalCount);
        Assert.Equal(1, (await runner.RunAsync(f.Source.Id)).Unchanged);
        Assert.Single(await f.Context.Jobs.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    [InlineData(AtsType.SuccessFactors)]
    public async Task RunnerWithoutApprovalsCallsProviderAndCompletes(AtsType type)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = type;
        var policy = new JobSourcePublicationPolicy(Options.Create(new JobAggregationOptions()), new Clock());
        var runner = new JobSourceRunner(f.Repository, [new EmptyProvider(type)], new NeverIngest(), new UnitOfWork(f.Context),
            new Clock(), f.Resolver, new ExternalJobNormalizer(), publicationPolicy: policy);
        Assert.True((await runner.RunAsync(f.Source.Id)).Succeeded);
        Assert.Empty(f.Context.Jobs);
        Assert.NotNull(f.Source.LastSuccessfulRunAtUtc);
    }

    [Fact]
    public void LogoCannotBeBorrowedByAnotherCompanyOrUnverifiedCompany()
    {
        var source = Source(AtsType.Greenhouse);
        var approval = Approval(source);
        approval.LogoUrl = "https://assets.example.test/logo.png";
        approval.LogoRightsEvidence = "fixture-logo-license";
        var policy = Policy(source, approval);
        source.Company.IsVerified = false;
        policy.ApplyLicensedLogo(source);
        Assert.Null(source.Company.LogoUrl);
        source.Company.IsVerified = true;
        source.CompanyId = Guid.NewGuid();
        policy.ApplyLicensedLogo(source);
        Assert.Null(source.Company.LogoUrl);
    }

    [Theory]
    [InlineData("Pune, India", "", true, true)]
    [InlineData("Remote", "IN", true, true)]
    [InlineData("Remote", "US", false, true)]
    [InlineData("Bangalore", "US", false, true)]
    [InlineData("Remote", "", false, false)]
    [InlineData("Indiana", "", false, false)]
    [InlineData("Hyderabad", "", false, false)]
    [InlineData("Hyderabad, Pakistan", "", false, false)]
    [InlineData("Remote", "XX", false, false)]
    [InlineData("Worldwide", "", false, false)]
    public void IndiaEligibilityRequiresEvidenceNotRemoteLabel(string location, string country, bool included, bool complete)
    {
        var source = Source(AtsType.Greenhouse);
        var selected = Policy(source).Select(source, new([new RawExternalJob
        { Location = location, CountryCodes = country.Length == 0 ? [] : [country], WorkplaceTypeText = "remote" }], 0, true));
        Assert.Equal(included ? 1 : 0, selected.Jobs.Count);
        Assert.Equal(complete, selected.IsComplete);
    }

    [Fact]
    public void VerifiedExactIndianLocationCanBeUsedWithoutGuessingCities()
    {
        var source = Source(AtsType.Greenhouse);
        var approval = Approval(source);
        approval.VerifiedIndiaLocations = ["Hyderabad, Telangana"];
        var selected = Policy(source, approval).Select(source, new([new RawExternalJob { Location = "Hyderabad, Telangana" }], 0, true));
        Assert.Single(selected.Jobs);
        Assert.True(selected.IsComplete);
    }

    [Fact]
    public void BlankReviewedLocationCannotGrantIndiaEligibility()
    {
        var source = Source(AtsType.Greenhouse);
        var approval = Approval(source);
        approval.VerifiedIndiaLocations = [""];
        var selected = Policy(source, approval).Select(source, new([new RawExternalJob { Location = "  " }], 0, true));
        Assert.Empty(selected.Jobs);
        Assert.False(selected.IsComplete);
    }

    [Fact]
    public async Task ApprovalExpiringDuringImportDoesNotInterruptValidRun()
    {
        using var f = new JobSourceFixture();
        f.Company.IsVerified = true;
        f.Map(f.Category.Id.ToString());
        var clock = new AdvancingClock();
        var approval = Approval(f.Source);
        var policy = new JobSourcePublicationPolicy(Options.Create(new JobAggregationOptions
        { SourceApprovals = new() { [f.Source.Id.ToString("D")] = approval } }), clock);
        f.Provider.Jobs = [new() { Title = "One", Location = "India" }, new() { Title = "Two", Location = "India" }];
        var ingestion = new ExpiringIngestion(clock);
        var result = await new JobSourceRunner(f.Repository, [f.Provider], ingestion, new UnitOfWork(f.Context),
            clock, f.Resolver, new ExternalJobNormalizer(), jobRepository: new JobRepository(f.Context),
            publicationPolicy: policy).RunAsync(f.Source.Id);
        Assert.Equal(2, ingestion.Calls);
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Closed);
        Assert.NotNull(f.Source.LastSuccessfulRunAtUtc);
    }

    [Fact]
    public void LicensedLogosAreCentralizedAndAdminOverrideWins()
    {
        var source = Source(AtsType.Greenhouse);
        var approval = Approval(source);
        approval.LogoUrl = "https://assets.acme.example/logo.png";
        var policy = Policy(source, approval);
        policy.ApplyLicensedLogo(source);
        Assert.Null(source.Company.LogoUrl);
        approval.LogoRightsEvidence = "test-permission-record";
        policy.ApplyLicensedLogo(source);
        Assert.Equal(approval.LogoUrl, source.Company.LogoUrl);
        source.Company.LogoUrl = "/media/admin-approved-logo.png";
        policy.ApplyLicensedLogo(source);
        Assert.Equal("/media/admin-approved-logo.png", source.Company.LogoUrl);
        var job = new Job { Company = source.Company, CompanyId = source.CompanyId, Category = new Category(), PublishedAtUtc = JobSourceFixture.Now };
        var dto = PublicJobProjections.Summary.Compile()(job);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"companyId\":", json);
        Assert.Contains("\"companyName\":\"Acme\"", json);
        Assert.Contains("\"companyLogoUrl\":\"/media/admin-approved-logo.png\"", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://assets.acme.example/logo.png")]
    public async Task ListingAndDetailReuseSameCompanyLogoAndOnlyCountAvailableJobs(string? logo)
    {
        using var f = new JobSourceFixture();
        f.Company.LogoUrl = logo;
        Job Job(string slug, JobStatus status = JobStatus.Published, bool hidden = false, bool expired = false) => new()
        {
            Slug = slug, Title = slug, Company = f.Company, CompanyId = f.Company.Id, Category = f.Category,
            CategoryId = f.Category.Id, Status = status, IsHidden = hidden, PublishedAtUtc = JobSourceFixture.Now,
            ExpiresAtUtc = expired ? JobSourceFixture.Now.AddSeconds(-1) : JobSourceFixture.Now.AddDays(1)
        };
        f.Context.Jobs.AddRange(Job("available-one"), Job("available-two"), Job("draft", JobStatus.Draft),
            Job("closed", JobStatus.Closed), Job("hidden", hidden: true), Job("expired", expired: true));
        await f.Context.SaveChangesAsync();
        var repository = new PublicJobRepository(f.Context, new Clock());
        var (items, total) = await repository.SearchAsync(new PublicJobQuery());
        Assert.Equal(2, total);
        Assert.All(items, dto => { Assert.Equal(f.Company.Id, dto.CompanyId); Assert.Equal(logo, dto.CompanyLogoUrl); });
        var detail = await repository.GetDetailsAsync("available-one");
        Assert.NotNull(detail);
        Assert.Equal(f.Company.Id, detail.CompanyId);
        Assert.Equal(logo, detail.CompanyLogoUrl);
        Assert.Null(await repository.GetDetailsAsync("expired"));
    }

    private static JobSource Source(AtsType type)
    {
        var company = new Company { Name = "Acme", IsVerified = true };
        return new() { Company = company, CompanyId = company.Id, AtsType = type, AtsIdentifier = "acme", CareerPageUrl = "https://acme.example/careers" };
    }

    private static JobSourcePublicationApproval Approval(JobSource source) => new()
    {
        CompanyId = source.CompanyId, AtsType = source.AtsType, AtsIdentifier = source.AtsIdentifier!.Trim(),
        CareerPageUrl = source.CareerPageUrl, RightsEvidence = "test-publication-permission", RightsExpireAtUtc = new(JobSourceFixture.Now.AddDays(1))
    };
    private static JobSourcePublicationPolicy Policy(JobSource source, int limit = 0)
    {
        var approval = Approval(source);
        approval.TestImportLimit = limit;
        return Policy(source, approval);
    }
    private static JobSourcePublicationPolicy Policy(JobSource source, JobSourcePublicationApproval approval) =>
        new(Options.Create(new JobAggregationOptions { SourceApprovals = new() { [source.Id.ToString("D")] = approval } }), new Clock());

    private static ICompleteExternalJobProvider Provider(AtsType type, IHttpClientFactory factory) => type switch
    {
        AtsType.Greenhouse => new GreenhouseExternalJobProvider(factory), AtsType.Lever => new LeverExternalJobProvider(factory),
        AtsType.Ashby => new AshbyExternalJobProvider(factory), _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    private static string Page(Uri uri, int total)
    {
        var skip = int.Parse(uri.Query.TrimStart('?').Split('&').Single(x => x.StartsWith("skip=", StringComparison.Ordinal))[5..], System.Globalization.CultureInfo.InvariantCulture);
        return LeverPage(skip, Math.Min(100, Math.Max(0, total - skip)));
    }
    private static string LeverPage(int skip, int count) => JsonSerializer.Serialize(Enumerable.Range(skip, count).Select(i => new
    {
        id = $"posting-{i}", text = $"Engineer {i}", country = "IN", descriptionPlain = "Develop software services.",
        hostedUrl = $"https://jobs.lever.co/acme/posting-{i}", categories = new { location = "Pune, India" }
    }));
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(JobSourceFixture.Now); }
    private sealed class AdvancingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(JobSourceFixture.Now);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class ExpiringIngestion(AdvancingClock clock) : IJobIngestionService
    {
        public int Calls { get; private set; }
        public Task<JobIngestionResult> IngestAsync(RawExternalJob job, CancellationToken cancellationToken = default)
        {
            Calls++;
            clock.Now = new(JobSourceFixture.Now.AddDays(2));
            return Task.FromResult(new JobIngestionResult { Outcome = JobIngestionOutcome.Unchanged });
        }
    }
    private sealed class NeverIngest : IJobIngestionService
    {
        public Task<JobIngestionResult> IngestAsync(RawExternalJob job, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("An empty snapshot must not reach ingestion.");
    }
    private sealed class EmptyProvider(AtsType type) : ICompleteExternalJobProvider
    {
        public AtsType AtsType => type;
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalJobSourceSnapshot([], 0, true));
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<RawExternalJob>>([]);
    }
    private sealed class Factory(Func<Uri, string> response, HttpStatusCode status = HttpStatusCode.OK,
        HttpStatusCode detailStatus = HttpStatusCode.OK) : HttpMessageHandler, IHttpClientFactory
    {
        public List<Uri> Requests { get; } = [];
        public HttpClient CreateClient(string name) => new(this, false) { BaseAddress = new Uri("https://api.example.test/") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            lock (Requests) Requests.Add(uri);
            // Greenhouse optional detail enrichment: valid identity with no invented deadline.
            var content = uri.AbsolutePath.Contains("/jobs/", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { id = uri.Segments.Last() }) : response(uri);
            return Task.FromResult(new HttpResponseMessage(uri.AbsolutePath.Contains("/jobs/", StringComparison.Ordinal) ? detailStatus : status)
                { Content = new StringContent(content) });
        }
    }
}
