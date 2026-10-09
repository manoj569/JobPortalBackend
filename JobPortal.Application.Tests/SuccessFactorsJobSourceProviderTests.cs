using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class SuccessFactorsJobSourceProviderTests
{
    private const string BoardUrl = "https://careers.example.com/go/Example-Careers/123456";

    [Fact]
    public async Task GenericProviderWithoutApprovalsIsIdempotentAndIncompleteScanDoesNotCloseOwnedJob()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.SuccessFactors;
        f.Source.AtsIdentifier = "123456";
        f.Source.CareerPageUrl = BoardUrl;
        f.Map(f.Category.Id.ToString());
        await f.Context.SaveChangesAsync();
        var corruptDetail = false;
        var provider = Provider((uri, _) => Task.FromResult(uri.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal)
            ? corruptDetail ? "<html>Access restricted</html>" : Detail(null)
            : Listing(1, 1, 1, 0, Row(55)).Replace("London, UK", "Pune, India", StringComparison.Ordinal)));
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonicalizer), fingerprints, unit, TimeProvider.System, f.Locks, canonicalizer);
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit, TimeProvider.System, f.Resolver,
            new ExternalJobNormalizer(), jobRepository: jobs,
            publicationPolicy: new JobPortal.Application.Features.JobAggregation.JobSourcePublicationPolicy(
                Microsoft.Extensions.Options.Options.Create(new JobPortal.Application.Features.JobAggregation.JobAggregationOptions()),
                TimeProvider.System));

        Assert.Equal(1, (await runner.RunAsync(f.Source.Id)).Created);
        var second = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Unchanged);
        var job = Assert.Single(f.Context.Jobs);
        Assert.Equal(f.Company.Id, job.CompanyId);
        Assert.Equal(f.Source.Id, job.JobSourceId);
        Assert.Equal("55", job.ExternalJobId);
        Assert.Equal(f.Category.Id, job.CategoryId);

        corruptDetail = true;
        var incomplete = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, incomplete.Closed);
        Assert.Equal(1, incomplete.Skipped);
        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Single(f.Context.Jobs);
    }

    [Fact]
    public async Task SecondCompanyUsesConfiguredOriginBoardAndCompanyWithoutDeloitteDefaults()
    {
        var requests = new ConcurrentBag<Uri>();
        var provider = Provider((uri, _) =>
        {
            requests.Add(uri);
            return Task.FromResult(uri.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal)
                ? Detail("98765") : Listing(1, 1, 1, 0, Row(55)));
        });
        var source = Source();
        var snapshot = await provider.FetchSnapshotAsync(source);
        var job = Assert.Single(snapshot.Jobs);
        Assert.True(snapshot.IsComplete);
        Assert.Equal("Example Company", job.CompanyName);
        Assert.Equal("98765", job.ExternalId);
        Assert.Equal("https://careers.example.com/job/engineer/55/", job.ApplicationUrl);
        Assert.All(requests, uri => Assert.Equal("careers.example.com", uri.Host));
        Assert.Contains(requests, uri => uri.AbsolutePath == "/go/Example-Careers/123456/" &&
            uri.Query == "?q=&sortColumn=referencedate&sortDirection=desc");
        Assert.Equal(0, snapshot.Skipped);
    }

    [Fact]
    public async Task PaginationUsesConfiguredBoardAndStablePostingIds()
    {
        var requests = new ConcurrentBag<Uri>();
        var provider = Provider((uri, _) =>
        {
            requests.Add(uri);
            var html = uri.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal) ? Detail(null) :
                uri.AbsolutePath.EndsWith("/25/", StringComparison.Ordinal)
                    ? Listing(26, 26, 26, 25, Row(26))
                    : Listing(1, 25, 26, 25, string.Join("", Enumerable.Range(1, 25).Select(Row)));
            return Task.FromResult(html);
        });
        var snapshot = await provider.FetchSnapshotAsync(Source());
        Assert.True(snapshot.IsComplete);
        Assert.Equal(26, snapshot.Jobs.Count);
        Assert.Equal(26, snapshot.Jobs.Select(x => x.ExternalId).Distinct().Count());
        Assert.Contains(requests, uri => uri.AbsolutePath == "/go/Example-Careers/123456/25/");
        Assert.Equal("1", snapshot.Jobs.First().ExternalId);
        Assert.Equal("26", snapshot.Jobs.Last().ExternalId);
    }

    [Fact]
    public void RelativePaginationLinkResolvesWithinConfiguredBoard()
    {
        var html = Listing(26, 26, 26, 25, Row(26))
            .Replace("/go/Example-Careers/123456/25/", "25/", StringComparison.Ordinal);
        var page = SuccessFactorsJobSourceProvider.ParseListingPage(html, 25, new Uri(BoardUrl));
        Assert.Equal(25, page.LastOffset);
        Assert.Equal("https://careers.example.com/job/engineer/26/", Assert.Single(page.Results).DetailUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://careers.example.com/go/Example-Careers/123456", "123456")]
    [InlineData("https://careers.example.com/go/Example-Careers/123456", "999")]
    [InlineData("https://careers.example.com/go/Example-Careers/123456", null)]
    [InlineData("https://careers.example.com/go/Example-Careers/123456", "deloitte")]
    [InlineData("https://careers.example.com/go/Example-Careers/123456?url=http://localhost", "123456")]
    [InlineData("https://careers.example.com/go/Example-Careers/123456#fragment", "123456")]
    [InlineData("https://user:password@careers.example.com/go/Example-Careers/123456", "123456")]
    [InlineData("https://careers.example.com:8443/go/Example-Careers/123456", "123456")]
    [InlineData("https://127.0.0.1/go/Example-Careers/123456", "123456")]
    [InlineData("https://[::1]/go/Example-Careers/123456", "123456")]
    [InlineData("https://localhost/go/Example-Careers/123456", "123456")]
    [InlineData("https://internal.local/go/Example-Careers/123456", "123456")]
    [InlineData("https://careers.example.com/arbitrary/123456", "123456")]
    [InlineData("//careers.example.com/go/Example-Careers/123456", "123456")]
    public async Task InvalidSourceIsRejectedBeforeHttp(string url, string? identifier)
    {
        var source = Source();
        source.CareerPageUrl = url;
        source.AtsIdentifier = identifier;
        var calls = 0;
        var provider = Provider((_, _) => { calls++; return Task.FromResult(string.Empty); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchSnapshotAsync(source));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("https://other.example.com/job/engineer/55/")]
    [InlineData("https://careers.example.com:8443/job/engineer/55/")]
    [InlineData("https://user:password@careers.example.com/job/engineer/55/")]
    [InlineData("https://careers.example.com/apply/55/")]
    [InlineData("http://careers.example.com/job/engineer/55/")]
    public async Task UnsafeDetailLinkFailsClosedBeforeDetailFetch(string link)
    {
        var calls = 0;
        var provider = Provider((_, _) =>
        {
            calls++;
            return Task.FromResult(Listing(1, 1, 1, 0, Row(55).Replace("/job/engineer/55/", link, StringComparison.Ordinal)));
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(Source()));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("https://other.example.com/go/Example-Careers/123456/0/")]
    [InlineData("/go/Other-Board/999/0/")]
    [InlineData("/go/Example-Careers/123456/25/")]
    [InlineData("/go/Example-Careers/123456/0/extra")]
    public async Task WrongBoardOrOffsetPaginationCannotProduceCompleteSnapshot(string lastLink)
    {
        var provider = Provider((_, _) => Task.FromResult(Listing(1, 1, 1, 0, Row(55))
            .Replace("/go/Example-Careers/123456/0/", lastLink, StringComparison.Ordinal)));
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(Source()));
    }

    [Fact]
    public async Task DuplicateExternalIdsAndUnparseableDetailsAreIncomplete()
    {
        var provider = Provider((uri, _) => Task.FromResult(uri.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal)
            ? Detail("99") : Listing(1, 2, 2, 0, Row(1) + Row(2))));
        Assert.False((await provider.FetchSnapshotAsync(Source())).IsComplete);

        provider = Provider((uri, _) => Task.FromResult(uri.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal)
            ? "<html>Access restricted</html>" : Listing(1, 1, 1, 0, Row(1))));
        var incomplete = await provider.FetchSnapshotAsync(Source());
        Assert.False(incomplete.IsComplete);
        Assert.Equal(1, incomplete.Skipped);
        Assert.Empty(incomplete.Jobs);
    }

    [Fact]
    public async Task DuplicateDetailLinksAreRejectedBeforeDetailsAreFetched()
    {
        var calls = 0;
        var provider = Provider((_, _) =>
        {
            calls++;
            return Task.FromResult(Listing(1, 2, 2, 0, Row(1) + Row(1)));
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(Source()));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DetailConcurrencyAndRequestSpacingRemainBounded()
    {
        var active = 0;
        var counts = new ConcurrentBag<int>();
        var starts = new ConcurrentBag<long>();
        var provider = Provider(async (uri, token) =>
        {
            if (!uri.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal))
                return Listing(1, 3, 3, 0, Row(1) + Row(2) + Row(3));
            starts.Add(Stopwatch.GetTimestamp());
            var count = Interlocked.Increment(ref active);
            counts.Add(count);
            try { await Task.Delay(400, token); return Detail(null); }
            finally { Interlocked.Decrement(ref active); }
        });
        Assert.True((await provider.FetchSnapshotAsync(Source())).IsComplete);
        Assert.InRange(counts.Max(), 1, 2);
        var ordered = starts.Order().ToArray();
        Assert.Equal(3, ordered.Length);
        Assert.True(Stopwatch.GetElapsedTime(ordered[0], ordered[1]) >= TimeSpan.FromMilliseconds(200));
        Assert.True(Stopwatch.GetElapsedTime(ordered[1], ordered[2]) >= TimeSpan.FromMilliseconds(200));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("224.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    public void PrivateReservedAndTransitionAddressesAreRejected(string address)
    {
        var ip = IPAddress.Parse(address);
        Assert.False(SuccessFactorsHttpTransport.IsPublicAddress(ip));
        Assert.Throws<HttpRequestException>(() => SuccessFactorsHttpTransport.ValidateAddresses([ip]));
    }

    [Fact]
    public void DnsSafetyRejectsEmptyAndMixedResultsAndTransportDoesNotRedirectOrProxy()
    {
        Assert.Throws<HttpRequestException>(() => SuccessFactorsHttpTransport.ValidateAddresses([]));
        Assert.Throws<HttpRequestException>(() => SuccessFactorsHttpTransport.ValidateAddresses(
            [IPAddress.Parse("8.8.8.8"), IPAddress.Loopback]));
        SuccessFactorsHttpTransport.ValidateAddresses([IPAddress.Parse("8.8.8.8"), IPAddress.Parse("2606:4700::1111")]);
        using var handler = SuccessFactorsHttpTransport.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    private static JobSource Source() => new()
    {
        AtsType = AtsType.SuccessFactors, AtsIdentifier = "123456", CareerPageUrl = BoardUrl,
        Company = new Company { Name = "Example Company" }
    };

    private static string Listing(int first, int last, int total, int lastOffset, string rows) =>
        $"<div>Results {first} to {last} of {total}</div><table>{rows}</table>" +
        $"<a class=\"paginationItemLast\" href=\"/go/Example-Careers/123456/{lastOffset}/\">Last</a>";

    private static string Row(int id) =>
        $"<tr class=\"data-row\"><td><a class=\"jobTitle-link\" href=\"/job/engineer/{id}/\">Engineer</a></td>" +
        "<td><span class=\"jobLocation\">London, UK</span></td><td><span class=\"jobDate\">Oct 4, 2026</span></td></tr>";

    private static string Detail(string? requisition) =>
        "<span itemprop=\"title\">Engineer</span><span itemprop=\"description\">Build reliable systems." +
        (requisition is null ? "" : $" Job requisition ID : {requisition}") + "</span>";

    private static SuccessFactorsJobSourceProvider Provider(Func<Uri, CancellationToken, Task<string>> response) =>
        new(new ClientFactory(response));

    private sealed class ClientFactory(Func<Uri, CancellationToken, Task<string>> response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(response), disposeHandler: true);
    }

    private sealed class Handler(Func<Uri, CancellationToken, Task<string>> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            new(HttpStatusCode.OK) { Content = new StringContent(await response(request.RequestUri!, cancellationToken)) };
    }
}
