using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Options;

namespace JobPortal.API.Services;

// One run per worker instance; different instances can run different sources.
// Database leases fence queue writes; existing session locks fence source execution.
public sealed partial class JobSourceRunWorker(IServiceScopeFactory scopes, JobSourceRunGuard guard,
    TimeProvider clock, IOptions<JobAggregationOptions> options, ILogger<JobSourceRunWorker> logger)
{
    public const int LeaseSeconds = 120;
    public const int HeartbeatSeconds = 20;

    [LoggerMessage(EventId = 4370, Level = LogLevel.Warning,
        Message = "JobSourceBackgroundRun Run={RunId} Source={SourceId} Reason={ReasonCode}.")]
    private static partial void Warning(ILogger logger, Guid runId, Guid sourceId, string reasonCode);

    public async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<JobSourceRun> candidates;
        await using (var queryScope = scopes.CreateAsyncScope())
            candidates = await queryScope.ServiceProvider.GetRequiredService<IJobSourceRunStore>()
                .CandidatesAsync(Now, stoppingToken);
        foreach (var candidate in candidates)
        {
            stoppingToken.ThrowIfCancellationRequested();
            IDisposable local;
            try { local = guard.Acquire(candidate.JobSourceId); }
            catch (ConflictException exception) when (exception.Code == "job_source_busy") { continue; }
            using (local)
            {
                await using var scope = scopes.CreateAsyncScope();
                await using var sourceLock = await scope.ServiceProvider.GetRequiredService<IJobSourceExecutionLock>()
                    .TryAcquireAsync(candidate.JobSourceId, stoppingToken);
                if (sourceLock is null) continue; // Busy does not consume an execution attempt.
                var retryAt = await RetryAtAsync(scope.ServiceProvider, candidate.JobSourceId, stoppingToken);
                var store = scope.ServiceProvider.GetRequiredService<IJobSourceRunStore>();
                if (candidate.Status == JobSourceRunStatus.Running)
                {
                    await store.RecoverAsync(candidate.Id, Now, retryAt, stoppingToken);
                    continue; // Persist Interrupted first, then wait for cooldown. Never immediate restart.
                }
                var claimed = await store.TryClaimAsync(candidate.Id, Guid.NewGuid(), Now, Now.AddSeconds(LeaseSeconds), stoppingToken);
                if (claimed is null) continue;
                await ExecuteAsync(scope.ServiceProvider, claimed, retryAt, stoppingToken);
                return;
            }
        }
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task<DateTime> RetryAtAsync(IServiceProvider services, Guid sourceId, CancellationToken ct)
    {
        var source = await services.GetRequiredService<IJobSourceRepository>().GetByIdAsync(sourceId, ct);
        return Now.AddMinutes(Math.Max(source?.ScanIntervalMinutes ?? 0,
            Math.Max(60, options.Value.Scheduler.InterruptedRunCooldownMinutes)));
    }

    private async Task ExecuteAsync(IServiceProvider services, JobSourceRun run, DateTime retryAt, CancellationToken stoppingToken)
    {
        var progress = services.GetRequiredService<IJobSourceRunProgress>();
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var heartbeatStop = new CancellationTokenSource();
        var heartbeat = HeartbeatAsync(run, progress, execution, heartbeatStop.Token);
        var status = JobSourceRunStatus.Interrupted;
        try
        {
            var result = await services.GetRequiredService<IJobSourceRunner>().RunAsync(run.JobSourceId, execution.Token);
            status = result.Succeeded ? JobSourceRunStatus.Succeeded : JobSourceRunStatus.Failed;
            var captured = progress.Snapshot;
            // A final infrastructure failure can return a zero-counter error result
            // after per-item durable commits. Keep the last observed progress in that case.
            var counters = !result.Succeeded && result.TotalReceived == 0 && captured.Counters.TotalReceived > 0
                ? captured.Counters with { Succeeded = false, Error = null } : result;
            progress.Report(new("Finalizing", captured.Processed, counters));
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested)
        {
            Warning(logger, run.Id, run.JobSourceId, "worker_interrupted");
        }
        catch (Exception)
        {
            status = JobSourceRunStatus.Failed;
            Warning(logger, run.Id, run.JobSourceId, "source_run_failed");
        }
        finally
        {
            await heartbeatStop.CancelAsync();
            await heartbeat; // Every task is awaited; no fire-and-forget execution.
            // Separate scope: runner error isolation may have reset its ChangeTracker.
            // Bounded cleanup is independent of the already-cancelled shutdown token.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                // Delay from interruption/completion, not from the beginning of a long run.
                var next = status == JobSourceRunStatus.Interrupted
                    ? await RetryAtAsync(scope.ServiceProvider, run.JobSourceId, cleanup.Token) : retryAt;
                if (!await scope.ServiceProvider.GetRequiredService<IJobSourceRunStore>()
                    .FinishAsync(run, status, Now, next, progress.Snapshot, cleanup.Token))
                    Warning(logger, run.Id, run.JobSourceId, "completion_lease_lost");
            }
            catch (Exception) { Warning(logger, run.Id, run.JobSourceId, "completion_deferred_to_recovery"); }
        }
    }

    private async Task HeartbeatAsync(JobSourceRun run, IJobSourceRunProgress progress,
        CancellationTokenSource execution, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(HeartbeatSeconds), clock, ct);
                await using var scope = scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<IJobSourceRunStore>()
                    .HeartbeatAsync(run, Now, Now.AddSeconds(LeaseSeconds), progress.Snapshot, ct))
                {
                    Warning(logger, run.Id, run.JobSourceId, "heartbeat_lease_lost");
                    await execution.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            Warning(logger, run.Id, run.JobSourceId, "heartbeat_failed");
            await execution.CancelAsync(); // Fail closed rather than work without a confirmed lease.
        }
    }
}
