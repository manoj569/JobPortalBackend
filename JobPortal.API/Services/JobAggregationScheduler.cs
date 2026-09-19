using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;

namespace JobPortal.API.Services;

// Singleton orchestration; each source gets its own scoped runner/DbContext.
public sealed class JobAggregationScheduler(
    IServiceScopeFactory scopes, IOptions<JobAggregationOptions> options,
    JobSourceRunGuard guard, TimeProvider clock, ILogger<JobAggregationScheduler> logger)
{
    private static readonly Action<ILogger, int, Exception?> DueFound = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(4301, nameof(DueFound)), "Job aggregation found {DueSourceCount} due sources.");
    private static readonly Action<ILogger, Guid, Exception?> SourceStarted = LoggerMessage.Define<Guid>(
        LogLevel.Information, new EventId(4302, nameof(SourceStarted)), "Scheduled aggregation starting for {JobSourceId}.");
    private static readonly Action<ILogger, Guid, int, int, int, int, bool, Exception?> SourceCompleted =
        LoggerMessage.Define<Guid, int, int, int, int, bool>(LogLevel.Information, new EventId(4303, nameof(SourceCompleted)),
            "Scheduled aggregation {JobSourceId}: Created {Created}, Matched {Matched}, Skipped {Skipped}, Failed {Failed}, Succeeded {Succeeded}.");
    private static readonly Action<ILogger, Guid, Exception?> SourceFailed = LoggerMessage.Define<Guid>(
        LogLevel.Warning, new EventId(4304, nameof(SourceFailed)), "Scheduled aggregation failed for {JobSourceId}; other sources will continue.");
    private static readonly Action<ILogger, Exception?> IterationFailed = LoggerMessage.Define(
        LogLevel.Error, new EventId(4305, nameof(IterationFailed)), "Job aggregation iteration failed; retrying on the next poll.");
    private static readonly Action<ILogger, Guid, Exception?> SourceBusy = LoggerMessage.Define<Guid>(
        LogLevel.Information, new EventId(4306, nameof(SourceBusy)), "Skipping aggregation {JobSourceId}: distributed execution lock busy.");
    private static readonly Action<ILogger, Guid, int, Exception?> SourceReceived = LoggerMessage.Define<Guid, int>(
        LogLevel.Information, new EventId(4307, nameof(SourceReceived)), "Aggregation {JobSourceId} received {Received} records.");

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value.Scheduler;
        if (!settings.Enabled) return;
        try
        {
            Guid[] sourceIds;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var sources = await scope.ServiceProvider.GetRequiredService<IJobSourceRepository>()
                    .GetDueSourcesAsync(clock.GetUtcNow().UtcDateTime, settings.BatchSize, cancellationToken);
                sourceIds = sources.Select(x => x.Id).ToArray();
            }
            if (sourceIds.Length == 0) return;
            DueFound(logger, sourceIds.Length, null);
            await Parallel.ForEachAsync(sourceIds, new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.MaxConcurrentSources,
                CancellationToken = cancellationToken
            }, async (sourceId, token) => await RunSourceAsync(sourceId, token));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Raw exception messages may include connection strings or response bodies.
            IterationFailed(logger, null);
        }
    }

    private async Task RunSourceAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        IDisposable lease;
        try { lease = guard.Acquire(sourceId); }
        catch (ConflictException exception) when (exception.Code == "job_source_busy") { return; }

        using (lease)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await using var distributed = await scope.ServiceProvider.GetRequiredService<IJobSourceExecutionLock>()
                    .TryAcquireAsync(sourceId, cancellationToken);
                if (distributed is null) { SourceBusy(logger, sourceId, null); return; }
                // A manual run or edit may have finished since the due query. Recheck
                // after acquiring the SAME guard used by admin runs/updates/deletes.
                var source = await scope.ServiceProvider.GetRequiredService<IJobSourceRepository>()
                    .GetByIdAsync(sourceId, cancellationToken);
                if (source is null || !source.IsActive || source.IsDeleted ||
                    (source.LastRunAtUtc.HasValue &&
                     source.LastRunAtUtc.Value.AddMinutes(source.ScanIntervalMinutes) > clock.GetUtcNow().UtcDateTime))
                    return;

                SourceStarted(logger, sourceId, null);
                var result = await scope.ServiceProvider.GetRequiredService<IJobSourceRunner>()
                    .RunAsync(sourceId, cancellationToken);
                SourceCompleted(logger, sourceId, result.Created, result.Matched, result.Skipped, result.Failed, result.Succeeded, null);
                SourceReceived(logger, sourceId, result.TotalReceived, null);
                if (!result.Succeeded) SourceFailed(logger, sourceId, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { SourceFailed(logger, sourceId, null); }
        }
    }
}
