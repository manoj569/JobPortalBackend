using Microsoft.Extensions.Logging;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;

namespace JobPortal.Application.Services;

public sealed partial class JobSourceRunner(
    IJobSourceRepository sources,
    IEnumerable<IExternalJobProvider> providers,
    IJobIngestionService ingestionService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IJobSourceCategoryResolver categoryResolver,
    IExternalJobNormalizer normalizer,
    ILogger<JobSourceRunner>? logger = null,
    IJobAutoPublishService? autoPublishService = null,
    IExternalJobMetadataEnricher? enricher = null,
    IJobRepository? jobRepository = null,
    JobPortal.Application.Features.JobAggregation.IJobSourceRunProgress? runProgress = null,
    JobPortal.Application.Features.JobAggregation.IJobSourcePublicationPolicy? publicationPolicy = null) : IJobSourceRunner
{
    private static readonly Action<ILogger, Guid, string, int, double, Exception?> Progress =
        LoggerMessage.Define<Guid, string, int, double>(LogLevel.Information, new EventId(4325, nameof(Progress)),
            "Job source {JobSourceId}: {Phase}, items {Count}, elapsed {ElapsedMilliseconds} ms.");
    private static readonly Action<ILogger, Guid, int, int, int, int, int, Exception?> RunCompleted =
        LoggerMessage.Define<Guid, int, int, int, int, int>(
            LogLevel.Information,
            new EventId(4320, nameof(RunCompleted)),
            "Job source run {JobSourceId}: Discovered {Discovered}, Created {Created}, " +
            "ExistingDuplicate {ExistingDuplicate}, Rejected {Rejected}, Failed {Failed}.");

    private static readonly Action<ILogger, Guid, string, int, Exception?> RunReason =
        LoggerMessage.Define<Guid, string, int>(
            LogLevel.Debug,
            new EventId(4321, nameof(RunReason)),
            "Job source run {JobSourceId} reason {ReasonCode}: {Count}.");

    private static readonly Action<ILogger, Guid, int, int, int, int, int, Exception?> PublicationCompleted =
        LoggerMessage.Define<Guid, int, int, int, int, int>(LogLevel.Information,
            new EventId(4324, nameof(PublicationCompleted)),
            "Job source run {JobSourceId}: Published {Published}, NeedsReview {NeedsReview}, " +
            "QualityRejected {QualityRejected}, PublishFailed {PublishFailed}, AutoPublishDisabled {AutoPublishDisabled}.");

    private static readonly Action<ILogger, Guid, Exception?> ItemFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(4322, nameof(ItemFailed)),
            "Job source run {JobSourceId}: individual item failed and was isolated; remaining items continue.");

    private static readonly Action<ILogger, Guid, Exception?> SourceRunFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(4323, nameof(SourceRunFailed)),
            "Job source run {JobSourceId} failed at provider/infrastructure level; no raw exception text is persisted.");

    // Optional so existing constructor callers (including tests) remain valid.
    private readonly ILogger<JobSourceRunner>? _logger = logger;
    private readonly IJobRepository? _jobRepository = jobRepository;

    [LoggerMessage(EventId = 4353, Level = LogLevel.Information,
        Message = "SuccessFactorsSyncCompleted Source={JobSourceId} Fetched={Fetched} Inserted={Inserted} Updated={Updated} Unchanged={Unchanged} Closed={Closed} Failed={Failed} DurationMs={DurationMs}.")]
    private static partial void LogSuccessFactorsSyncCompleted(ILogger logger, Guid jobSourceId, int fetched,
        int inserted, int updated, int unchanged, int closed, int failed, double durationMs);

    [LoggerMessage(EventId = 4354, Level = LogLevel.Information,
        Message = "JobSourceRunAttemptStarted Source={JobSourceId} AttemptAt={AttemptAt}.")]
    private static partial void LogAttempt(ILogger logger, Guid jobSourceId, DateTime attemptAt);
    [LoggerMessage(EventId = 4355, Level = LogLevel.Information,
        Message = "JobSourceRunCancelled Source={JobSourceId} Processed={Processed} DurationMs={DurationMs}.")]
    private static partial void LogCancelled(ILogger logger, Guid jobSourceId, int processed, double durationMs);
    [LoggerMessage(EventId = 4356, Level = LogLevel.Information,
        Message = "JobSourceRunSucceeded Source={JobSourceId} Processed={Processed} DurationMs={DurationMs}.")]
    private static partial void LogSucceeded(ILogger logger, Guid jobSourceId, int processed, double durationMs);
    [LoggerMessage(EventId = 4357, Level = LogLevel.Warning,
        Message = "JobSourceRunFailed Source={JobSourceId} ErrorType={ErrorType}.")]
    private static partial void LogFailed(ILogger logger, Guid jobSourceId, string errorType);

    public async Task<JobSourceRunResult> RunAsync(
        Guid jobSourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var source = await sources.GetByIdAsync(
            jobSourceId,
            cancellationToken);

        if (source is null)
        {
            return new JobSourceRunResult
            {
                JobSourceId = jobSourceId,
                Succeeded = false,
                Error = "Job source was not found."
            };
        }

        if (!source.IsActive)
        {
            return new JobSourceRunResult
            {
                JobSourceId = source.Id,
                Succeeded = false,
                Error = "Job source is inactive."
            };
        }

        // Admin and scheduler callers already hold the SAME local/distributed
        // execution guards. Save the attempt before any provider/network work.
        var attemptAt = timeProvider.GetUtcNow().UtcDateTime;
        source.LastRunAtUtc = attemptAt;
        sources.Update(source);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (_logger is not null) LogAttempt(_logger, source.Id, attemptAt);
        cancellationToken.ThrowIfCancellationRequested();

        var provider = providers.FirstOrDefault(
            x => x.AtsType == source.AtsType);

        if (provider is null)
        {
            // Unsupported configurations are failed attempts too: automatic polling
            // must respect their ScanIntervalMinutes rather than retry every poll.
            source.LastError = $"No provider is registered for ATS type '{source.AtsType}'.";
            source.ConsecutiveFailures++;
            sources.Update(source);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (_logger is not null) LogFailed(_logger, source.Id, "UnsupportedProvider");
            return new JobSourceRunResult
            {
                JobSourceId = source.Id,
                Succeeded = false,
                Error = source.LastError
            };
        }

        var previousSuccessfulRun = source.LastSuccessfulRunAtUtc;
        var previousFailures = source.ConsecutiveFailures;
        var previousError = source.LastError;
        var runTimer = System.Diagnostics.Stopwatch.StartNew();
        var processed = 0;

        try
        {
            publicationPolicy?.Validate(source);
            publicationPolicy?.ApplyLicensedLogo(source);
            ExternalJobSourceSnapshot? snapshot = null;
            IReadOnlyCollection<RawExternalJob> rawJobs;
            IReadOnlyCollection<RawExternalJob> observedJobs;
            if (provider is ICompleteExternalJobProvider completeProvider)
            {
                snapshot = await completeProvider.FetchSnapshotAsync(source, cancellationToken);
                // Import eligibility is not evidence that a vacancy disappeared. Preserve all
                // validated provider identities before geographic filtering for reconciliation.
                observedJobs = snapshot.Jobs;
                if (publicationPolicy is not null) snapshot = publicationPolicy.Select(source, snapshot);
                rawJobs = snapshot.Jobs.Select(x => x with
                {
                    CompanyId = source.CompanyId,
                    JobSourceId = string.IsNullOrWhiteSpace(x.ExternalId) ? null : source.Id
                }).ToArray();
            }
            else
            {
                rawJobs = await provider.FetchJobsAsync(source, cancellationToken);
                observedJobs = rawJobs;
            }
            if (_logger is not null) Progress(_logger, source.Id, "Provider fetch completed", rawJobs.Count, runTimer.Elapsed.TotalMilliseconds, null);
            runProgress?.Report(new("DbPreload", 0, new JobSourceRunResult
            { JobSourceId = source.Id, TotalReceived = rawJobs.Count + (snapshot?.Skipped ?? 0) }));
            var processingTimer = System.Diagnostics.Stopwatch.StartNew();
            var enrichmentMilliseconds = 0d;
            var ingestionMilliseconds = 0d;
            (categoryResolver as IJobSourceCategoryRunCache)?.BeginRun();
            if (_logger is not null) Progress(_logger, source.Id, "Enrichment and ingestion started", rawJobs.Count, 0, null);

            // Prefetch canonical URL duplicates in one database round trip.
            // PrepareRunAsync only reads raw ApplicationUrl values, so normalization,
            // enrichment and category resolution remain isolated per provider item below.
            var preloadTimer = System.Diagnostics.Stopwatch.StartNew();
            if (ingestionService is IBulkJobIngestionService bulkIngestion)
                await bulkIngestion.PrepareRunAsync(rawJobs, cancellationToken);
            if (_logger is not null) Progress(_logger, source.Id, "DB preload completed", rawJobs.Count, preloadTimer.Elapsed.TotalMilliseconds, null);

            var created = 0;
            var updated = 0;
            var unchanged = 0;
            var matched = 0;
            var skipped = snapshot?.Skipped ?? 0;
            var failed = 0;
            var published = 0;
            var needsReview = 0;
            var qualityRejected = 0;
            var publishFailed = 0;
            var disabled = 0;
            var qualityReasons = new Dictionary<JobQualityReasonCode, int>();
            var reasonCounts = new Dictionary<JobIngestionReasonCode, int>();

            void ReportProgress(string phase) => runProgress?.Report(new(phase, processed, new JobSourceRunResult
            {
                JobSourceId = source.Id, TotalReceived = rawJobs.Count + (snapshot?.Skipped ?? 0),
                Created = created, Updated = updated, Unchanged = unchanged, Matched = matched,
                Skipped = skipped, Failed = failed, Published = published, NeedsReview = needsReview,
                QualityRejected = qualityRejected, PublishFailed = publishFailed, AutoPublishDisabled = disabled
            }));
            ReportProgress("Ingestion");

            foreach (var rawJob in rawJobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Long fetch/import runs must not outlive their publication permission.
                publicationPolicy?.Validate(source);

                var publicationAttempt = false;
                try
                {
                    var enrichmentStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    RawExternalJob preparedJob;
                    try
                    {
                        var normalized = normalizer.Normalize(rawJob);
                        normalized = enricher?.Enrich(normalized) ?? normalized;
                        var categoryId = await categoryResolver.ResolveCategoryIdAsync(source, normalized, cancellationToken);
                        preparedJob = normalized with { CategoryId = categoryId };
                    }
                    finally { enrichmentMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(enrichmentStart).TotalMilliseconds; }

                    var ingestionStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    JobIngestionResult result;
                    try { result = await ingestionService.IngestAsync(preparedJob, cancellationToken); }
                    finally { ingestionMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(ingestionStart).TotalMilliseconds; }

                    switch (result.Outcome)
                    {
                        case JobIngestionOutcome.Created:
                            created++;

                            // Only newly created aggregated jobs are considered.
                            // Existing duplicates must never be republished here.
                            if (result.JobId.HasValue &&
                                autoPublishService is not null)
                            {
                                publicationAttempt = true;
                                var publication = await autoPublishService.TryPublishAsync(
                                    result.JobId.Value,
                                    cancellationToken);
                                switch (publication.Outcome)
                                {
                                    case JobAutoPublishOutcome.Published: published++; break;
                                    case JobAutoPublishOutcome.NeedsReview: needsReview++; break;
                                    case JobAutoPublishOutcome.Rejected: qualityRejected++; break;
                                    case JobAutoPublishOutcome.Disabled: disabled++; break;
                                    case JobAutoPublishOutcome.JobNotFound:
                                        publishFailed++;
                                        failed++;
                                        reasonCounts[JobIngestionReasonCode.AutoPublishFailed] =
                                            reasonCounts.GetValueOrDefault(JobIngestionReasonCode.AutoPublishFailed) + 1;
                                        break;
                                }
                                foreach (var reason in publication.Reasons.Distinct())
                                    qualityReasons[reason] = qualityReasons.GetValueOrDefault(reason) + 1;
                            }

                            break;

                        case JobIngestionOutcome.Updated:
                            updated++;
                            break;

                        case JobIngestionOutcome.Unchanged:
                            unchanged++;
                            break;

                        case JobIngestionOutcome.MatchedByUrl:
                        case JobIngestionOutcome.MatchedByFingerprint:
                        case JobIngestionOutcome.MatchedByFuzzy:
                            matched++;
                            break;

                        case JobIngestionOutcome.CompanyNotFound:
                            skipped++;
                            break;

                        case JobIngestionOutcome.Invalid:
                            skipped++;
                            break;

                        default:
                            failed++;
                            break;
                    }

                    // Tally the machine-readable reason for every item result.
                    if (result.ReasonCode != JobIngestionReasonCode.None)
                    {
                        reasonCounts[result.ReasonCode] =
                            reasonCounts.GetValueOrDefault(result.ReasonCode) + 1;
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // A malformed/problematic individual record must not
                    // abort processing of the remaining provider records.
                    // Raw exception text may contain URLs or credentials and is
                    // therefore never persisted; only the structured reason tally
                    // records the failure classification.
                    unitOfWork.ResetAfterFailure();
                    (ingestionService as IBulkJobIngestionService)?.ResetRunAfterFailure();
                    failed++;
                    if (publicationAttempt) publishFailed++;
                    var failureReason = publicationAttempt
                        ? JobIngestionReasonCode.AutoPublishFailed : JobIngestionReasonCode.PersistenceError;
                    reasonCounts[failureReason] = reasonCounts.GetValueOrDefault(failureReason) + 1;
                    if (_logger is not null)
                    {
                        ItemFailed(_logger, source.Id, null);
                    }
                }
                finally
                {
                    processed++;
                    ReportProgress("Ingestion");
                    if (_logger is not null && processed % 25 == 0)
                        Progress(_logger, source.Id, "Enrichment and ingestion progress", processed, processingTimer.Elapsed.TotalMilliseconds, null);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var closed = 0;
            ReportProgress("Reconciliation");
            publicationPolicy?.Validate(source);
            var externalIdsForReconciliation = observedJobs.Select(x => x.ExternalId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToArray();
            var hasCompleteIdentities = externalIdsForReconciliation.Length == observedJobs.Count &&
                externalIdsForReconciliation.Distinct(StringComparer.Ordinal).Count() == observedJobs.Count;
            var canReconcile = snapshot is { IsComplete: true, Skipped: 0 } && hasCompleteIdentities && skipped == 0 && failed == 0;
            var reconciliationTimer = System.Diagnostics.Stopwatch.StartNew();
            if (canReconcile && _jobRepository is not null)
            {
                closed = await _jobRepository.CloseSourceJobsMissingFromSnapshotAsync(
                    source.Id, externalIdsForReconciliation, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            }
            // CloseSourceJobsMissingFromSnapshotAsync only stages tracked changes.
            // The final Save below commits closures and success bookkeeping together.
            // Cancellation before that save discards both, not already committed jobs.
            cancellationToken.ThrowIfCancellationRequested();
            if (_logger is not null)
            {
                Progress(_logger, source.Id, "Enrichment completed", processed, enrichmentMilliseconds, null);
                Progress(_logger, source.Id, "Insert/update processing completed (includes saves)", processed, ingestionMilliseconds, null);
                if ((ingestionService as IBulkJobIngestionService)?.RunMetrics is { } metrics)
                    Progress(_logger, source.Id, "Item SaveChanges attempts", metrics.SaveCalls, metrics.SaveMilliseconds, null);
                Progress(_logger, source.Id, "Reconciliation completed", closed, reconciliationTimer.Elapsed.TotalMilliseconds, null);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            // A complete-source provider's partial scan must never claim a successful
            // full run. Already committed items remain resumable on the next attempt.
            var successful = snapshot is null ? skipped == 0 && failed == 0 : canReconcile;
            source.LastSuccessfulRunAtUtc = successful ? now : previousSuccessfulRun;
            source.LastError = successful ? null : "External job source scan was incomplete or had failed/skipped items.";
            source.ConsecutiveFailures = successful ? 0 : previousFailures + 1;

            sources.Update(source);
            publicationPolicy?.Validate(source);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Provider-wide fetch failures never reach this per-item tally, but
            // any explicitly failed ingestion results are counted too.
            if (failed > 0 && !reasonCounts.ContainsKey(JobIngestionReasonCode.PersistenceError) &&
                !reasonCounts.ContainsKey(JobIngestionReasonCode.AutoPublishFailed) &&
                !reasonCounts.ContainsKey(JobIngestionReasonCode.Unknown))
            {
                reasonCounts[JobIngestionReasonCode.Unknown] =
                    reasonCounts.GetValueOrDefault(JobIngestionReasonCode.Unknown) + failed;
            }

            var runResult = new JobSourceRunResult
            {
                JobSourceId = source.Id,
                TotalReceived = rawJobs.Count + (snapshot?.Skipped ?? 0),
                Created = created,
                Updated = updated,
                Unchanged = unchanged,
                Closed = closed,
                Matched = matched,
                Skipped = skipped,
                Failed = failed,
                Published = published,
                NeedsReview = needsReview,
                QualityRejected = qualityRejected,
                PublishFailed = publishFailed,
                AutoPublishDisabled = disabled,
                QualityReasonCounts = qualityReasons.Count == 0 ? null : qualityReasons,
                ReasonCounts = reasonCounts.Count == 0 ? null : reasonCounts,
                Succeeded = successful,
                Error = source.LastError
            };

            if (_logger is not null)
            {
                if (successful) LogSucceeded(_logger, source.Id, processed, runTimer.Elapsed.TotalMilliseconds);
                else LogFailed(_logger, source.Id, "IncompleteOrUnsafeScan");
                Progress(_logger, source.Id, "Enrichment and ingestion completed", processed, processingTimer.Elapsed.TotalMilliseconds, null);
                Progress(_logger, source.Id, "Run completed", processed, runTimer.Elapsed.TotalMilliseconds, null);
                PublicationCompleted(_logger, source.Id, published, needsReview, qualityRejected, publishFailed, disabled, null);
                if (source.AtsType == JobPortal.Domain.Enums.AtsType.SuccessFactors)
                    LogSuccessFactorsSyncCompleted(_logger, source.Id, runResult.TotalReceived, created, updated, unchanged,
                        closed, failed, runTimer.Elapsed.TotalMilliseconds);
                foreach (var (reason, count) in qualityReasons)
                    RunReason(_logger, source.Id, $"Quality.{reason}", count, null);
                RunCompleted(
                    _logger,
                    source.Id,
                    runResult.TotalReceived,
                    runResult.Created,
                    runResult.ExistingDuplicate,
                    runResult.Rejected,
                    runResult.Failed,
                    null);

                foreach (var (reason, count) in reasonCounts)
                {
                    RunReason(_logger, source.Id, reason.ToString(), count, null);
                }
            }

            return runResult;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            unitOfWork.ResetAfterFailure();
            source.LastSuccessfulRunAtUtc = previousSuccessfulRun;
            source.LastError = previousError;
            source.ConsecutiveFailures = previousFailures;
            if (_logger is not null) LogCancelled(_logger, source.Id, processed, runTimer.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            // Infrastructure/provider-wide failure: distinguishable from the
            // per-item failures above. The raw exception is logged structurally
            // but never persisted to source.LastError (which may leak secrets).
            unitOfWork.ResetAfterFailure();

            if (_logger is not null)
            {
                SourceRunFailed(_logger, source.Id, null);
                LogFailed(_logger, source.Id, exception.GetType().Name);
            }

            // Exception messages can contain response bodies, URLs or credentials.
            source.LastError = "External job source run failed.";
            source.LastSuccessfulRunAtUtc = previousSuccessfulRun;
            source.ConsecutiveFailures = previousFailures + 1;

            sources.Update(source);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new JobSourceRunResult
            {
                JobSourceId = source.Id,
                Succeeded = false,
                Error = source.LastError
            };
        }
        finally
        {
            (categoryResolver as IJobSourceCategoryRunCache)?.EndRun();
            if (ingestionService is IBulkJobIngestionService bulk) await bulk.CompleteRunAsync();
            if (_logger is not null) Progress(_logger, source.Id, "Total duration (including cleanup)", 0, runTimer.Elapsed.TotalMilliseconds, null);
        }
    }
}
