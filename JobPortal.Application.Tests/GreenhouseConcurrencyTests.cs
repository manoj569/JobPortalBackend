using System.Net;
using System.Text.Json;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class GreenhouseConcurrencyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task DetailFanoutIsBoundedAndResultsRetainOrder(int concurrency)
    {
        using var handler = new Handler(concurrency);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var provider = new GreenhouseExternalJobProvider(new Factory(client), Options.Create(new JobAggregationOptions { GreenhouseDetailConcurrency = concurrency }));
        var result = await provider.FetchJobsAsync(new JobSource { AtsIdentifier = "acme" });
        Assert.Equal(concurrency, handler.Peak);
        Assert.Equal(12, handler.Count);
        Assert.Equal(Enumerable.Range(1, 12).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), result.Select(j => j.ExternalId));
        Assert.All(result, j => Assert.Null(j.ExpiresAtUtc));
    }

    [Fact]
    public async Task CallerCancellationStopsWorkers()
    {
        using var handler = new Handler(2, block: true);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        using var cancel = new CancellationTokenSource();
        var task = new GreenhouseExternalJobProvider(new Factory(client)).FetchJobsAsync(new JobSource { AtsIdentifier = "acme" }, cancel.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, handler.Active);
        Assert.InRange(handler.Count, 1, 2);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(int concurrency, bool block = false) : HttpMessageHandler
    {
        public int Active;
        public int Peak;
        public int Count;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Query.Length > 0)
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                { jobs = Enumerable.Range(1, 12).Select(i => new { id = i, title = "Engineer" }) })) };
            Interlocked.Increment(ref Count);
            var active = Interlocked.Increment(ref Active);
            lock (barrier) Peak = Math.Max(Peak, active);
            if (active == concurrency) barrier.TrySetResult();
            Started.TrySetResult();
            try
            {
                if (block) await Task.Delay(Timeout.Infinite, cancellationToken);
                await barrier.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                return new(HttpStatusCode.OK) { Content = new StringContent("malformed JSON") };
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
}
