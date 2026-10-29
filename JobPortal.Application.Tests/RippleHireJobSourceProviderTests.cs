using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Application.Services;
using JobPortal.Persistence;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace JobPortal.Application.Tests;
public sealed class RippleHireJobSourceProviderTests
{
    private const string Board = "https://ltm.ripplehire.com/candidate/?lang=en&source=INDEED&token=pwHdNly1GEtOKNRuAGnS";
    private static JobSource Source() => new() { CompanyId = Guid.NewGuid(), Company = new Company { Name = "Configured employer" }, CareerPageUrl = Board, AtsType = AtsType.RippleHire };
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RippleHire", name + ".json"));
    private static string Listing(int count)
    {
        var root = JsonNode.Parse(Fixture("listing"))!;
        root["totalJobCount"] = count;
        var entries = root["jobVoList"]!.AsArray();
        while (entries.Count > Math.Min(count, 10)) entries.RemoveAt(entries.Count - 1);
        return root.ToJsonString();
    }
    [Fact]
    public void ProviderIdentifiersRemainStable()
    {
        Assert.Equal(8, (int)AtsType.RippleHire);
        Assert.Equal(7, (int)AtsType.SmartRecruiters);
        Assert.Equal(6, (int)AtsType.OracleRecruitingCloud);
    }
    [Theory]
    [InlineData("http://ltm.ripplehire.com/candidate/?token=abc")]
    [InlineData("https://127.0.0.1/candidate/?token=abc")]
    [InlineData("https://localhost/candidate/?token=abc")]
    [InlineData("https://ltm.ripplehire.com.evil.com/candidate/?token=abc")]
    [InlineData("https://ltm.ripplehire.com:444/candidate/?token=abc")]
    [InlineData("https://user@ltm.ripplehire.com/candidate/?token=abc")]
    [InlineData("https://ltm.ripplehire.com/ripplehire/candidate?token=abc")]
    [InlineData("https://ltm.ripplehire.com/candidate/?token=abc&token=def")]
    [InlineData("https://ltm.ripplehire.com/candidate/?token=abc#detail/job/1")]
    public void UnsafeSourcesAreRejected(string url) => Assert.False(RippleHireSourceConfiguration.Resolve(url, null).IsValid);
    [Fact]
    public void ConfigurationDerivesPublicTokenAndRejectsConflicts()
    {
        var config = JobSourceConfiguration.Resolve(AtsType.RippleHire, Board, null);
        Assert.True(config.IsValid);
        Assert.Equal("pwHdNly1GEtOKNRuAGnS", config.Identifier);
        Assert.False(RippleHireSourceConfiguration.Resolve(Board, "other").IsValid);
        Assert.True(new SaveJobSourceRequestValidator().Validate(new SaveJobSourceRequest(Guid.NewGuid(), Board, AtsType.RippleHire, null)).IsValid);
    }
    [Fact]
    public async Task VerifiedFixturesMapPublicJob()
    {
        var handler = new Handler((_, i) => Json(i == 0 ? Listing(1) : Fixture("detail")));
        var job = Assert.Single(await new RippleHireJobSourceProvider(new Factory(handler)).FetchJobsAsync(Source()));
        Assert.Equal("905121", job.ExternalId);
        Assert.Equal("Specialist - Data Engineering", job.Title);
        Assert.Equal("Configured employer", job.CompanyName);
        Assert.Equal("Bengaluru", job.Location);
        Assert.Contains("India", job.AdditionalLocations);
        Assert.Contains("observability", job.Description);
        Assert.Equal(Board + "#detail/job/905121", job.ApplicationUrl);
        Assert.Equal(new DateTime(2026, 10, 29, 7, 38, 55, DateTimeKind.Utc), job.SourcePostedAtUtc);
        Assert.Null(job.SalaryMin);
    }
    [Fact]
    public async Task MissingDateRemainsUnknown()
    {
        var detail = JsonNode.Parse(Fixture("detail"))!;
        detail["jobVO"]!["publishDetails"] = null;
        var job = Assert.Single(await new RippleHireJobSourceProvider(new Factory(new Handler((_, i) => Json(i == 0 ? Listing(1) : detail.ToJsonString())))).FetchJobsAsync(Source()));
        Assert.Null(job.SourcePostedAtUtc);
    }
    [Fact]
    public async Task LaterFailureDoesNotReturnPartialJobs()
    {
        var handler = new Handler((_, i) => i == 0 ? Json(Listing(2)) : new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<HttpRequestException>(() => new RippleHireJobSourceProvider(new Factory(handler)).FetchJobsAsync(Source()));
    }
    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task TransientResponsesHaveBoundedRetries(int status)
    {
        var handler = new Handler((_, _) => new HttpResponseMessage((HttpStatusCode)status) { Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero) } });
        await Assert.ThrowsAsync<HttpRequestException>(() => new RippleHireJobSourceProvider(new Factory(handler)).FetchJobsAsync(Source()));
        Assert.Equal(3, handler.Count);
    }
    [Fact]
    public async Task CancellationPreventsRequests()
    {
        var handler = new Handler((_, _) => Json(Listing(1)));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RippleHireJobSourceProvider(new Factory(handler)).FetchJobsAsync(Source(), cancelled.Token));
        Assert.Equal(0, handler.Count);
    }
    [Fact]
    public async Task EmptySourceCannotReportSuccess()
    {
        var result = await new JobSourceConnectionTester(new Factory(new Handler((_, _) => Json(Listing(0))))).TestAsync(Source());
        Assert.False(result.ListingReachable);
    }
    [Fact]
    public void CanonicalizationKeepsJobsDistinct()
    {
        var canonicalizer = new UrlCanonicalizer();
        Assert.NotEqual(canonicalizer.Canonicalize(Board + "#detail/job/1"), canonicalizer.Canonicalize(Board + "#detail/job/2"));
        Assert.Equal("https://jobs.lever.co/acme/1", canonicalizer.Canonicalize("https://jobs.lever.co/acme/1#fragment"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PaginationUsesPublicContractAndRejectsDuplicateIdentity(bool repeat)
    {
        var handler = new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("candidatejobsearch", StringComparison.Ordinal)) return Json(Listing(11));
                var page = JsonNode.Parse(Fixture("page"))!.AsArray();
                while (page.Count > 1) page.RemoveAt(page.Count - 1);
                if (repeat) page[0]!["jobSeq"] = "905121";
                return Json(page.ToJsonString());
            }
            var detail = JsonNode.Parse(Fixture("detail"))!;
            detail["jobVO"]!["jobSeq"] = request.RequestUri!.Query.Split("jobSeq=")[1].Split('&')[0];
            return Json(detail.ToJsonString());
        });
        var provider = new RippleHireJobSourceProvider(new Factory(handler));
        if (repeat) await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchJobsAsync(Source()));
        else
        {
            var jobs = await provider.FetchJobsAsync(Source());
            Assert.Equal(11, jobs.Count);
            Assert.Equal(11, jobs.Select(x => x.ExternalId).Distinct().Count());
        }
    }
    [Fact]
    public async Task ParsedFactsUseExistingIndiaPolicy()
    {
        var job = Assert.Single(await new RippleHireJobSourceProvider(new Factory(new Handler((_, i) => Json(i == 0 ? Listing(1) : Fixture("detail"))))).FetchJobsAsync(Source()));
        Assert.Equal(JobPortal.Application.Services.IndiaJobEligibilityDecision.Accepted, JobPortal.Application.Services.IndiaJobEligibilityPolicy.Evaluate(job).Decision);
        Assert.NotEqual(JobPortal.Application.Services.IndiaJobEligibilityDecision.Accepted, JobPortal.Application.Services.IndiaJobEligibilityPolicy.Evaluate(job with { Location = "USA", AdditionalLocations = [] }).Decision);
    }
    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    [Fact]
    public async Task RepeatedImportUpdatesSourceIdentityWithoutDuplicatingJobs()
    {
        using var fixture = new JobSourceFixture();
        fixture.Source.AtsType = AtsType.RippleHire;
        fixture.Source.CareerPageUrl = Board;
        fixture.Source.AtsIdentifier = null;
        var parsed = Assert.Single(await new RippleHireJobSourceProvider(new Factory(new Handler((_, i) => Json(i == 0 ? Listing(1) : Fixture("detail"))))).FetchJobsAsync(fixture.Source));
        var jobs = new JobRepository(fixture.Context);
        var fingerprints = new JobFingerprintService();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(fixture.Context), new CategoryManagementRepository(fixture.Context),
            new JobDeduplicationService(jobs, fingerprints, new UrlCanonicalizer()), fingerprints, new UnitOfWork(fixture.Context), TimeProvider.System, fixture.Locks, new UrlCanonicalizer());
        var raw = parsed with { JobSourceId = fixture.Source.Id, CategoryId = fixture.Category.Id };
        var first = await ingestion.IngestAsync(raw);
        await ingestion.IngestAsync(raw with { Title = "Updated public job title" });
        var saved = Assert.Single(await fixture.Context.Jobs.ToArrayAsync());
        Assert.Equal(first.JobId, saved.Id);
        Assert.Equal("Updated public job title", saved.Title);
        Assert.Equal(raw.ApplicationUrl, saved.ApplicationUrl);
        Assert.Equal(fixture.Source.Id, saved.JobSourceId);
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request, Count++));
    }
}
