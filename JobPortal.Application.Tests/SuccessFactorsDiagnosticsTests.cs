using System.Collections.Concurrent;
using System.Net;
using System.Text;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class SuccessFactorsDiagnosticsTests
{
    private const string Board = "https://careers.example.com/go/Example-Careers/123456";

    [Theory]
    [InlineData("metadata", "ListingPaginationMetadataInvalid")]
    [InlineData("last-link", "ListingLastPageLinkInvalid")]
    [InlineData("range", "ListingRangeInvalid")]
    [InlineData("row-count", "ListingRowCountMismatch")]
    [InlineData("job-link", "ListingRowMissingJobLink")]
    [InlineData("title", "ListingRowMissingTitle")]
    [InlineData("detail-url", "ListingRowInvalidDetailUrl")]
    [InlineData("date", "ListingRowInvalidDate")]
    public async Task Http200MalformedListingRetainsExceptionAndLogsSafeParseReason(string fault, string reason)
    {
        var html = Listing(0, 1, 0, Row(1));
        html = fault switch
        {
            "metadata" => "<html>Login or access restriction: PRIVATE_SENTINEL</html>",
            "last-link" => html.Replace("/go/Example-Careers/123456/0/", "https://other.example.com/go/Other/1/0/", StringComparison.Ordinal),
            "range" => html.Replace("Results 1 to 1", "Results 26 to 26", StringComparison.Ordinal),
            "row-count" => Listing(0, 2, 0, Row(1)),
            "job-link" => html.Replace("jobTitle-link", "unrecognized", StringComparison.Ordinal),
            "title" => html.Replace(">Engineer</a>", "></a>", StringComparison.Ordinal),
            "detail-url" => html.Replace("/job/engineer/1/", "https://other.example.com/job/engineer/1/", StringComparison.Ordinal),
            _ => html.Replace("Oct 4, 2026", "unparseable PRIVATE_SENTINEL", StringComparison.Ordinal)
        };
        var logs = new Logs();
        var calls = 0;
        var source = Source();
        var provider = Provider((_, _) => { calls++; return Task.FromResult(Ok(html)); }, logs);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(source));
        Assert.Equal(1, calls); // No details/ingestion or hidden retry after parse failure.
        AssertFailure(logs, source.Id, "listing_parse", 0, reason, 200);
    }

    [Theory]
    [InlineData(10001, 10000, "ListingRecordLimitExceeded")]
    [InlineData(26, 0, "ListingLastOffsetMismatch")]
    public async Task InitialRecordAndPaginationBoundsRemainFailClosed(int total, int last, string reason)
    {
        var source = Source();
        var logs = new Logs();
        var calls = 0;
        var provider = Provider((_, _) => { calls++; return Task.FromResult(Ok(Listing(0, total, last, Rows(0, 25)))); }, logs);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(source));
        Assert.Equal(1, calls);
        AssertFailure(logs, source.Id, "listing_pagination", 0, reason, 200);
    }

    [Theory]
    [InlineData("total", "ListingTotalChanged")]
    [InlineData("last-offset", "ListingLastOffsetChanged")]
    public async Task PaginationChangesReportTheFailingOffset(string change, string reason)
    {
        var source = Source();
        var logs = new Logs();
        var calls = 0;
        var provider = Provider((_, _) =>
        {
            calls++;
            return Task.FromResult(Ok(calls == 1 ? Listing(0, 50, 25, Rows(0, 25)) :
                Listing(25, change == "total" ? 51 : 50, change == "last-offset" ? 50 : 25, Rows(25, 25))));
        }, logs);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(source));
        Assert.Equal(2, calls);
        AssertFailure(logs, source.Id, "listing_pagination", 25, reason, 200);
    }

    [Fact]
    public async Task TotalDriftAtOffset825IsReproducedWithoutAnyLiveRequestsOrDetailFetches()
    {
        var source = Source();
        var logs = new Logs();
        var requests = new List<int>();
        var provider = Provider((uri, _) =>
        {
            Assert.StartsWith("/go/", uri.AbsolutePath);
            var tail = uri.AbsolutePath.TrimEnd('/').Split('/').Last();
            var offset = tail == "123456" ? 0 : int.Parse(tail, System.Globalization.CultureInfo.InvariantCulture);
            requests.Add(offset);
            return Task.FromResult(Ok(Listing(offset, offset == 825 ? 1492 : 1493, 1475, Rows(offset, 25))));
        }, logs);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(source));
        Assert.Equal(34, requests.Count);
        Assert.Equal(825, requests.Last());
        AssertFailure(logs, source.Id, "listing_pagination", 825, "ListingTotalChanged", 200);
    }

    [Fact]
    public async Task RepeatedPageAndDuplicateLinksCannotReturnACompleteSnapshot()
    {
        var source = Source();
        var logs = new Logs();
        var calls = 0;
        var provider = Provider((_, _) => { calls++; return Task.FromResult(Ok(Listing(0, 50, 25, Rows(0, 25)))); }, logs);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(source));
        Assert.Equal(2, calls);
        AssertFailure(logs, source.Id, "listing_parse", 25, "ListingRangeInvalid", 200);

        logs = new Logs(); calls = 0;
        provider = Provider((_, _) => { calls++; return Task.FromResult(Ok(Listing(0, 2, 0, Row(1) + Row(1)))); }, logs);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(source));
        Assert.Equal(1, calls);
        AssertFailure(logs, source.Id, "listing_snapshot", 0, "ListingDuplicateDetailLinks", 200);
    }

    [Theory]
    [InlineData(true, "ResponseDeclaredSizeExceeded")]
    [InlineData(false, "ResponseReadSizeExceeded")]
    public async Task BothResponseSizeLimitsRemainEnforced(bool declared, string reason)
    {
        var logs = new Logs();
        var source = Source();
        var provider = Provider((_, _) =>
        {
            HttpContent content = declared ? new StringContent("PRIVATE_SENTINEL") :
                new StreamContent(new UnknownLengthStream(new byte[4 * 1024 * 1024 + 1]));
            if (declared) content.Headers.ContentLength = 4 * 1024 * 1024 + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }, logs);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(source));
        AssertFailure(logs, source.Id, "listing_response", 0, reason, 200);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownInvalidDataDoesNotLeakExceptionTextOrInventResponseStatus(bool headersReceived)
    {
        var source = Source();
        var logs = new Logs();
        var exception = new InvalidDataException("PRIVATE_SENTINEL credentials HTML document");
        // Even a malicious/custom exception Data entry cannot become a log reason.
        exception.Data["SuccessFactorsFailureReason"] = "PRIVATE_SENTINEL";
        var provider = Provider((_, _) => headersReceived
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream(exception)) })
            : Task.FromException<HttpResponseMessage>(exception), logs);
        Assert.Same(exception, await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(source)));
        AssertFailure(logs, source.Id, "listing_response", 0, "ResponseReadInvalidData", headersReceived ? 200 : null);
    }

    [Fact]
    public async Task DetailResponseFailureIncludesOriginatingListingOffset()
    {
        var source = Source();
        var logs = new Logs();
        var provider = Provider((uri, _) =>
        {
            var response = Ok(uri.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal) ? "PRIVATE_SENTINEL" : Listing(0, 1, 0, Row(1)));
            if (uri.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal)) response.Content.Headers.ContentLength = 4 * 1024 * 1024 + 1;
            return Task.FromResult(response);
        }, logs);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(source));
        AssertFailure(logs, source.Id, "detail_response", 0, "ResponseDeclaredSizeExceeded", 200);
    }

    [Fact]
    public async Task HttpFailureStillThrowsWithSafeStatusAndOffsetDiagnostics()
    {
        var source = Source();
        var logs = new Logs();
        var provider = Provider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            { ReasonPhrase = "PRIVATE_SENTINEL", Content = new StringContent("PRIVATE_SENTINEL") }), logs);
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchSnapshotAsync(source));
        AssertFailure(logs, source.Id, "listing_response", 0, "HttpStatusFailure", 503);
    }

    [Fact]
    public async Task ProviderInvalidDataLeavesExistingJobsAndSuccessUntouchedAndRespectsCooldown()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.SuccessFactors;
        f.Source.AtsIdentifier = "123456";
        f.Source.CareerPageUrl = Board;
        var previous = JobSourceFixture.Now.AddDays(-2);
        f.Source.LastSuccessfulRunAtUtc = previous;
        f.Context.AddRange(new Job { Title = "Imported", CompanyId = f.Company.Id, CategoryId = f.Category.Id,
            JobSourceId = f.Source.Id, ExternalJobId = "existing", Status = JobStatus.Published },
            new Job { Title = "Manual", CompanyId = f.Company.Id, CategoryId = f.Category.Id, Status = JobStatus.Published });
        await f.Context.SaveChangesAsync();
        var provider = Provider((_, _) => Task.FromResult(Ok("PRIVATE_SENTINEL not a listing")), new Logs());
        var runner = new JobSourceRunner(f.Repository, [provider], new NeverIngest(), new UnitOfWork(f.Context),
            new Clock(), f.Resolver, new ExternalJobNormalizer(), jobRepository: new JobRepository(f.Context));
        Assert.False((await f.CreateService(runner).RunAsync(f.Source.Id)).Succeeded);
        Assert.All(await f.Context.Jobs.AsNoTracking().ToArrayAsync(), job => Assert.Equal(JobStatus.Published, job.Status));
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
        var saved = await f.Context.JobSources.AsNoTracking().SingleAsync();
        Assert.Equal(previous, saved.LastSuccessfulRunAtUtc);
        Assert.Equal(JobSourceFixture.Now, saved.LastRunAtUtc);
        Assert.Equal(1, saved.ConsecutiveFailures);
        Assert.Empty(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now.AddMinutes(1), 25));
    }

    private static void AssertFailure(Logs logs, Guid source, string stage, int offset, string reason, int? status)
    {
        var failure = Assert.Single(logs.Failures);
        Assert.Equal(source, failure["JobSourceId"]);
        Assert.Equal(stage, failure["Stage"]);
        Assert.Equal(offset, failure["PaginationOffset"]);
        Assert.Equal(reason, failure["ReasonCode"]);
        Assert.Equal(status, failure["StatusCode"]);
        Assert.DoesNotContain(logs.Messages, message => message.Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
        Assert.All(logs.Exceptions, Assert.Null);
    }
    private static JobSource Source() => new() { AtsType = AtsType.SuccessFactors, AtsIdentifier = "123456", CareerPageUrl = Board,
        Company = new Company { Name = "Example" } };
    private static string Listing(int offset, int total, int lastOffset, string rows) =>
        $"<div>Results {offset + 1} to {Math.Min(offset + 25, total)} of {total}</div><table>{rows}</table>" +
        $"<a class=\"paginationItemLast\" href=\"/go/Example-Careers/123456/{lastOffset}/\">Last</a>";
    private static string Rows(int offset, int count) => string.Concat(Enumerable.Range(offset + 1, count).Select(Row));
    private static string Row(int id) => $"<tr class=\"data-row\"><td><a class=\"jobTitle-link\" href=\"/job/engineer/{id}/\">Engineer</a>" +
        "<span class=\"jobLocation\">London</span><span class=\"jobDate\">Oct 4, 2026</span></td></tr>";
    private static HttpResponseMessage Ok(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };
    private static SuccessFactorsJobSourceProvider Provider(Func<Uri, CancellationToken, Task<HttpResponseMessage>> response, Logs logs) => new(new Factory(response), logs);
    private sealed class Factory(Func<Uri, CancellationToken, Task<HttpResponseMessage>> response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(response));
    }
    private sealed class Handler(Func<Uri, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request.RequestUri!, cancellationToken);
    }
    private sealed class Logs : ILogger<SuccessFactorsJobSourceProvider>
    {
        public ConcurrentBag<Dictionary<string, object?>> Failures { get; } = [];
        public ConcurrentBag<string> Messages { get; } = [];
        public ConcurrentBag<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception)); Exceptions.Add(exception);
            if (eventId.Id == 4360) Failures.Add(((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(x => x.Key, x => x.Value));
        }
    }
    private sealed class BrokenStream(InvalidDataException exception) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(exception);
    }
    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
    private sealed class NeverIngest : IJobIngestionService
    {
        public Task<JobIngestionResult> IngestAsync(RawExternalJob rawJob, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Ingestion must not start after provider failure.");
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(JobSourceFixture.Now);
    }
}
