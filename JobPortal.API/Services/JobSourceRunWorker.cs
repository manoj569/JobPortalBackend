using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using JobPortal.Domain.Enums;

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

    [LoggerMessage(EventId = 4389, Level = LogLevel.Information,
        Message = "JobSourceExecutionSummary Run={RunId} Source={SourceId} Status={Status} Execution={ExecutionCount} StopReason={StopReason} ListingTotal={ListingTotal} Checkpoint={Checkpoint} CumulativeCreated={Created} CumulativeExisting={Existing} Failed={Failed} CumulativePublished={Published} ReconciliationSafe={ReconciliationSafe} DurationMs={DurationMs} NextAttempt={NextAttemptUtc}.")]
    private static partial void Summary(ILogger logger, Guid runId, Guid sourceId, string status, int executionCount,
        string stopReason, int listingTotal, int checkpoint, int created, int existing, int failed, int published,
        bool reconciliationSafe, double durationMs, DateTime nextAttemptUtc);

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
                    await store.RecoverAsync(candidate.Id, Now, candidate.CheckpointJson is null ? retryAt : ContinuationAt, stoppingToken);
                    continue; // Persist Interrupted first, then wait for cooldown. Never immediate restart.
                }
                // Shared PostgreSQL session locks bound concurrency across Render instances.
                await using var slot = scope.ServiceProvider.GetService<IJobSourceRunCheckpoint>() is null ? null :
                    await AcquireSlotAsync(scope.ServiceProvider.GetRequiredService<IJobSourceExecutionLock>(), stoppingToken);
                if (scope.ServiceProvider.GetService<IJobSourceRunCheckpoint>() is not null && slot is null) return;
                var claimed = await store.TryClaimAsync(candidate.Id, Guid.NewGuid(), Now, Now.AddSeconds(LeaseSeconds), stoppingToken);
                if (claimed is null) continue;
                await ExecuteAsync(scope.ServiceProvider, claimed, retryAt, stoppingToken);
                return;
            }
        }
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateTime ContinuationAt => Now.AddSeconds(options.Value.Workday.ContinuationDelaySeconds);

    private async Task<IAsyncDisposable?> AcquireSlotAsync(IJobSourceExecutionLock locks, CancellationToken ct)
    {
        for (var index = 0; index < options.Value.Scheduler.MaxConcurrentSources; index++)
        {
            var key = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"CareerHarbor:IngestionSlot:v1:{index}"))[..16]);
            var slot = await locks.TryAcquireAsync(key, ct);
            if (slot is not null) return slot;
        }
        return null;
    }
    private async Task<DateTime> RetryAtAsync(IServiceProvider services, Guid sourceId, CancellationToken ct)
    {
        var source = await services.GetRequiredService<IJobSourceRepository>().GetByIdAsync(sourceId, ct);
        return Now.AddMinutes(Math.Max(source?.ScanIntervalMinutes ?? 0,
            Math.Max(60, options.Value.Scheduler.InterruptedRunCooldownMinutes)));
    }

    private async Task ExecuteAsync(IServiceProvider services, JobSourceRun run, DateTime retryAt, CancellationToken stoppingToken)
    {
        var duration = System.Diagnostics.Stopwatch.StartNew();
        var progress = services.GetRequiredService<IJobSourceRunProgress>();
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var heartbeatStop = new CancellationTokenSource();
        var checkpoint = services.GetService<IJobSourceRunCheckpoint>();
        var heartbeat = HeartbeatAsync(run, progress, execution, checkpoint, heartbeatStop.Token);
        var status = JobSourceRunStatus.Interrupted;
        try
        {
            var source = await services.GetRequiredService<IJobSourceRepository>().GetByIdAsync(run.JobSourceId, execution.Token);
            if (run.DiagnosticsJson is not null)
                progress.Report(new("Resuming", run.Processed,
                    System.Text.Json.JsonSerializer.Deserialize<JobSourceRunResult>(run.DiagnosticsJson) ?? new()));
            if (checkpoint is not null && source?.AtsType == AtsType.Workday)
            {
                await checkpoint.BindAsync(run, execution.Token);
                checkpoint.Enable(source);
            }
            if (source is null || !source.IsActive)
            {
                status = JobSourceRunStatus.Cancelled;
                var cancelledProgress = progress.Snapshot;
                progress.Report(cancelledProgress with { Counters = cancelledProgress.Counters with
                { RunId = run.Id, ContinuationPending = false, Succeeded = false, ReconciliationSafe = false,
                  StopReason = "SourceUnavailable" } });
                return;
            }
            if (checkpoint?.Enabled == true)
            {
                execution.CancelAfter(TimeSpan.FromSeconds(options.Value.Workday.ExecutionSeconds));
                if (run.ExecutionCount > options.Value.Workday.MaximumExecutions)
                {
                    status = JobSourceRunStatus.Failed;
                    progress.Report(new("ExecutionLimit", run.Processed, progress.Snapshot.Counters with
                    { RunId = run.Id, StopReason = "MaximumExecutionsExceeded", ContinuationPending = false, ReconciliationSafe = false }));
                    return;
                }
            }
            var result = await services.GetRequiredService<IJobSourceRunner>().RunAsync(run.JobSourceId, execution.Token);
            status = result.ContinuationPending ? JobSourceRunStatus.Paused : result.Succeeded ? JobSourceRunStatus.Succeeded :
                checkpoint?.Enabled == true && result.StopReason != "InfrastructureFailure" ? JobSourceRunStatus.Unsafe : JobSourceRunStatus.Failed;
            var captured = progress.Snapshot;
            // A final infrastructure failure can return a zero-counter error result
            // after per-item durable commits. Keep the last observed progress in that case.
            var counters = !result.Succeeded && result.TotalReceived == 0 && captured.Counters.TotalReceived > 0
                ? captured.Counters with { Succeeded = false, Error = null } : result;
            progress.Report(new("Finalizing", captured.Processed, counters));
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested)
        {
            if (checkpoint?.Enabled == true)
            {
                status = JobSourceRunStatus.Paused;
                var captured = progress.Snapshot;
                progress.Report(captured with { Counters = captured.Counters with
                { ContinuationPending = true, Succeeded = false, ReconciliationSafe = false,
                  StopReason = stoppingToken.IsCancellationRequested ? "HostShutdown" : "ExecutionBudgetOrLeaseInterrupted" } });
            }
            Warning(logger, run.Id, run.JobSourceId, stoppingToken.IsCancellationRequested
                ? "host_shutdown_cancelled" : "heartbeat_or_lease_cancelled");
        }
        catch (JobSourceSnapshotUnsafeException exception)
        {
            status = JobSourceRunStatus.Unsafe;
            var captured = progress.Snapshot;
            progress.Report(captured with { Phase = "Unsafe", Counters = captured.Counters with
            { RunId = run.Id, StopReason = exception.ReasonCode, ReconciliationSafe = false,
                ListingDiagnostics = exception.ListingDiagnostics ?? captured.Counters.ListingDiagnostics } });
        }
        catch (Exception)
        {
            status = JobSourceRunStatus.Failed;
            Warning(logger, run.Id, run.JobSourceId, "source_run_failed");
        }
        finally
        {
            if (checkpoint?.Enabled == true)
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await using var recoveryScope = scopes.CreateAsyncScope();
                    var row = await recoveryScope.ServiceProvider.GetRequiredService<IJobSourceRunStore>()
                        .GetAsync(run.JobSourceId, run.Id, recovery.Token);
                    var journal = recoveryScope.ServiceProvider.GetRequiredService<IJobSourceRunCheckpoint>();
                    if (row is not null)
                    {
                        await journal.BindAsync(row, recovery.Token);
                        if (journal.State?.CompletedResult is { Succeeded: true } completed)
                        {
                            status = JobSourceRunStatus.Succeeded;
                            progress.Report(new("Finalizing", completed.IndiaEligible + completed.MissingLocationAccepted, completed));
                        }
                        else
                        {
                            var counters = journal.CommittedCounters;
                            var captured = progress.Snapshot;
                            var recoveredProviderSkipped = journal.State?.RebaseCount > captured.Counters.RebaseCount
                                ? journal.State.ProviderSkipped : captured.Counters.ProviderSkipped;
                            var reasons = new Dictionary<JobIngestionReasonCode, int>(counters.ReasonCounts ?? new Dictionary<JobIngestionReasonCode, int>());
                            if (recoveredProviderSkipped > 0) reasons[JobIngestionReasonCode.ProviderError] = recoveredProviderSkipped;
                            progress.Report(captured with { Processed = counters.TotalReceived,
                                Counters = (captured.Counters with
                                {
                                    RunId = run.Id, ListingTotal = journal.State?.ListingTotal ?? captured.Counters.ListingTotal,
                                    RebaseCount = journal.State?.RebaseCount ?? captured.Counters.RebaseCount,
                                    ListingDiagnostics = journal.State?.RebaseCount > captured.Counters.RebaseCount
                                        ? journal.State.ListingDiagnostics : captured.Counters.ListingDiagnostics,
                                    ListingPages = journal.State?.ListingPages ?? captured.Counters.ListingPages,
                                    TotalReceived = journal.State?.RebaseCount > captured.Counters.RebaseCount
                                        ? journal.State.Cursor : Math.Max(captured.Counters.TotalReceived, journal.State?.Cursor ?? 0),
                                    Parsed = journal.State?.RebaseCount > captured.Counters.RebaseCount
                                        ? journal.State.ObservedJobs.Count : Math.Max(captured.Counters.Parsed, journal.State?.ObservedJobs.Count ?? 0),
                                    Created = counters.Created, Updated = counters.Updated, Unchanged = counters.Unchanged,
                                    Matched = counters.Matched, Failed = counters.Failed, Published = counters.Published,
                                    Skipped = counters.Skipped + recoveredProviderSkipped, ReasonCounts = reasons,
                                    NeedsReview = counters.NeedsReview, QualityRejected = counters.QualityRejected,
                                    PublishFailed = counters.PublishFailed, AutoPublishDisabled = counters.AutoPublishDisabled,
                                    ValidationReasonCounts = counters.ValidationReasonCounts, QualityReasonCounts = counters.QualityReasonCounts,
                                    CheckpointCursor = journal.State?.Cursor ?? 0,
                                    LastCheckpointAtUtc = journal.State?.CommittedAtUtc,
                                    RemainingWork = journal.State is { } state ? state.ListingTotal - state.Cursor : null
                                }).WithDetailProgress(checkpoint.Details) });
                        }
                    }
                }
                catch (Exception) { Warning(logger, run.Id, run.JobSourceId, "progress_recovery_deferred"); }
            }
            await heartbeatStop.CancelAsync();
            await heartbeat; // Every task is awaited; no fire-and-forget execution.
            // Separate scope: runner error isolation may have reset its ChangeTracker.
            // Bounded cleanup is independent of the already-cancelled shutdown token.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                // Delay from interruption/completion, not from the beginning of a long run.
                var next = status == JobSourceRunStatus.Paused ? ContinuationAt : status == JobSourceRunStatus.Interrupted
                    ? await RetryAtAsync(scope.ServiceProvider, run.JobSourceId, cleanup.Token) : retryAt;
                var final = progress.Snapshot;
                progress.Report(final with { Counters = final.Counters with { DurationMilliseconds = duration.Elapsed.TotalMilliseconds } });
                var store = scope.ServiceProvider.GetRequiredService<IJobSourceRunStore>();
                if (!await store
                    .FinishAsync(run, status, Now, next, progress.Snapshot, cleanup.Token))
                    Warning(logger, run.Id, run.JobSourceId, "completion_lease_lost");
                else
                {
                    var row = await store.GetAsync(run.JobSourceId, run.Id, cleanup.Token);
                    if (row is not null)
                    {
                        var recorded = JobSourceRunResponse.From(row).Diagnostics ?? progress.Snapshot.Counters;
                        Summary(logger, row.Id, row.JobSourceId, row.Status.ToString(), row.ExecutionCount,
                            recorded.StopReason ?? row.ErrorCode ?? "Unknown", recorded.ListingTotal, recorded.CheckpointCursor,
                            row.Created, row.Updated + row.Unchanged + row.Matched, row.Failed, row.Published,
                            recorded.ReconciliationSafe, recorded.DurationMilliseconds, row.NextAttemptAtUtc);
                    }
                }
            }
            catch (Exception) { Warning(logger, run.Id, run.JobSourceId, "completion_deferred_to_recovery"); }
        }
    }

    private async Task HeartbeatAsync(JobSourceRun run, IJobSourceRunProgress progress,
        CancellationTokenSource execution, IJobSourceRunCheckpoint? checkpoint, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(HeartbeatSeconds), clock, ct);
                await using var scope = scopes.CreateAsyncScope();
                var captured = progress.Snapshot;
                if (checkpoint?.Enabled == true) captured = captured with { Counters = captured.Counters.WithDetailProgress(checkpoint.Details) };
                if (!await scope.ServiceProvider.GetRequiredService<IJobSourceRunStore>()
                    .HeartbeatAsync(run, Now, Now.AddSeconds(LeaseSeconds), captured, ct))
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
