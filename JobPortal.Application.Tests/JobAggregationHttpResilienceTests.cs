using System.Net;
using System.Net.Http.Headers;
using JobPortal.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobAggregationHttpResilienceTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task TransientResponsesRetryWithinBound(int status)
    {
        var clock = new SchedulerFixture.ControlledClock();
        using var transport = new Transport((count, _) => Task.FromResult(Response(count < 3 ? status : 200)));
        using var client = Client(clock, transport);
        var task = client.GetAsync("https://api.lever.co/public");
        var delays = await DriveAsync(task, clock);
        using var response = await task;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, transport.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, delays);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(501)]
    public async Task PermanentErrorsDoNotRetry(int status)
    {
        using var transport = new Transport((_, _) => Task.FromResult(Response(status)));
        using var client = Client(new SchedulerFixture.ControlledClock(), transport);
        using var response = await client.GetAsync("https://api.lever.co/public");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterDeltaAndDateAreHonored(bool date)
    {
        var clock = new SchedulerFixture.ControlledClock();
        using var transport = new Transport((count, _) =>
        {
            var response = Response(count == 1 ? 429 : 200);
            response.Headers.RetryAfter = date ? new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(7)) : new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        });
        using var client = Client(clock, transport);
        var task = client.GetAsync("https://api.lever.co/public");
        var delays = await DriveAsync(task, clock);
        using var result = await task;
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(delays));
        Assert.Equal(2, transport.Calls);
    }

    [Fact]
    public async Task LongRetryAfterDoesNotRetryEarlyAndLogsNoPayloadOrUrl()
    {
        var logger = new SafeLogger();
        using var transport = new Transport((_, _) =>
        {
            var response = Response(429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        });
        using var client = Client(new SchedulerFixture.ControlledClock(), transport, logger);
        using var result = await client.GetAsync("https://api.lever.co/secret?token=secret");
        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Equal(1, transport.Calls);
        Assert.Contains(logger.Messages, x => x.Contains("rate limited", StringComparison.Ordinal));
        Assert.All(logger.Messages, message => Assert.DoesNotContain("secret", message, StringComparison.OrdinalIgnoreCase));
        Assert.All(logger.Exceptions, Assert.Null);
    }

    [Fact]
    public async Task NetworkFailuresAreRetriedButBounded()
    {
        var clock = new SchedulerFixture.ControlledClock();
        using var transport = new Transport((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("secret transport")));
        using var client = Client(clock, transport);
        var task = client.GetAsync("https://api.lever.co/public");
        await DriveAsync(task, clock);
        await Assert.ThrowsAsync<HttpRequestException>(() => task);
        Assert.Equal(3, transport.Calls);
    }

    [Fact]
    public async Task PersistentServerFailuresStopAfterThreeAttempts()
    {
        var clock = new SchedulerFixture.ControlledClock();
        using var transport = new Transport((_, _) => Task.FromResult(Response(503)));
        using var client = Client(clock, transport);
        var task = client.GetAsync("https://api.lever.co/public");
        await DriveAsync(task, clock);
        using var result = await task;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(3, transport.Calls);
    }

    [Fact]
    public async Task CancellationInterruptsRetryDelay()
    {
        var clock = new SchedulerFixture.ControlledClock();
        using var transport = new Transport((_, _) => Task.FromResult(Response(429)));
        using var client = Client(clock, transport);
        using var cancellation = new CancellationTokenSource();
        var task = client.GetAsync("https://api.lever.co/public", cancellation.Token);
        _ = await clock.Timers.Reader.ReadAsync(); // attempt timeout
        _ = await clock.Timers.Reader.ReadAsync(); // retry delay
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task PerAttemptTimeoutIsRetriedAndRemainsBounded()
    {
        var clock = new SchedulerFixture.ControlledClock();
        using var transport = new Transport(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Response(200); });
        using var client = Client(clock, transport);
        var task = client.GetAsync("https://api.lever.co/public");
        await DriveAsync(task, clock, fireTimeouts: true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(3, transport.Calls);
    }

    private static async Task<List<TimeSpan>> DriveAsync(Task task, SchedulerFixture.ControlledClock clock, bool fireTimeouts = false)
    {
        var delays = new List<TimeSpan>();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!task.IsCompleted)
        {
            var ready = clock.Timers.Reader.WaitToReadAsync(limit.Token).AsTask();
            if (await Task.WhenAny(task, ready) == task) break;
            await ready;
            while (clock.Timers.Reader.TryRead(out var timer))
            {
                if (timer.DueTime == TimeSpan.FromSeconds(10)) { if (fireTimeouts) timer.Fire(); }
                else { delays.Add(timer.DueTime); timer.Fire(); }
            }
        }
        return delays;
    }

    private static HttpClient Client(TimeProvider clock, HttpMessageHandler transport, SafeLogger? logger = null) =>
        new(new AggregationHttpRetryHandler(clock, logger ?? new SafeLogger(), new AggregationHttpRetryState()) { InnerHandler = transport }) { Timeout = Timeout.InfiniteTimeSpan };
    private static HttpResponseMessage Response(int status) => new((HttpStatusCode)status) { Content = new StringContent("secret response") };

    [Fact]
    public async Task RetryAfterCooldownProtectsSubsequentSourcesWithoutSleepingOrNetwork()
    {
        var clock = new SchedulerFixture.ControlledClock();
        var state = new AggregationHttpRetryState();
        using var transport = new Transport((_, _) =>
        {
            var response = Response(429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(new AggregationHttpRetryHandler(clock, new SafeLogger(), state) { InnerHandler = transport });
        using var first = await client.GetAsync("https://api.lever.co/first");
        using var second = await client.GetAsync("https://api.lever.co/second");
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(TimeSpan.FromHours(1), second.Headers.RetryAfter!.Delta);
        Assert.Equal(TimeSpan.Zero, state.Remaining("Lever", clock.GetUtcNow().AddHours(2)));
        Assert.Equal(TimeSpan.Zero, state.Remaining("Ashby", clock.GetUtcNow()));
    }

    private sealed class Transport(Func<int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(++Calls, cancellationToken);
    }
    private sealed class SafeLogger : ILogger<AggregationHttpRetryHandler>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Messages.Add(formatter(state, exception)); Exceptions.Add(exception); }
    }
}
