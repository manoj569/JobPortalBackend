using System.Net;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ExternalJobExpiryTests
{
    [Fact]
    public async Task Greenhouse_DeadlineSurvivesRunnerPersistenceWithoutRefreshingDuplicateExpiry()
    {
        using var fixture = new JobSourceFixture();
        using var handler = new DeadlineHandler("""{"id":123,"application_deadline":"2026-12-01T17:30:00+05:30"}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        fixture.Map(fixture.Category.Id.ToString());
        fixture.Provider.Jobs = await new GreenhouseExternalJobProvider(new Factory(client)).FetchJobsAsync(fixture.Source);
        var result = await fixture.Runner.RunAsync(fixture.Source.Id);
        Assert.Equal(1, result.Created);
        fixture.Context.ChangeTracker.Clear();
        var job = Assert.Single(fixture.Context.Jobs);
        var expected = new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, job.ExpiresAtUtc);
        Assert.Equal(JobStatus.Draft, job.Status);

        fixture.Provider.Jobs = [fixture.Provider.Jobs.Single() with { ExpiresAtUtc = expected.AddDays(30) }];
        result = await fixture.Runner.RunAsync(fixture.Source.Id);
        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Created);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(expected, Assert.Single(fixture.Context.Jobs).ExpiresAtUtc);
    }

    [Theory]
    [InlineData("\"2026-12-01T12:00:00Z\"", "2026-12-01T12:00:00Z")]
    [InlineData("\"2026-12-01T17:30:00+05:30\"", "2026-12-01T12:00:00Z")]
    [InlineData("\"2026-12-01T07:00:00-05:00\"", "2026-12-01T12:00:00Z")]
    [InlineData("\"2020-01-01T00:00:00Z\"", "2020-01-01T00:00:00Z")]
    [InlineData("null", null)]
    [InlineData("42", null)]
    [InlineData("{}", null)]
    [InlineData("\"\"", null)]
    [InlineData("\"invalidZ\"", null)]
    [InlineData("\"2026-12-01\"", null)]
    [InlineData("\"2026-12-01T12:00:00\"", null)]
    [InlineData("\"2026-12-01T12:00:00+25:00\"", null)]
    public async Task Greenhouse_OnlyExplicitSourceInstantsBecomeUtc(string deadlineJson, string? expected)
    {
        using var handler = new DeadlineHandler("{\"id\":123,\"application_deadline\":" + deadlineJson + "}");
        var job = Assert.Single(await FetchAsync(handler));
        Assert.Equal(expected is null ? null : DateTimeOffset.Parse(expected).UtcDateTime, job.ExpiresAtUtc);
        if (job.ExpiresAtUtc.HasValue) Assert.Equal(DateTimeKind.Utc, job.ExpiresAtUtc.Value.Kind);
        Assert.Equal(new[] { "/v1/boards/acme/jobs?content=true", "/v1/boards/acme/jobs/123" }, handler.Paths);
    }

    [Theory]
    [InlineData("{\"id\":123,\"updated_at\":\"2030-01-01T00:00:00Z\",\"first_published\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{\"id\":456,\"application_deadline\":\"2030-01-01T00:00:00Z\"}")]
    public async Task Greenhouse_DoesNotInventExpiryOrUseAnotherPostsDeadline(string detail)
    {
        using var handler = new DeadlineHandler(detail);
        Assert.Null(Assert.Single(await FetchAsync(handler)).ExpiresAtUtc);
    }

    [Fact]
    public async Task Greenhouse_DisappearedDetailHasNoExpiry()
    {
        using var handler = new DeadlineHandler("{}", HttpStatusCode.NotFound);
        Assert.Null(Assert.Single(await FetchAsync(handler)).ExpiresAtUtc);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Greenhouse_DetailFailure_KeepsJobWithoutExpiry(
     HttpStatusCode status)
    {
        using var handler = new DeadlineHandler("{}", status);

        var job = Assert.Single(await FetchAsync(handler));

        Assert.Null(job.ExpiresAtUtc);

        Assert.Equal(
            new[]
            {
            "/v1/boards/acme/jobs?content=true",
            "/v1/boards/acme/jobs/123"
            },
            handler.Paths);
    }

    [Fact]
    public async Task Greenhouse_CancellationDuringDetailIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new DeadlineHandler("{}", cancel: cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FetchAsync(handler, cancellation.Token));
        Assert.Equal(2, handler.Paths.Count);
    }

    [Fact]
    public async Task Greenhouse_MalformedOptionalDetailDoesNotDiscardListing()
    {
        using var handler = new DeadlineHandler("not JSON");
        Assert.Null(Assert.Single(await FetchAsync(handler)).ExpiresAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Greenhouse_ExhaustedTransportOrTimeoutDoesNotDiscardListing(bool timeout)
    {
        using var handler = new DeadlineHandler("{}", error: timeout
            ? new TaskCanceledException("timeout") : new HttpRequestException("transport"));
        Assert.Null(Assert.Single(await FetchAsync(handler)).ExpiresAtUtc);
    }

    [Fact]
    public async Task Ashby_PublicationDateDoesNotBecomeExpiry()
    {
        using var handler = new DeadlineHandler("""
            {"jobs":[{"title":"Engineer","isListed":true,"publishedAt":"2026-10-01T00:00:00Z"}]}
            """);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var jobs = await new AshbyExternalJobProvider(new Factory(client))
            .FetchJobsAsync(new JobSource { AtsIdentifier = "acme" });
        Assert.Null(Assert.Single(jobs).ExpiresAtUtc);
    }

    private static async Task<IReadOnlyCollection<JobPortal.Application.Abstractions.Jobs.RawExternalJob>> FetchAsync(
        HttpMessageHandler handler, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.test/") };
        return await new GreenhouseExternalJobProvider(new Factory(client))
            .FetchJobsAsync(new JobSource { AtsIdentifier = "acme" }, cancellationToken);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class DeadlineHandler(string detail, HttpStatusCode status = HttpStatusCode.OK,
        CancellationTokenSource? cancel = null, Exception? error = null) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            var isList = path == "/v1/boards/acme/jobs?content=true";
            if (!isList)
            {
                cancel?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                if (error is not null) throw error;
            }
            return Task.FromResult(new HttpResponseMessage(isList ? HttpStatusCode.OK : status)
            {
                Content = new StringContent(isList
                    ? "{\"jobs\":[{\"id\":123,\"title\":\"Engineer\",\"content\":\"Apply before December\"}]}"
                    : detail)
            });
        }
    }
}
