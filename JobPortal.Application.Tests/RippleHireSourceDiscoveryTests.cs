using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using JobPortal.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Application.Services;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Persistence;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class RippleHireSourceDiscoveryTests
{
    private const string PublicUrl = "https://ltimindtree.ripplehire.com/candidate/?token=public-test-token&lang=en&source=CAREERSITE";
    private static string Fixture(string name) => System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RippleHire", name)), @"(?<=token=)[A-Za-z0-9_-]+", "public-test-token", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };
    private static HttpResponseMessage Listing() => new(HttpStatusCode.OK) { Content = new StringContent(Fixture("listing.json"), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task FullPublicUrlIsVerifiedWithoutFetchingHtml()
    {
        var factory = new Factory((name, request) => { Assert.Equal(RippleHireJobSourceProvider.HttpClientName, name); Assert.Equal(HttpMethod.Post, request.Method); return Listing(); });
        var result = await new RippleHireSourceDiscovery(factory).DiscoverAsync(PublicUrl, null);
        Assert.True(result.Succeeded);
        Assert.Equal("ManualUrlVerified", result.Status);
        Assert.False(result.ManualConfigurationRequired);
        Assert.True(RippleHireSourceConfiguration.Resolve(result.ResolvedCareerPageUrl, null).IsValid);
        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public async Task EmployerLinksFromVerifiedPublicEvidenceResolveAndVerify()
    {
        var factory = new Factory((name, _) => name == RippleHireSourceDiscovery.HttpClientName ? Html(Fixture("employer-careers.html")) : Listing());
        var result = await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://www.ltm.com/india-careers", null);
        Assert.True(result.Succeeded);
        Assert.Equal("Resolved", result.Status);
        Assert.Contains("token=public-test-token", result.ResolvedCareerPageUrl);
        Assert.DoesNotContain("#", result.ResolvedCareerPageUrl);
        Assert.Equal(2, factory.Count);
    }

    [Fact]
    public async Task TenantInputUsesPublicEntryRouteAndRequiresExposedLink()
    {
        var factory = new Factory((name, request) =>
        {
            if (name != RippleHireSourceDiscovery.HttpClientName) return Listing();
            Assert.Equal("/candidate/", request.RequestUri!.AbsolutePath);
            return Html(Fixture("employer-careers.html"));
        });
        Assert.True((await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://ltimindtree.ripplehire.com/", null)).Succeeded);
    }

    [Theory]
    [InlineData("<input type='hidden' id='shareToken' value='internal-only'>")]
    [InlineData("<p>No public careers here</p>")]
    [InlineData("<!-- <a href='https://ltimindtree.ripplehire.com/candidate/?token=abc'>fake</a> -->")]
    [InlineData("<script>var text=\"<a href='https://ltimindtree.ripplehire.com/candidate/?token=abc'>fake</a>\";</script>")]
    public async Task MissingPublicEvidenceRequiresManualConfiguration(string html)
    {
        var factory = new Factory((_, _) => Html(html));
        var result = await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://example.com/careers", null);
        Assert.False(result.Succeeded);
        Assert.True(result.ManualConfigurationRequired);
        Assert.Equal("RIPPLEHIRE_PUBLIC_TOKEN_NOT_FOUND", result.ReasonCode);
        Assert.Single(result.ValidationErrors);
        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public async Task DifferentPublicTokensAreAmbiguousBeforeVerification()
    {
        var factory = new Factory((_, _) => Html(Fixture("employer-careers.html") + "<iframe src='" + PublicUrl.Replace("public-test-token", "different") + "'></iframe>"));
        var result = await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://example.com/careers", null);
        Assert.Equal("Ambiguous", result.Status);
        Assert.Null(result.ResolvedCareerPageUrl);
        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public async Task EquivalentFilteredLinksDoNotCreateAmbiguity()
    {
        var html = Fixture("employer-careers.html");
        var factory = new Factory((name, _) => name == RippleHireSourceDiscovery.HttpClientName ? Html(html + html.Replace("geo=India", "search=Cloud")) : Listing());
        Assert.True((await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://example.com/careers", null)).Succeeded);
    }

    [Fact]
    public async Task RelativePublicLinksAndMetadataAreSupported()
    {
        var html = "<link rel='canonical' href='/candidate/?token=exposed'><meta content='/candidate/?token=exposed' property='og:url'>";
        var factory = new Factory((name, _) => name == RippleHireSourceDiscovery.HttpClientName ? Html(html) : Listing());
        Assert.True((await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://ltimindtree.ripplehire.com/candidate/", null)).Succeeded);
    }

    [Theory]
    [InlineData("http://example.com/careers")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("https://metadata.local/")]
    [InlineData("https://example.com:8443/")]
    public async Task UnsafeInputNeverMakesRequests(string input)
    {
        var factory = new Factory((_, _) => throw new InvalidOperationException());
        var result = await new RippleHireSourceDiscovery(factory).DiscoverAsync(input, null);
        Assert.False(result.Succeeded);
        Assert.Equal(0, factory.Count);
    }

    [Fact]
    public async Task SafeRelativeRedirectIsFollowed()
    {
        var factory = new Factory((name, request) => name != RippleHireSourceDiscovery.HttpClientName ? Listing() :
            request.RequestUri!.AbsolutePath == "/careers" ? Redirect("/public-careers") : Html(Fixture("employer-careers.html")));
        Assert.True((await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://example.com/careers", null)).Succeeded);
        Assert.Equal(3, factory.Count);
    }

    [Theory]
    [InlineData("https://127.0.0.1/private")]
    [InlineData("http://example.com/careers")]
    [InlineData("/auth/saml2")]
    [InlineData("/careers")]
    public async Task UnsafeAuthAndLoopRedirectsAreRejected(string target)
    {
        var factory = new Factory((_, _) => Redirect(target));
        Assert.False((await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://example.com/careers", null)).Succeeded);
        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public async Task HtmlDoesNotBecomeValidUntilProviderVerifiesListing()
    {
        var factory = new Factory((name, _) => name == RippleHireSourceDiscovery.HttpClientName ? Html(Fixture("employer-careers.html")) : new(HttpStatusCode.Forbidden));
        var result = await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://example.com/careers", null);
        Assert.False(result.Succeeded);
        Assert.Null(result.ResolvedCareerPageUrl);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RippleHireSourceDiscovery(new Factory((_, _) => Listing())).DiscoverAsync(PublicUrl, null, cancelled.Token));
    }

    [Fact]
    public async Task ConnectionTestReturnsStructuredDiscoveryResult()
    {
        var factory = new Factory((name, _) => name == RippleHireSourceDiscovery.HttpClientName ? Html(Fixture("employer-careers.html")) : Listing());
        var result = await new JobSourceConnectionTester(factory, new RippleHireSourceDiscovery(factory)).TestAsync(new() { AtsType = AtsType.RippleHire, CareerPageUrl = "https://www.ltm.com/india-careers" });
        Assert.True(result.ListingReachable);
        Assert.Equal("Resolved", result.Discovery!.Status);
    }

    private static HttpResponseMessage Redirect(string target) => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(target, UriKind.RelativeOrAbsolute) } };
    [Fact]
    public async Task EditPersistsVerifiedManualUrlAndConflictingIdentifierCannotOverwriteIt()
    {
        using var fixture = new JobSourceFixture();
        var factory = new Factory((_, _) => Listing());
        var service = fixture.CreateService(discovery: new RippleHireSourceDiscovery(factory));
        var updated = await service.UpdateAsync(fixture.Source.Id, new(fixture.Company.Id, PublicUrl, AtsType.RippleHire, null));
        Assert.Equal("ManualUrlVerified", updated.Discovery!.Status);
        Assert.Equal("public-test-token", fixture.Source.AtsIdentifier);
        await Assert.ThrowsAsync<RippleHireDiscoveryException>(() => service.UpdateAsync(fixture.Source.Id,
            new(fixture.Company.Id, PublicUrl, AtsType.RippleHire, "conflicting")));
        Assert.Equal(updated.CareerPageUrl, fixture.Source.CareerPageUrl);
        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public async Task DiscoveredUnsafeUrlsCannotBeFetched()
    {
        var factory = new Factory((_, _) => Html("<iframe src='https://127.0.0.1/candidate/?token=secret'></iframe><a href='https://fake.ripplehire.com.evil.example/candidate/?token=abc'>Jobs</a>"));
        Assert.False((await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://example.com/careers", null)).Succeeded);
        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public async Task RedirectChainHasFixedRequestLimit()
    {
        var redirectNumber = 0;
        var factory = new Factory((_, _) => Redirect("/next/" + ++redirectNumber));
        Assert.False((await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://example.com/careers", null)).Succeeded);
        Assert.Equal(5, factory.Count);
    }

    [Fact]
    public async Task OversizedResponseIsRejected()
    {
        var factory = new Factory((_, _) => Html(new string('x', 1024 * 1024 + 1)));
        Assert.False((await new RippleHireSourceDiscovery(factory).DiscoverAsync("https://example.com/careers", null)).Succeeded);
    }
    [Fact]
    public async Task CreatePersistsResolutionAndSyncDoesNotRediscover()
    {
        using var fixture = new JobSourceFixture();
        var discoveryRequests = 0;
        var factory = new Factory((name, request) =>
        {
            if (name == RippleHireSourceDiscovery.HttpClientName) { discoveryRequests++; return Html(Fixture("employer-careers.html")); }
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent(Fixture("detail.json"), Encoding.UTF8, "application/json") };
            var listing = JsonNode.Parse(Fixture("listing.json"))!;
            listing["totalJobCount"] = 1;
            var entries = listing["jobVoList"]!.AsArray();
            while (entries.Count > 1) entries.RemoveAt(entries.Count - 1);
            return new(HttpStatusCode.OK) { Content = new StringContent(listing.ToJsonString(), Encoding.UTF8, "application/json") };
        });
        var service = fixture.CreateService(discovery: new RippleHireSourceDiscovery(factory));
        var created = await service.CreateAsync(new(fixture.Company.Id, "https://www.ltm.com/india-careers", AtsType.RippleHire, null));
        Assert.Equal("Resolved", created.Discovery!.Status);
        Assert.Equal("public-test-token", created.AtsIdentifier);
        var persisted = await fixture.Repository.GetByIdAsync(created.Id);
        Assert.Equal(created.CareerPageUrl, persisted!.CareerPageUrl);
        var jobs = new JobRepository(fixture.Context);
        var fingerprints = new JobFingerprintService();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(fixture.Context), new CategoryManagementRepository(fixture.Context),
            new JobDeduplicationService(jobs, fingerprints, new UrlCanonicalizer()), fingerprints, new UnitOfWork(fixture.Context), TimeProvider.System, fixture.Locks, new UrlCanonicalizer());
        var runner = new JobSourceRunner(fixture.Repository, [new RippleHireJobSourceProvider(factory)], ingestion,
            new UnitOfWork(fixture.Context), TimeProvider.System, fixture.Resolver, new ExternalJobNormalizer());
        var first = await runner.RunAsync(created.Id);
        Assert.True(first.Succeeded);
        var second = await runner.RunAsync(created.Id);
        Assert.True(second.Succeeded);
        Assert.Equal(1, discoveryRequests);
        Assert.Single(await fixture.Context.Jobs.Where(job => job.JobSourceId == created.Id).ToArrayAsync());
    }

    [Fact]
    public async Task DiscoveryFailureDoesNotPersistAnUnsupportedSource()
    {
        using var fixture = new JobSourceFixture();
        var factory = new Factory((_, _) => Html("<p>No public source</p>"));
        var service = fixture.CreateService(discovery: new RippleHireSourceDiscovery(factory));
        var error = await Assert.ThrowsAsync<RippleHireDiscoveryException>(() => service.CreateAsync(new(fixture.Company.Id, "https://example.com/careers", AtsType.RippleHire, null)));
        Assert.True(error.Discovery.ManualConfigurationRequired);
        Assert.DoesNotContain(await fixture.Context.JobSources.ToArrayAsync(), source => source.AtsType == AtsType.RippleHire);
    }

    [Fact]
    public void DiscoveryTransportRejectsPrivateDnsAndDisablesAutomaticRedirects()
    {
        Assert.Throws<HttpRequestException>(() => SuccessFactorsHttpTransport.ValidateAddresses([IPAddress.Parse("10.0.0.1")]));
        Assert.Throws<HttpRequestException>(() => SuccessFactorsHttpTransport.ValidateAddresses([IPAddress.Parse("8.8.8.8"), IPAddress.Loopback]));
        using var transport = SuccessFactorsHttpTransport.CreateHandler();
        Assert.False(transport.AllowAutoRedirect);
        Assert.False(transport.UseCookies);
        Assert.False(transport.UseProxy);
        Assert.NotNull(transport.ConnectCallback);
    }
    [Fact]
    public async Task TestedEmployerUrlCanBeSavedWithMatchingDiscoveredIdentifier()
    {
        using var fixture = new JobSourceFixture();
        var factory = new Factory((name, _) => name == RippleHireSourceDiscovery.HttpClientName ? Html(Fixture("employer-careers.html")) : Listing());
        var discovery = new RippleHireSourceDiscovery(factory);
        const string employerUrl = "https://www.ltm.com/india-careers";
        var tested = await new JobSourceConnectionTester(factory, discovery).TestAsync(new() { AtsType = AtsType.RippleHire, CareerPageUrl = employerUrl });
        Assert.True(tested.ListingReachable);
        var identifier = RippleHireSourceConfiguration.Resolve(tested.Discovery!.ResolvedCareerPageUrl, null).Identifier;
        var saved = await fixture.CreateService(discovery: discovery).CreateAsync(new(fixture.Company.Id, employerUrl, AtsType.RippleHire, identifier));
        Assert.Equal(tested.Discovery.ResolvedCareerPageUrl, saved.CareerPageUrl);
        Assert.Equal(identifier, saved.AtsIdentifier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("public-test-token")]
    public async Task ResolvedTestConfigurationSavesWithOptionalIdentifier(string? identifier)
    {
        using var fixture = new JobSourceFixture();
        var factory = new Factory((_, _) => Listing());
        var discovery = new RippleHireSourceDiscovery(factory);
        var tested = await new JobSourceConnectionTester(factory, discovery).TestAsync(new() { AtsType = AtsType.RippleHire, CareerPageUrl = PublicUrl, AtsIdentifier = identifier });
        Assert.True(tested.ListingReachable);
        var service = fixture.CreateService(discovery: discovery);
        var saved = await service.CreateAsync(new(fixture.Company.Id, tested.Discovery!.ResolvedCareerPageUrl!, AtsType.RippleHire, identifier));
        Assert.Equal(fixture.Company.Id, saved.CompanyId);
        Assert.Equal("public-test-token", saved.AtsIdentifier);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(new(fixture.Company.Id, PublicUrl, AtsType.RippleHire, identifier)));
        Assert.Equal(409, duplicate.StatusCode);
        Assert.Equal("duplicate_job_source", duplicate.Code);
    }

    [Fact]
    public async Task EmployerUrlWithConflictingIdentifierStillCannotBeSaved()
    {
        using var fixture = new JobSourceFixture();
        var factory = new Factory((name, _) => name == RippleHireSourceDiscovery.HttpClientName ? Html(Fixture("employer-careers.html")) : Listing());
        var service = fixture.CreateService(discovery: new RippleHireSourceDiscovery(factory));
        var error = await Assert.ThrowsAsync<RippleHireDiscoveryException>(() => service.CreateAsync(new(fixture.Company.Id, "https://www.ltm.com/india-careers", AtsType.RippleHire, "mismatched")));
        Assert.Equal("RIPPLEHIRE_CONFIGURATION_CONFLICT", error.Code);
        Assert.Equal(1, factory.Count);
        Assert.DoesNotContain(await fixture.Context.JobSources.ToArrayAsync(), source => source.AtsType == AtsType.RippleHire);
    }

    [Fact]
    public async Task SuccessfulConnectionDoesNotBypassCompanyOrScheduleValidation()
    {
        using var fixture = new JobSourceFixture();
        var factory = new Factory((_, _) => Listing());
        var discovery = new RippleHireSourceDiscovery(factory);
        Assert.True((await new JobSourceConnectionTester(factory, discovery).TestAsync(new() { AtsType = AtsType.RippleHire, CareerPageUrl = PublicUrl })).ListingReachable);
        var service = fixture.CreateService(discovery: discovery);
        var missing = await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(new(Guid.NewGuid(), PublicUrl, AtsType.RippleHire, null)));
        Assert.Equal("invalid_company", missing.Code);
        var invalid = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => service.CreateAsync(new(fixture.Company.Id, PublicUrl, AtsType.RippleHire, null, ScanIntervalMinutes: 0)));
        Assert.Contains(invalid.Errors, error => error.ErrorCode == "SCAN_INTERVAL_INVALID");
        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public async Task DiscoveredIdentifierDoesNotSubstituteForPublicEvidenceOrVerification()
    {
        using var fixture = new JobSourceFixture();
        foreach (var exposeLink in new[] { false, true })
        {
            var factory = new Factory((name, _) => name == RippleHireSourceDiscovery.HttpClientName
                ? Html(exposeLink ? Fixture("employer-careers.html") : "<p>No public link</p>") : new(HttpStatusCode.Forbidden));
            var service = fixture.CreateService(discovery: new RippleHireSourceDiscovery(factory));
            var error = await Assert.ThrowsAsync<RippleHireDiscoveryException>(() => service.CreateAsync(new(fixture.Company.Id, "https://www.ltm.com/india-careers", AtsType.RippleHire, "public-test-token")));
            Assert.Equal(exposeLink ? "RIPPLEHIRE_DISCOVERY_VERIFICATION_FAILED" : "RIPPLEHIRE_PUBLIC_TOKEN_NOT_FOUND", error.Code);
        }
        Assert.DoesNotContain(await fixture.Context.JobSources.ToArrayAsync(), source => source.AtsType == AtsType.RippleHire);
    }

    internal sealed class Factory(Func<string, HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public int Count { get; private set; }
        public HttpClient CreateClient(string name) => new(new Handler(request => { Count++; return respond(name, request); }));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}

public sealed class JobSourceRequestErrorsTests
{
    [Fact]
    public async Task ValidationResponseAndLogsIncludeFieldsCodesAndTraceWithoutLoggingValues()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-test" };
        context.Request.Path = "/api/admin/job-sources";
        context.Request.Method = "POST";
        context.Response.Body = new MemoryStream();
        var logger = new CaptureLogger();
        var failure = new ValidationFailure("AtsIdentifier", "Identifier is too long.", "private-career-token") { ErrorCode = "ATS_IDENTIFIER_TOO_LONG" };
        await new GlobalExceptionMiddleware(_ => throw new ValidationException([failure]), logger).InvokeAsync(context);
        Assert.Equal(400, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("ATS_IDENTIFIER_TOO_LONG", body.RootElement.GetProperty("reasonCodes").GetProperty("AtsIdentifier")[0].GetString());
        Assert.Equal("trace-test", body.RootElement.GetProperty("traceId").GetString());
        Assert.Contains("AtsIdentifier", logger.Text);
        Assert.Contains("ATS_IDENTIFIER_TOO_LONG", logger.Text);
        Assert.DoesNotContain("private-career-token", logger.Text);
    }
    [Fact]
    public void BindingFailureUsesSameEnvelopeAndDoesNotEchoRejectedJson()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "binding-trace" };
        var action = new ActionContext(context, new RouteData(), new ActionDescriptor());
        action.ModelState.AddModelError("$.atsType", "invalid token-bearing input private-career-token");
        var result = JobSourceRequestErrors.BindingFailure(action);
        var error = Assert.IsType<JobSourceRequestError>(result.Value);
        Assert.Equal("validation_error", error.Code);
        Assert.Contains("AtsType", error.Errors.Keys);
        Assert.DoesNotContain("private-career-token", JsonSerializer.Serialize(error));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompanyAndDiscoveryFailuresHaveActionableFields(bool company)
    {
        Exception exception = company ? new BadRequestException("Company must reference an existing company.", "invalid_company") :
            new RippleHireDiscoveryException(new("NotFound", null, true, "RIPPLEHIRE_PUBLIC_TOKEN_NOT_FOUND", ["Provide a complete public career URL."]));
        var error = JobSourceRequestErrors.FromException(exception, new DefaultHttpContext());
        Assert.Contains(company ? "CompanyId" : "CareerPageUrl", error.Errors.Keys);
        if (!company) Assert.True(error.Discovery!.ManualConfigurationRequired);
    }
    private sealed class CaptureLogger : ILogger<GlobalExceptionMiddleware>
    {
        public string Text { get; private set; } = "";
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Text += formatter(state, exception);
    }
}
