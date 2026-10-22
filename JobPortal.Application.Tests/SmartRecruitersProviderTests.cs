using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Tests.EmployerDirectIntegrationTests;
using static JobPortal.Application.Tests.GenericIndiaJobIngestionTests;

namespace JobPortal.Application.Tests;

public sealed class SmartRecruitersProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("ExampleCompany")]
    public async Task SourceCreateUpdateScheduleAndManualQueueUseExistingArchitecture(string? identifier)
    {
        using var f = new JobSourceFixture();
        var request = f.Request with { AtsType = AtsType.SmartRecruiters,
            CareerPageUrl = "https://careers.smartrecruiters.com/ExampleCompany", AtsIdentifier = identifier };
        var created = await f.Service.CreateAsync(request);
        Assert.Equal("ExampleCompany", created.AtsIdentifier);
        var updated = await f.Service.UpdateAsync(created.Id, request);
        Assert.Equal(AtsType.SmartRecruiters, updated.AtsType);
        Assert.Contains(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, 25), x => x.Id == created.Id);
        var store = Stub.Create<IJobSourceRunStore>((method, args) =>
        {
            Assert.Equal("EnqueueAsync", method);
            return Task.FromResult(new JobSourceRun { JobSourceId = (Guid)args[0]! });
        });
        var queued = await new JobSourceRunService(f.Repository, store, TimeProvider.System).EnqueueAsync(created.Id, null, default);
        Assert.Equal(created.Id, queued.JobSourceId);
    }

    [Theory]
    [InlineData("../Other")]
    [InlineData("https://evil.test")]
    [InlineData("ExampleCompany?x=1")]
    [InlineData("ExampleCompany#fragment")]
    [InlineData("name@host")]
    [InlineData("%2e%2e")]
    [InlineData("ExampleCompany/other")]
    [InlineData("ExampleCompany\\other")]
    [InlineData("ExampleCompany\n")]
    [InlineData("OtherCompany")]
    public void UnsafeOrConflictingIdentifierIsRejected(string identifier) => Assert.False(
        JobSourceConfiguration.Resolve(AtsType.SmartRecruiters, "https://careers.smartrecruiters.com/ExampleCompany", identifier).IsValid);

    [Theory]
    [InlineData("http://careers.smartrecruiters.com/ExampleCompany")]
    [InlineData("https://evil.test/ExampleCompany")]
    [InlineData("https://user@careers.smartrecruiters.com/ExampleCompany")]
    [InlineData("https://careers.smartrecruiters.com:444/ExampleCompany")]
    [InlineData("https://careers.smartrecruiters.com/ExampleCompany?x=1")]
    [InlineData("https://careers.smartrecruiters.com/ExampleCompany#x")]
    [InlineData("https://careers.smartrecruiters.com/ExampleCompany/../Other")]
    [InlineData("https://careers.smartrecruiters.com/ExampleCompany/%2e%2e")]
    [InlineData("https://careers.smartrecruiters.com")]
    public void UnsafeCareerUrlsAreRejected(string url) => Assert.False(JobSourceConfiguration.Resolve(AtsType.SmartRecruiters, url, null).IsValid);

    [Fact]
    public void RegistrationIsFirstClassAndNoMigrationIsRequired()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Single(scope.ServiceProvider.GetServices<IExternalJobProvider>(), x => x.AtsType == AtsType.SmartRecruiters);
        Assert.Equal(7, (int)AtsType.SmartRecruiters);
        using var db = new PostgresJobPortalDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task InventoryEnumerates347BeforeAnyDetailsAndMapsIndependentSections()
    {
        var handler = new FixtureHandler(347);
        var snapshot = await Provider(handler).FetchSnapshotAsync(Source());
        Assert.True(snapshot.IsComplete);
        Assert.Equal(347, snapshot.ReportedListingTotal);
        Assert.Equal(347, snapshot.Jobs.Count);
        Assert.Equal(new[] { 0, 100, 200, 300 }, handler.Offsets.Take(4));
        Assert.All(handler.Requests.Take(4), uri => Assert.Contains("destination=PUBLIC&limit=100&offset=", uri.Query));
        Assert.All(handler.Requests, uri => Assert.Equal("api.smartrecruiters.com", uri.Host));
        var job = snapshot.Jobs.First();
        Assert.Equal(Id(1), job.ExternalId);
        Assert.Equal("Engineer 1", job.Title);
        Assert.Equal("Pune, Maharashtra, IN", job.Location);
        Assert.Equal(DateTime.Parse("2026-10-20T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(), job.SourcePostedAtUtc);
        Assert.Contains("Build services", job.Description);
        Assert.Contains("Company introduction", job.Description);
        Assert.Contains("Additional information", job.Description);
        Assert.Equal("Required skills", job.Requirements);
        Assert.Null(job.Responsibilities); Assert.Null(job.Benefits);
        Assert.Equal("Engineering", job.ExternalCategory);
        Assert.Equal("Full-time", job.EmploymentTypeText);
        Assert.Equal(WorkplaceType.Hybrid, job.WorkplaceType);
        Assert.Null(job.ExperienceLevel); // combined mid/senior is not a single normalized level
        Assert.Equal("https://jobs.smartrecruiters.com/ExampleCompany/1-engineer", job.CanonicalJobUrl);
        Assert.Equal(job.CanonicalJobUrl + "?oga=true", job.ApplicationUrl);
        Assert.Contains("industry", job.SourceMetadataJson);
        Assert.Equal(347, handler.DetailCalls);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("totalChange")]
    [InlineData("badOffset")]
    [InlineData("badLimit")]
    [InlineData("badTotal")]
    [InlineData("huge")]
    [InlineData("malformed")]
    [InlineData("redirect")]
    [InlineData("404")]
    [InlineData("401")]
    [InlineData("429")]
    [InlineData("500")]
    [InlineData("timeout")]
    public async Task UnsafeInventoryNeverAuthorizesClosureOrFetchesDetails(string fault)
    {
        var handler = new FixtureHandler(101, fault);
        var snapshot = await Provider(handler).FetchSnapshotAsync(Source());
        Assert.False(snapshot.IsComplete); Assert.Empty(snapshot.Jobs); Assert.Equal(0, handler.DetailCalls);
        Assert.NotEmpty(snapshot.ProviderReasonCounts!);
        if (fault is "429" or "500") Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData("detailId")]
    [InlineData("detailUuid")]
    [InlineData("detailInactive")]
    [InlineData("detailUnsafeUrl")]
    [InlineData("detailUrlEncodedTraversal")]
    [InlineData("detailMalformed")]
    [InlineData("detail404")]
    [InlineData("detailTimeout")]
    public async Task FailedEnrichmentCannotCertifyACompleteInventory(string fault)
    {
        var snapshot = await Provider(new FixtureHandler(1, fault)).FetchSnapshotAsync(Source());
        Assert.False(snapshot.IsComplete); Assert.Empty(snapshot.Jobs); Assert.True(snapshot.Skipped > 0);
    }

    [Fact]
    public async Task InventoryChangesDuringEnrichmentCannotAuthorizeReconciliation()
    {
        var snapshot = await Provider(new FixtureHandler(1, "verificationChange")).FetchSnapshotAsync(Source());
        Assert.False(snapshot.IsComplete); Assert.Single(snapshot.Jobs);
    }

    [Fact]
    public async Task FullListingAvoidsUnnecessaryDetailsAndNeverFollowsRef()
    {
        var handler = new FixtureHandler(1, "fullListing");
        var snapshot = await Provider(handler).FetchSnapshotAsync(Source());
        Assert.True(snapshot.IsComplete); Assert.Equal(0, handler.DetailCalls);
        Assert.All(handler.Requests, uri => Assert.DoesNotContain("127.0.0.1", uri.AbsoluteUri));
    }

    [Theory]
    [InlineData("noCanonical")]
    [InlineData("encodedSlug")]
    public async Task SuppliedCandidateUrlsSupportApplyOnlyAndEncodedPublicSlugs(string fault)
    {
        var snapshot = await Provider(new FixtureHandler(1, fault)).FetchSnapshotAsync(Source());
        Assert.True(snapshot.IsComplete);
        var job = Assert.Single(snapshot.Jobs);
        if (fault == "noCanonical") Assert.Equal(job.ApplicationUrl, job.CanonicalJobUrl);
        else Assert.Contains("%C3", job.ApplicationUrl);
    }

    [Fact]
    public async Task EmptyHttp200IsNotCompanyExistenceProof()
    {
        var snapshot = await Provider(new FixtureHandler(0)).FetchSnapshotAsync(Source());
        Assert.Empty(snapshot.Jobs); Assert.Equal(0, snapshot.ReportedListingTotal);
        Assert.False(snapshot.IsComplete);
        Assert.Contains("SMARTRECRUITERS_EMPTY_COMPANY_UNPROVEN", snapshot.ProviderReasonCounts!.Keys);
    }

    [Theory]
    [InlineData("pages")]
    [InlineData("requests")]
    [InlineData("bytes")]
    public async Task InventoryBudgetsStopBeforeEnrichment(string budget)
    {
        var settings = Settings();
        if (budget == "pages") settings.MaximumPages = 1;
        if (budget == "requests") settings.MaximumRequests = 1;
        if (budget == "bytes") { settings.MaximumResponseBytes = 1024; settings.MaximumScanBytes = 1024; }
        var handler = new FixtureHandler(347);
        var snapshot = await Provider(handler, settings).FetchSnapshotAsync(Source());
        Assert.False(snapshot.IsComplete); Assert.Equal(0, handler.DetailCalls);
        Assert.Contains(snapshot.ProviderReasonCounts!.Keys, x => x.Contains("BUDGET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DetailBudgetCannotStarveInitialInventoryEnumeration()
    {
        var handler = new FixtureHandler(347); var settings = Settings(); settings.MaximumRequests = 6;
        var snapshot = await Provider(handler, settings).FetchSnapshotAsync(Source());
        Assert.False(snapshot.IsComplete); Assert.Equal(4, handler.Offsets.Count); Assert.Equal(2, handler.DetailCalls);
        Assert.Equal(347, snapshot.ReportedListingTotal);
    }

    [Fact]
    public async Task ConnectionTesterUsesPublicListingWithoutEnrichment()
    {
        var handler = new FixtureHandler(1);
        var tester = new JobSourceConnectionTester(Stub.Create<IHttpClientFactory>((_, _) => new HttpClient(handler, false)));
        var result = await tester.TestAsync(Source());
        Assert.True(result.ListingReachable);
        Assert.Equal(0, handler.DetailCalls);
    }

    [Fact]
    public async Task LongRetryAfterDefersTheNextProviderScopeWithoutAnotherRequest()
    {
        var handler = new FixtureHandler(1, "longRetry");
        var state = new AggregationHttpRetryState();
        var factory = Stub.Create<IHttpClientFactory>((_, _) => new HttpClient(handler, false));
        var one = new SmartRecruitersJobSourceProvider(factory, Options.Create(Settings()), retryState: state);
        var first = await one.FetchSnapshotAsync(Source());
        Assert.False(first.IsComplete); Assert.Single(handler.Requests);
        var two = new SmartRecruitersJobSourceProvider(factory, Options.Create(Settings()), retryState: state);
        Assert.False((await two.FetchSnapshotAsync(Source())).IsComplete);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ScanDeadlineProducesIncompleteSnapshot()
    {
        var handler = new FixtureHandler(1, "stall"); var settings = Settings(); settings.ScanTimeoutSeconds = 1;
        var snapshot = await Provider(handler, settings).FetchSnapshotAsync(Source());
        Assert.False(snapshot.IsComplete);
        Assert.Contains("SMARTRECRUITERS_TIMEOUT", snapshot.ProviderReasonCounts!.Keys);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(new FixtureHandler(1)).FetchSnapshotAsync(Source(), cancellation.Token));
    }

    [Fact]
    public async Task RunnerUsesStableUuidUpdatesAndRequiresTwoCompleteMissingScans()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.SmartRecruiters; f.Source.AtsIdentifier = "ExampleCompany";
        f.Source.CareerPageUrl = "https://careers.smartrecruiters.com/ExampleCompany";
        await f.Context.SaveChangesAsync();
        var handler = new FixtureHandler(2);
        var runner = Runner(f, Provider(handler));
        var first = await runner.RunAsync(f.Source.Id); Assert.Equal(2, first.Created);
        var saved = await f.Context.Jobs.SingleAsync(x => x.ExternalJobId == Id(2));
        var originalId = saved.Id;
        handler.Title = "Changed Engineer"; handler.City = "Bengaluru"; handler.Reverse = true;
        var repeated = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, repeated.Created); Assert.Equal(2, repeated.Updated);
        Assert.Equal(originalId, (await f.Context.Jobs.SingleAsync(x => x.ExternalJobId == Id(2))).Id);
        saved = await f.Context.Jobs.SingleAsync(x => x.Id == originalId);
        saved.Status = JobStatus.Published; saved.SourceOwnsPublication = true; await f.Context.SaveChangesAsync();
        handler.Count = 1; handler.Fault = "detailMalformed";
        var incomplete = await runner.RunAsync(f.Source.Id); Assert.False(incomplete.ReconciliationSafe);
        Assert.Equal(JobStatus.Published, (await f.Context.Jobs.SingleAsync(x => x.Id == originalId)).Status);
        handler.Fault = null;
        var missingOne = await runner.RunAsync(f.Source.Id); Assert.True(missingOne.ReconciliationSafe); Assert.Equal(0, missingOne.Closed);
        var missingTwo = await runner.RunAsync(f.Source.Id); Assert.Equal(1, missingTwo.Closed);
    }

    [Fact]
    public async Task ExplicitIndiaEvidenceSurvivesProviderMappingPersistenceAndPublication()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.SmartRecruiters; f.Source.AtsIdentifier = "ExampleCompany";
        f.Source.CareerPageUrl = "https://careers.smartrecruiters.com/ExampleCompany";
        await f.Context.SaveChangesAsync();
        var handler = new FixtureHandler(1) { City = "Bengaluru", Region = "Karnataka", Country = "in" };
        var provider = Provider(handler);
        var snapshot = await provider.FetchSnapshotAsync(f.Source);
        var mapped = Assert.Single(snapshot.Jobs);
        Assert.Equal("in", Assert.Single(mapped.CountryCodes));
        Assert.Equal("Bengaluru, Karnataka, in", mapped.Location);
        Assert.Equal(IndiaJobEligibilityDecision.Accepted, IndiaJobEligibilityPolicy.Evaluate(mapped).Decision);

        var result = await Runner(f, provider).RunAsync(f.Source.Id);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Published);
        Assert.Equal(0, result.NeedsReview);
        var saved = await f.Context.Jobs.SingleAsync();
        Assert.Equal(IndiaJobEligibilityDecision.Accepted, ImportedJobIntegrity.Eligibility(saved).Decision);
        using var metadata = JsonDocument.Parse(saved.SourceMetadataJson!);
        Assert.Equal("in", metadata.RootElement.GetProperty("CountryCodes")[0].GetString());
        Assert.Equal("Bengaluru, Karnataka, in", metadata.RootElement.GetProperty("Location").GetString());
        Assert.Equal(JsonValueKind.Object, metadata.RootElement.GetProperty("ProviderPayload").GetProperty("location").ValueKind);
        Assert.Equal(JobStatus.Published, saved.Status);
    }

    [Theory]
    [InlineData("Paris", "FR", 1, 0)]
    [InlineData(null, null, 0, 1)]
    public async Task ExplicitNonIndiaAndUnknownOrRemoteOnlyLocationsDoNotPublish(
        string? city, string? country, int expectedNonIndia, int expectedUnknown)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.SmartRecruiters; f.Source.AtsIdentifier = "ExampleCompany";
        f.Source.CareerPageUrl = "https://careers.smartrecruiters.com/ExampleCompany";
        await f.Context.SaveChangesAsync();
        var handler = new FixtureHandler(1) { City = city, Country = country,
            Region = country == "IN" ? "Maharashtra" : null, Hybrid = false, Remote = true };
        var result = await Runner(f, Provider(handler)).RunAsync(f.Source.Id);
        Assert.True(result.Created == 0, JsonSerializer.Serialize(result));
        Assert.Equal(expectedNonIndia, result.NonIndiaRejected);
        Assert.Equal(expectedUnknown, result.IndiaEligibilityUnknown);
        Assert.Equal(0, result.Published);
        Assert.Empty(await f.Context.Jobs.ToArrayAsync());
    }

    [Fact]
    public async Task ExistingNeedsReviewSmartRecruitersJobIsUpdatedAndPublishedWithoutDuplicate()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.SmartRecruiters; f.Source.AtsIdentifier = "ExampleCompany";
        f.Source.CareerPageUrl = "https://careers.smartrecruiters.com/ExampleCompany";
        await f.Context.SaveChangesAsync();
        var handler = new FixtureHandler(1);
        var detail = JsonSerializer.Serialize(new
        {
            id = "1", uuid = Id(1), name = "Engineer 1", active = true,
            company = new { identifier = "ExampleCompany", name = "Example" },
            location = new { city = "Pune", region = "Maharashtra", country = "IN", remote = true, hybrid = true },
            applyUrl = "https://jobs.smartrecruiters.com/ExampleCompany/1-engineer?oga=true",
            postingUrl = "https://jobs.smartrecruiters.com/ExampleCompany/1-engineer"
        });
        var existingId = Guid.NewGuid();
        f.Context.Jobs.Add(new Job
        {
            Id = existingId, Title = "Engineer 1", Slug = "engineer-1-legacy", Description = "Existing listing",
            Location = "Pune, Maharashtra, IN", ApplicationUrl = "https://jobs.smartrecruiters.com/ExampleCompany/1-engineer?oga=true",
            CompanyId = f.Company.Id, JobSourceId = f.Source.Id, ExternalJobId = Id(1),
            SourceOwnsPublication = true, Status = JobStatus.Draft, SourceMetadataJson = detail,
            FirstSeenAtUtc = JobSourceFixture.Now, LastSeenAtUtc = JobSourceFixture.Now
        });
        await f.Context.SaveChangesAsync();
        var result = await Runner(f, Provider(handler)).RunAsync(f.Source.Id);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Published);
        Assert.Equal(0, result.NeedsReview);
        var saved = Assert.Single(await f.Context.Jobs.ToArrayAsync());
        Assert.Equal(existingId, saved.Id);
        Assert.Equal(IndiaJobEligibilityDecision.Accepted, ImportedJobIntegrity.Eligibility(saved).Decision);
        Assert.Equal(JobStatus.Published, saved.Status);
        Assert.True(result.ReconciliationSafe);
    }

    internal static JobSource Source() => new() { AtsType = AtsType.SmartRecruiters, AtsIdentifier = "ExampleCompany",
        CareerPageUrl = "https://careers.smartrecruiters.com/ExampleCompany", Company = new Company { Name = "Example" } };
    internal static SmartRecruitersOptions Settings() => new() { RequestIntervalMilliseconds = 0 };
    internal static SmartRecruitersJobSourceProvider Provider(FixtureHandler handler, SmartRecruitersOptions? settings = null) =>
        new(Stub.Create<IHttpClientFactory>((_, _) => new HttpClient(handler, false)), Options.Create(settings ?? Settings()));
    internal static string Id(int number) => new Guid(number, 0, 0, new byte[8]).ToString("D");
    internal sealed class FixtureHandler(int count, string? fault = null) : HttpMessageHandler
    {
        public int Count { get; set; } = count;
        public string? Fault { get; set; } = fault;
        public string Title { get; set; } = "Engineer";
        public string? City { get; set; } = "Pune";
        public string? Country { get; set; } = "IN";
        public string? Region { get; set; } = "Maharashtra";
        public bool Hybrid { get; set; } = true;
        public bool Remote { get; set; } = true;
        public bool Reverse { get; set; }
        public int DetailCalls { get; private set; }
        public List<int> Offsets { get; } = [];
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("X-SmartToken"));
            Requests.Add(request.RequestUri!);
            if (Fault == "stall") return WaitForCancellation(cancellationToken);
            if (Fault == "longRetry") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                { Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1)) } });
            if (Fault == "timeout") throw new TaskCanceledException();
            if (Fault == "redirect") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://127.0.0.1/") } });
            if (int.TryParse(Fault, out var code)) return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero) } });
            if (Fault == "malformed") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{") });
            if (request.RequestUri!.Query.Length > 0)
            {
                Assert.Contains("destination=PUBLIC&limit=100&offset=", request.RequestUri.Query);
                var offset = int.Parse(request.RequestUri.Query.Split("offset=")[1], System.Globalization.CultureInfo.InvariantCulture);
                Offsets.Add(offset);
                var total = Fault == "verificationChange" && Offsets.Count > 1 ? Count + 1 : Count;
                var records = Enumerable.Range(offset + 1, Math.Max(0, Math.Min(100, total - offset))).Select(i =>
                    Fault == "fullListing" ? Detail(i) : Listing(Fault == "duplicate" && offset > 0 ? 1 : i)).ToArray();
                if (Reverse) Array.Reverse(records);
                if (Fault == "empty" && offset > 0) records = [];
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                {
                    limit = Fault == "badLimit" ? 10 : 100, offset = Fault == "badOffset" ? -1 : offset,
                    totalFound = Fault == "badTotal" ? (object)"unknown" : Fault == "huge" ? 20001 : Fault == "totalChange" && offset > 0 ? total + 1 : total,
                    content = records
                }) });
            }
            DetailCalls++;
            if (Fault == "detailTimeout") throw new TaskCanceledException();
            if (Fault == "detailMalformed") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{") });
            if (Fault == "detail404") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var id = new Guid(request.RequestUri.Segments[^1]);
            var numeric = Enumerable.Range(1, Count).Single(i => Id(i) == id.ToString("D"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Detail(numeric)) });
        }
        private static async Task<HttpResponseMessage> WaitForCancellation(CancellationToken token)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        }
        private object Listing(int i) => new { id = i.ToString(System.Globalization.CultureInfo.InvariantCulture), uuid = Id(i), name = Title + " " + i,
            company = new { identifier = "ExampleCompany" }, @ref = "http://127.0.0.1/never-fetch" };
        private object Detail(int i) => new { id = (Fault == "detailId" ? 999 : i).ToString(System.Globalization.CultureInfo.InvariantCulture), uuid = Fault == "detailUuid" ? Id(999) : Id(i),
            name = Title + " " + i, active = Fault != "detailInactive", company = new { identifier = "ExampleCompany", name = "Example" },
            location = new { city = City, region = Region, country = Country, remote = Remote, hybrid = Hybrid },
            releasedDate = "2026-10-20T10:00:00Z", typeOfEmployment = new { label = "Full-time" }, experienceLevel = new { id = "mid_senior_level" },
            department = new { label = "Engineering" }, function = new { label = "Technology" }, industry = new { label = "Software" },
            postingUrl = Fault == "noCanonical" ? null : $"https://jobs.smartrecruiters.com/ExampleCompany/{i}-engineer",
            applyUrl = Fault == "detailUnsafeUrl" ? "http://127.0.0.1/" : Fault == "detailUrlEncodedTraversal"
                ? $"https://jobs.smartrecruiters.com/ExampleCompany/{i}-engineer%2f.." : Fault == "encodedSlug"
                ? $"https://jobs.smartrecruiters.com/ExampleCompany/{i}-ing%C3%A9nieur" : $"https://jobs.smartrecruiters.com/ExampleCompany/{i}-engineer?oga=true",
            jobAd = new { sections = new { jobDescription = new { text = "<p>Build services</p>" }, qualifications = new { text = "<p>Required skills</p>" },
                companyDescription = new { title = "Company introduction", text = "About the employer" }, additionalInformation = new { title = "Additional information", text = "Other terms" } } } };
    }
}
