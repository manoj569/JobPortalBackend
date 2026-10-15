using System.Net;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class GenericWorkdayConfigurationTests
{
    [Theory]
    [InlineData("jj.wd5.myworkdayjobs.com", "jj", "JJ")]
    [InlineData("ag.wd3.myworkdayjobs.com", "ag", "Airbus")]
    [InlineData("walmart.wd504.myworkdayjobs.com", "walmart", "walmartexternal")]
    public void GeneratesExactCasePreservingEndpoints(string host, string tenant, string site)
    {
        var source = Source(host, site, $"{tenant}/{site}");
        var target = WorkdayJobSourceProvider.ValidateSource(source);
        Assert.Equal(tenant, target.Tenant);
        Assert.Equal(site, target.Site);
        Assert.Equal($"https://{host}/wday/cxs/{tenant}/{site}", target.ApiRoot.AbsoluteUri);
        Assert.Equal($"https://{host}/wday/cxs/{tenant}/{site}/jobs",
            WorkdaySourceConfiguration.Validate(source.CareerPageUrl, source.AtsIdentifier).Configuration!.ListingEndpoint.AbsoluteUri);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(504)]
    [InlineData(98765)]
    public void UsesHostnameStructureRatherThanRegionAllowlist(int region)
    {
        Assert.True(WorkdaySourceConfiguration.Validate($"https://tenant.wd{region}.myworkdayjobs.com/ExactCase", "tenant/ExactCase").IsValid);
    }

    [Theory]
    [InlineData("https://tenant.wd1.myworkdayjobs.com/External", "wd1", null, "External")]
    [InlineData("https://tenant.wd3.myworkdayjobs.com/en-US/External", "wd3", "en-US", "External")]
    [InlineData("https://spgi.wd5.myworkdayjobs.com/SPGI_Careers", "wd5", null, "SPGI_Careers")]
    [InlineData("https://spgi.wd5.myworkdayjobs.com/en-US/SPGI_Careers", "wd5", "en-US", "SPGI_Careers")]
    [InlineData("https://browserstack.wd3.myworkdayjobs.com/External", "wd3", null, "External")]
    [InlineData("https://tenant.wd12.myworkdayjobs.com/External_Rockwell_Automation/", "wd12", null, "External_Rockwell_Automation")]
    [InlineData("https://tenant.wd504.myworkdayjobs.com/en-US/External-Rockwell-Automation-Early-Careers", "wd504", "en-US", "External-Rockwell-Automation-Early-Careers")]
    [InlineData("https://tenant.wd98765.myworkdayjobs.com/Site123", "wd98765", null, "Site123")]
    public void ParsesPublicWorkdayRootsAndBuildsCanonicalCxsUrl(string url, string shard, string? locale, string site)
    {
        var result = WorkdaySourceConfiguration.Parse(url);

        Assert.True(result.IsValid, result.Message);
        var configuration = result.Configuration!;
        Assert.Equal(new Uri(url).Host, configuration.Host);
        Assert.Equal(shard, configuration.Shard);
        Assert.Equal(locale, configuration.Locale);
        Assert.Equal(site, configuration.CareerSite);
        Assert.Equal("https://" + configuration.Host + "/" + (locale is null ? "" : locale + "/") + site,
            configuration.CanonicalCareerPageUrl);
        Assert.Equal($"https://{configuration.Host}/wday/cxs/{configuration.Tenant}/{site}/jobs",
            configuration.ListingEndpoint.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://tenant.wd5.myworkdayjobs.com/Site")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com.evil.com/Site")]
    [InlineData("https://evilmyworkdayjobs.com/Site")]
    [InlineData("https://localhost/Site")]
    [InlineData("https://127.0.0.1/Site")]
    [InlineData("https://10.0.0.1/Site")]
    [InlineData("https://user:password@tenant.wd5.myworkdayjobs.com/Site")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com:8443/Site")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/Site?redirect=https://evil.test")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/Site?")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/Site#fragment")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/Site#")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/Site/job/Some-Job/123")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/job/Some-Job/123")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/search")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/Site/../Other")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/%2e%2e/Site")]
    [InlineData("https://tenant.wd5.myworkdayjobs.com/Site%2fjob")]
    [InlineData("https://tenant.wd0.myworkdayjobs.com/Site")]
    [InlineData("not a url")]
    public void RejectsNonRootsAndUnsafeWorkdayUrls(string url) =>
        Assert.False(WorkdaySourceConfiguration.Parse(url).IsValid);

    [Theory]
    [InlineData("https://spgi.wd5.myworkdayjobs.com/SPGI_Careers", "spgi", "wd5", "SPGI_Careers")]
    [InlineData("https://spgi.wd5.myworkdayjobs.com/en-US/SPGI_Careers", "spgi", "wd5", "SPGI_Careers")]
    [InlineData("https://browserstack.wd3.myworkdayjobs.com/External", "browserstack", "wd3", "External")]
    public void PublicRootCanBeTheIdentifierAndNormalizesToCanonicalCxsIdentity(string url, string tenant, string shard, string site)
    {
        var result = WorkdaySourceConfiguration.Validate(url, url);

        Assert.True(result.IsValid, result.Message);
        Assert.Equal(tenant, result.Configuration!.Tenant);
        Assert.Equal(shard, result.Configuration.Shard);
        Assert.Equal(site, result.Configuration.CareerSite);
        Assert.Equal($"{tenant}/{site}", result.Configuration.Identifier);
        Assert.Equal($"https://{tenant}.{shard}.myworkdayjobs.com/wday/cxs/{tenant}/{site}/jobs",
            result.Configuration.ListingEndpoint.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ag")]
    [InlineData("Airbus")]
    [InlineData("AG/airbus")]
    public void SafelyDerivesCanonicalIdentityFromCareerUrl(string? identifier)
    {
        var result = WorkdaySourceConfiguration.Validate("https://AG.WD3.myworkdayjobs.com/en-US/Airbus", identifier);
        Assert.True(result.IsValid);
        Assert.Equal("ag/Airbus", result.Configuration!.Identifier);
    }

    [Theory]
    [InlineData("http://ag.wd3.myworkdayjobs.com/Airbus", "ag/Airbus", "WORKDAY_ENDPOINT_INVALID")]
    [InlineData("https://ag.wd3.myworkdayjobs.com:8443/Airbus", "ag/Airbus", "WORKDAY_ENDPOINT_INVALID")]
    [InlineData("https://workday.evil.com/Airbus", "ag/Airbus", "WORKDAY_HOST_INVALID")]
    [InlineData("https://ag.wd3.myworkdayjobs.com.evil.com/Airbus", "ag/Airbus", "WORKDAY_HOST_INVALID")]
    [InlineData("https://myworkdayjobs.com/Airbus", "ag/Airbus", "WORKDAY_HOST_INVALID")]
    [InlineData("https://ag.wdX.myworkdayjobs.com/Airbus", "ag/Airbus", "WORKDAY_HOST_INVALID")]
    [InlineData("https://127.0.0.1/Airbus", "ag/Airbus", "WORKDAY_ENDPOINT_INVALID")]
    [InlineData("https://user:secret@ag.wd3.myworkdayjobs.com/Airbus", "ag/Airbus", "WORKDAY_ENDPOINT_INVALID")]
    [InlineData("https://ag.wd3.myworkdayjobs.com/Other/../Airbus", "ag/Airbus", "WORKDAY_ENDPOINT_INVALID")]
    [InlineData("https://ag.wd3.myworkdayjobs.com/%2e%2e/Airbus", "ag/Airbus", "WORKDAY_ENDPOINT_INVALID")]
    [InlineData("https://ag.wd3.myworkdayjobs.com/Airbus%2fjob", "ag/Airbus", "WORKDAY_ENDPOINT_INVALID")]
    [InlineData("https://ag.wd3.myworkdayjobs.com/Airbus?token=secret", "ag/Airbus", "WORKDAY_ENDPOINT_INVALID")]
    [InlineData("https://ag.wd3.myworkdayjobs.com/", "ag/Airbus", "WORKDAY_CAREER_SITE_MISSING")]
    [InlineData("https://ag.wd3.myworkdayjobs.com/Airbus", "/Airbus", "WORKDAY_TENANT_MISSING")]
    [InlineData("https://ag.wd3.myworkdayjobs.com/Airbus", "ag/", "WORKDAY_CAREER_SITE_MISSING")]
    [InlineData("https://ag.wd3.myworkdayjobs.com/Airbus", "other/Airbus", "WORKDAY_CONFIGURATION_CONFLICT")]
    [InlineData("https://ag.wd3.myworkdayjobs.com/Airbus", "ag/Other", "WORKDAY_CONFIGURATION_CONFLICT")]
    public void RejectsUnsafeAndContradictoryConfiguration(string url, string identifier, string code)
    {
        var result = WorkdaySourceConfiguration.Validate(url, identifier);
        Assert.False(result.IsValid);
        Assert.Equal(code, result.ReasonCode);
        Assert.DoesNotContain("secret", result.Message);
    }

    [Theory]
    [InlineData("jj.wd5.myworkdayjobs.com", "jj", "JJ", "jj/JJ")]
    [InlineData("ag.wd3.myworkdayjobs.com", "ag", "Airbus", "Airbus")]
    [InlineData("walmart.wd504.myworkdayjobs.com", "walmart", "walmartexternal", "walmart")]
    public async Task NormalizedConfigurationsUseExistingListingAndDetailContract(string host, string tenant, string site, string identifier)
    {
        var listingCount = 0;
        var detailCount = 0;
        using var handler = new Handler(async request =>
        {
            Assert.Equal(host, request.RequestUri!.Host);
            if (request.Method == HttpMethod.Post)
            {
                listingCount++;
                Assert.Equal($"/wday/cxs/{tenant}/{site}/jobs", request.RequestUri.AbsolutePath);
                Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/json");
                Assert.Contains(request.Headers.AcceptLanguage, value => value.Value == "en-US");
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
                Assert.Equal(20, body.RootElement.GetProperty("limit").GetInt32());
                Assert.Equal(0, body.RootElement.GetProperty("offset").GetInt32());
                Assert.Equal("", body.RootElement.GetProperty("searchText").GetString());
                Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("appliedFacets").ValueKind);
                return Json("""{"total":1,"jobPostings":[{"title":"Graduate Software Engineer","externalPath":"/job/Pune/Graduate_REQ-1","locationsText":"Pune","bulletFields":["REQ-1"]}]}""");
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/wday/cxs/{tenant}/{site}/job/Pune/Graduate_REQ-1", request.RequestUri.AbsolutePath);
            detailCount++;
            return Json("""{"jobPostingInfo":{"title":"Graduate Software Engineer","jobReqId":"REQ-1","jobDescription":"<p>Build reliable systems.</p>","location":"Pune","jobRequisitionLocation":{"country":{"alpha2Code":"IN","descriptor":"India"}}}}""");
        });
        using var client = new HttpClient(handler);
        using var provider = new WorkdayJobSourceProvider(new Factory(client));
        var snapshot = await provider.FetchSnapshotAsync(Source(host, site, identifier));
        Assert.Single(snapshot.Jobs);
        Assert.Equal(1, detailCount);
        Assert.True(listingCount >= 1);
        Assert.True(snapshot.ListingDiagnostics!.ProcessingComplete);
        Assert.False(snapshot.IsComplete); // Workday remains non-authoritative for absence reconciliation.
    }

    [Theory]
    [InlineData("Airbus")]
    [InlineData("ag")]
    [InlineData(null)]
    public async Task AdminCreateAndUpdatePersistCanonicalWorkdayIdentity(string? identifier)
    {
        using var fixture = new JobSourceFixture();
        var request = fixture.Request with { AtsType = AtsType.Workday, CareerPageUrl = "https://ag.wd3.myworkdayjobs.com/Airbus",
            AtsIdentifier = identifier };
        var created = await fixture.Service.CreateAsync(request);
        Assert.Equal("ag/Airbus", created.AtsIdentifier);
        var updated = await fixture.Service.UpdateAsync(created.Id, request with
            { CareerPageUrl = "https://walmart.wd504.myworkdayjobs.com/walmartexternal", AtsIdentifier = "walmartexternal" });
        Assert.Equal("walmart/walmartexternal", updated.AtsIdentifier);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(updated.AtsIdentifier, (await fixture.Repository.GetByIdAsync(created.Id))!.AtsIdentifier);
    }

    [Fact]
    public async Task AdminAcceptsPublicRootIdentifierAndPersistsCanonicalSiteRoot()
    {
        using var fixture = new JobSourceFixture();
        var url = "https://spgi.wd5.myworkdayjobs.com/en-US/SPGI_Careers/";
        var created = await fixture.Service.CreateAsync(fixture.Request with
        {
            AtsType = AtsType.Workday,
            CareerPageUrl = url,
            AtsIdentifier = url
        });

        Assert.Equal("https://spgi.wd5.myworkdayjobs.com/en-US/SPGI_Careers", created.CareerPageUrl);
        Assert.Equal("spgi/SPGI_Careers", created.AtsIdentifier);
        var target = WorkdayJobSourceProvider.ValidateSource(new JobSource
        {
            AtsType = created.AtsType,
            CareerPageUrl = created.CareerPageUrl,
            AtsIdentifier = created.AtsIdentifier
        });
        Assert.Equal("spgi", target.Tenant);
        Assert.Equal("wd5", WorkdaySourceConfiguration.Parse(created.CareerPageUrl).Configuration!.Shard);
        Assert.Equal("SPGI_Careers", target.Site);
        Assert.Equal("https://spgi.wd5.myworkdayjobs.com/wday/cxs/spgi/SPGI_Careers/jobs",
            WorkdaySourceConfiguration.Parse(created.CareerPageUrl).Configuration!.ListingEndpoint.AbsoluteUri);
    }

    [Fact]
    public async Task InvalidSourceLogsSpecificSanitizedFailureWithoutCreatingHttpClient()
    {
        var logs = new Logs();
        var source = Source("ag.wd3.myworkdayjobs.com", "Airbus", "ag/Other-secret");
        using var provider = new WorkdayJobSourceProvider(new ForbiddenFactory(), logs);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchSnapshotAsync(source));
        Assert.Contains(logs.Messages, message => message.Contains("WORKDAY_CONFIGURATION_CONFLICT", StringComparison.Ordinal) &&
            message.Contains("ConfigurationValidation", StringComparison.Ordinal) && message.Contains("CareerSite=Airbus", StringComparison.Ordinal));
        Assert.All(logs.Messages, message => Assert.DoesNotContain("Other-secret", message));
    }

    [Fact]
    public async Task InvalidStoredWorkdaySourceIsRejectedBeforeRunIsQueued()
    {
        using var fixture = new JobSourceFixture();
        fixture.Source.AtsType = AtsType.Workday;
        fixture.Source.CareerPageUrl = "https://ag.wd3.myworkdayjobs.com/Airbus";
        fixture.Source.AtsIdentifier = "other/Airbus";
        await fixture.Context.SaveChangesAsync();
        var service = new JobSourceRunService(fixture.Repository, new JobSourceRunStore(fixture.Context), TimeProvider.System);
        var exception = await Assert.ThrowsAsync<BadRequestException>(() => service.EnqueueAsync(fixture.Source.Id, null, default));
        Assert.Equal("WORKDAY_CONFIGURATION_CONFLICT", exception.Code);
        Assert.Empty(fixture.Context.JobSourceRuns);
    }

    private static JobSource Source(string host, string site, string? identifier) => new()
    { AtsType = AtsType.Workday, CareerPageUrl = $"https://{host}/{site}", AtsIdentifier = identifier, Company = new() { Name = "Fixture Company" } };
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => client; }
    private sealed class ForbiddenFactory : IHttpClientFactory
    { public HttpClient CreateClient(string name) => throw new Xunit.Sdk.XunitException("Invalid configuration reached HTTP initialization."); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request); }
    private sealed class Logs : ILogger<WorkdayJobSourceProvider>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
