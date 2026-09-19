using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.Services;

// Only installed on public aggregation GET clients, not Adzuna or AI Apply.
public sealed class AggregationHttpRetryHandler(TimeProvider clock, ILogger<AggregationHttpRetryHandler> logger,
    AggregationHttpRetryState retryState) : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);
    private static readonly Action<ILogger, string, int, int, double, Exception?> Retrying = LoggerMessage.Define<string, int, int, double>(
        LogLevel.Warning, new EventId(4320, nameof(Retrying)),
        "Aggregation {Provider} HTTP retry {Attempt}; status {StatusCode} (0 = transport/timeout), delay {DelaySeconds}s.");
    private static readonly Action<ILogger, string, Exception?> RateLimited = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(4321, nameof(RateLimited)), "Aggregation {Provider} rate limited.");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Method != HttpMethod.Get) return await base.SendAsync(request, cancellationToken);
        var provider = request.RequestUri?.Host switch
        {
            "boards-api.greenhouse.io" => "Greenhouse", "api.lever.co" => "Lever", "api.ashbyhq.com" => "Ashby", _ => "ATS"
        };
        var remaining = retryState.Remaining(provider, clock.GetUtcNow());
        if (remaining > TimeSpan.Zero)
        {
            // A previous source may have been rate-limited. Do not ignore a long
            // Retry-After on the next poll; the bounded host set lives across clients.
            RateLimited(logger, provider, null);
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new RetryConditionHeaderValue(remaining) }
            };
        }
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            HttpResponseMessage? response = null;
            TimeSpan delay;
            var status = 0;
            try
            {
                response = await base.SendAsync(copy, linked.Token);
                // Buffer within the per-attempt timeout, including stalled body reads.
                await response.Content.LoadIntoBufferAsync(linked.Token);
                status = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.TooManyRequests) RateLimited(logger, provider, null);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is not null)
                {
                    var now = clock.GetUtcNow();
                    var cooldown = RetryDelay(response, attempt);
                    var maximum = DateTimeOffset.MaxValue - now;
                    retryState.Defer(provider, now + (cooldown > maximum ? maximum : cooldown));
                }
                var transient = response.StatusCode == HttpStatusCode.TooManyRequests || status is 500 or 502 or 503 or 504;
                if (!transient || attempt == MaxAttempts) return response;
                delay = RetryDelay(response, attempt);
                // Never retry earlier than the server requests. Large Retry-After
                // values defer to the next source attempt, not an early capped retry.
                if (delay > MaximumDelay) return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                response?.Dispose();
                response = null;
                if (attempt == MaxAttempts) throw;
                delay = TimeSpan.FromSeconds(attempt);
            }
            catch { response?.Dispose(); throw; }
            response?.Dispose();
            Retrying(logger, provider, attempt, status, delay.TotalSeconds, null);
            await Task.Delay(delay, clock, cancellationToken);
        }
    }

    private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta ?? (header?.Date is { } date ? date - clock.GetUtcNow() : TimeSpan.FromSeconds(attempt));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}

// Only the three known provider labels plus ATS test/fallback label are used.
// This is process-local rate-limit memory, not a distributed execution lock.
public sealed class AggregationHttpRetryState
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> deferred = new(StringComparer.Ordinal);
    public TimeSpan Remaining(string provider, DateTimeOffset now) =>
        deferred.TryGetValue(provider, out var until) && until > now ? until - now : TimeSpan.Zero;
    public void Defer(string provider, DateTimeOffset until) =>
        deferred.AddOrUpdate(provider, until, (_, previous) => previous > until ? previous : until);
}
