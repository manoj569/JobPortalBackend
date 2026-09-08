using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Infrastructure.AIApply;
using Microsoft.Extensions.Options;

namespace JobPortal.API.HostedServices;

public sealed class AIApplyHostedService(IServiceScopeFactory scopes, IOptions<AIApplyOptions> options,
    IAIApplyWorkerState workerState, IAIApplyWorkerIdentity workerIdentity, IPlaywrightBrowserRuntime browserRuntime, TimeProvider clock,
    ILogger<AIApplyHostedService> logger) : BackgroundService
{
    private int activeExecutions;
    private int browserUnavailableLogged;
    private static readonly Action<ILogger, Exception?> IterationFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(5201, "AIApplyIterationFailed"),
            "AI Apply queue polling iteration failed.");
    private static readonly Action<ILogger, Exception?> BrowserUnavailable =
        LoggerMessage.Define(LogLevel.Warning, new EventId(5202, "AIApplyBrowserUnavailable"),
            "AI Apply browser capability is unavailable; queue claiming is paused.");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        workerState.Started();
        var concurrency = Math.Clamp(options.Value.MaxConcurrentApplications, 1, 20);
        if (!options.Value.Enabled) return;
        await PersistHeartbeatAsync(false, false, stoppingToken);
        try { await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => PollAsync(stoppingToken))); }
        finally
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<IAIApplyOperationalStore>().MarkWorkerInactiveAsync(workerIdentity.Id, clock.GetUtcNow().UtcDateTime, CancellationToken.None); }
            catch { IterationFailed(logger, null); }
        }
    }

    private async Task PollAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var browserAvailable = options.Value.Enabled && options.Value.Browser.Enabled &&
                    await browserRuntime.IsAvailableAsync(stoppingToken);
                if (browserAvailable)
                {
                    Interlocked.Exchange(ref browserUnavailableLogged, 0);
                    using var scope = scopes.CreateScope();
                    Interlocked.Increment(ref activeExecutions); workerState.Heartbeat(Volatile.Read(ref activeExecutions));
                    bool processed;
                    try { processed = await scope.ServiceProvider.GetRequiredService<IAIApplyExecutionService>().ProcessNextAsync(stoppingToken); }
                    finally { Interlocked.Decrement(ref activeExecutions); workerState.Heartbeat(Volatile.Read(ref activeExecutions)); }
                    await PersistHeartbeatAsync(true, false, stoppingToken);
                    AIApplyTelemetry.WorkerLoops.Add(1, AIApplyTelemetry.Tags("poll", processed ? "processed" : "empty"));
                    if (processed) continue;
                }
                else if (options.Value.Enabled && options.Value.Browser.Enabled &&
                    Interlocked.Exchange(ref browserUnavailableLogged, 1) == 0)
                    BrowserUnavailable(logger, null);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { IterationFailed(logger, null); AIApplyTelemetry.WorkerLoopFailures.Add(1, AIApplyTelemetry.Tags("poll", "failed")); await PersistHeartbeatAsync(false, true, stoppingToken); }
            workerState.Heartbeat(Volatile.Read(ref activeExecutions));
            await PersistHeartbeatAsync(false, false, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.QueuePollingIntervalSeconds, 2, 300)), stoppingToken);
        }
    }

    private async Task PersistHeartbeatAsync(bool pollSucceeded, bool pollFailed, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<IAIApplyOperationalStore>();
            var local = workerState.Snapshot(); var now = clock.GetUtcNow().UtcDateTime;
            await store.UpsertWorkerAsync(workerIdentity.Id, local.StartedAtUtc, now, Volatile.Read(ref activeExecutions), Math.Clamp(options.Value.MaxConcurrentApplications, 1, 20), pollSucceeded, pollFailed, ct);
            if (pollSucceeded) await store.CleanupWorkersAsync(now.AddDays(-Math.Clamp(options.Value.Observability.WorkerRetentionDays, 1, 90)), 100, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { IterationFailed(logger, null); }
    }
}
