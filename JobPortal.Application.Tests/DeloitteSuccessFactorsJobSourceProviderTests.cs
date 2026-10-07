using System.Net;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Application.Services;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class DeloitteSuccessFactorsJobSourceProviderTests
{
    [Fact]
    public void ListingParserReadsRowsAndOffsetPagination()
    {
        var html = Listing(26, 26, 26, 25, Row("Engineer", "hyderabad/123"));
        var page = SuccessFactorsJobSourceProvider.ParseListingPage(html, 25, new Uri(Source().CareerPageUrl));
        Assert.Equal(26, page.TotalCount);
        Assert.Equal(25, page.LastOffset);
        Assert.Equal("Engineer", Assert.Single(page.Results).Title);
        Assert.Equal("https://southasiacareers.deloitte.com/job/hyderabad/123/", page.Results[0].DetailUrl.AbsoluteUri);
    }

    [Fact]
    public async Task FetchSnapshotParsesOfficialDetailAndPreservesOriginalJobUrl()
    {
        var source = Source();
        var factory = new FakeClientFactory(request => request.AbsolutePath.Contains("/job/", StringComparison.Ordinal)
            ? Detail("Systems Engineer") : Listing(1, 1, 1, 0, Row("Systems Engineer", "systems-engineer/123")));
        var provider = new SuccessFactorsJobSourceProvider(factory);

        var snapshot = await provider.FetchSnapshotAsync(source);

        var job = Assert.Single(snapshot.Jobs);
        Assert.True(snapshot.IsComplete);
        Assert.Equal(0, snapshot.Skipped);
        Assert.Equal("100536", job.ExternalId);
        Assert.Equal("https://southasiacareers.deloitte.com/job/systems-engineer/123/", job.ApplicationUrl);
        Assert.Equal("Hyderabad, India", job.Location);
        Assert.Equal(new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), job.SourcePostedAtUtc);
        Assert.Null(job.SalaryMin);
        Assert.Null(job.EmploymentType);
        Assert.Null(job.WorkplaceType);

        var normalized = new ExternalJobNormalizer().Normalize(job);
        Assert.Contains("Build reliable systems", normalized.Description);
        Assert.DoesNotContain("alert", normalized.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedDetailMakesSnapshotIncompleteAndCannotBeUsedForClosing()
    {
        var provider = new SuccessFactorsJobSourceProvider(new FakeClientFactory(request =>
            request.AbsolutePath.Contains("/job/", StringComparison.Ordinal)
                ? "<html>not a job page</html>"
                : Listing(1, 1, 1, 0, Row("Engineer", "engineer/123"))));

        var snapshot = await provider.FetchSnapshotAsync(Source());

        Assert.False(snapshot.IsComplete);
        Assert.Equal(1, snapshot.Skipped);
        Assert.Empty(snapshot.Jobs);
    }

    [Fact]
    public async Task ProviderRejectsSourceOutsideConfiguredOfficialBoard()
    {
        var provider = new SuccessFactorsJobSourceProvider(new FakeClientFactory(_ => string.Empty));
        var source = Source();
        source.CareerPageUrl = "https://example.test/careers";
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchSnapshotAsync(source));
    }

    [Fact]
    public void DetailUsesStablePostingIdWhenRequisitionIdIsAbsent()
    {
        var listing = new SuccessFactorsJobSourceProvider.ListingEntry("Engineer", "Pune, India",
            DateTimeOffset.Parse("2026-10-04T00:00:00Z"), new Uri("https://southasiacareers.deloitte.com/job/engineer/123/"));
        var html = Detail("Engineer").Replace("Job requisition ID : 100536", "", StringComparison.Ordinal);
        var parsed = SuccessFactorsJobSourceProvider.ParseDetail(html, listing, "Deloitte");
        Assert.Equal("123", parsed!.ExternalId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deloitte")]
    [InlineData("https://southasiacareers.deloitte.com/go/Deloitte-India/718244")]
    public async Task ProviderRequiresExactDeloitteBoardIdentifier(string? identifier)
    {
        var factory = new FakeClientFactory(_ => throw new InvalidOperationException("No HTTP call is expected."));
        var provider = new SuccessFactorsJobSourceProvider(factory);
        var source = Source();
        source.AtsIdentifier = identifier;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchSnapshotAsync(source));
        Assert.Equal("SuccessFactors source must use a public HTTPS /go/{board}/{numeric-id} URL matching its ATS identifier.", exception.Message);
    }

    [Fact]
    public async Task SourceHttpFailurePropagatesAndDoesNotProduceACompleteSnapshot()
    {
        var provider = new SuccessFactorsJobSourceProvider(new FaultClientFactory((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchSnapshotAsync(Source()));
    }

    [Fact]
    public async Task CancellationDuringSourceRequestPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new SuccessFactorsJobSourceProvider(new FaultClientFactory(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var fetch = provider.FetchSnapshotAsync(Source(), cancellation.Token);
        await Task.Delay(20);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
    }

    private static JobSource Source() => new()
    {
        AtsType = AtsType.SuccessFactors,
        AtsIdentifier = "718244",
        CareerPageUrl = "https://southasiacareers.deloitte.com/go/Deloitte-India/718244",
        Company = new Company { Name = "Deloitte" }
    };

    private static string Listing(int first, int last, int total, int lastOffset, params string[] rows) =>
        $"<div>Results {first} to {last} of {total}</div><table>{string.Join("", rows)}</table>" +
        $"<a class=\"paginationItemLast\" href=\"/go/Deloitte-India/718244/{lastOffset}/\">Last</a>";

    private static string Row(string title, string route) =>
        $"<tr class=\"data-row\"><td><a class=\"jobTitle-link\" href=\"/job/{route}/\">{title}</a></td>" +
        "<td><span class=\"jobLocation\">Hyderabad, IN</span></td><td><span class=\"jobDate\">Oct 4, 2026</span></td></tr>";

    private static string Detail(string title) =>
        $"<html><span itemprop=\"title\">{title}</span>" +
        "<span itemprop=\"description\"><p>Build reliable systems.</p><script>alert('x')</script> Job requisition ID : 100536</span>" +
        "<meta itemprop=\"datePosted\" content=\"2026-10-04T00:00:00Z\"><meta itemprop=\"validThrough\" content=\"2026-11-04T00:00:00Z\">" +
        "<meta itemprop=\"streetAddress\" content=\"Hyderabad, IN\"></html>";

    private sealed class FakeClientFactory(Func<Uri, string> response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler(response), disposeHandler: true);
    }

    private sealed class FakeHandler(Func<Uri, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(request.RequestUri!))
            });
        }
    }

    private sealed class FaultClientFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FaultHandler(send), disposeHandler: true);
    }

    private sealed class FaultHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
