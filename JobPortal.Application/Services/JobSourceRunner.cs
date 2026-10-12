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
    JobPortal.Application.Features.JobAggregation.IJobSourcePublicationPolicy? publicationPolicy = null,
    IJobSourceRunCheckpoint? checkpoint = null) : IJobSourceRunner
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

    private static readonly Action<ILogger, Guid, string, string, int, Exception?> IngestionReason =
        LoggerMessage.Define<Guid, string, string, int>(LogLevel.Information,
            new EventId(4386, nameof(IngestionReason)),
            "JobSourceIngestionReason Source={JobSourceId} Stage={Stage} ReasonCode={ReasonCode} Count={Count}.");

    [LoggerMessage(EventId = 4387, Level = LogLevel.Information,
        Message = "JobSourcePersistenceSummary Source={SourceId} New={Created} Existing={Existing} Rejected={Rejected} ProviderSkipped={ProviderSkipped} SelectionSkipped={SelectionSkipped} Failed={Failed}.")]
    private static partial void LogPersistenceSummary(ILogger logger, Guid sourceId, int created, int existing,
        int rejected, int providerSkipped, int selectionSkipped, int failed);

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
    private readonly JobPortal.Application.Features.JobAggregation.IJobSourcePublicationPolicy selectionPolicy = publicationPolicy ??
        new JobPortal.Application.Features.JobAggregation.JobSourcePublicationPolicy(
            Microsoft.Extensions.Options.Options.Create(new JobPortal.Application.Features.JobAggregation.JobAggregationOptions()), timeProvider);
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

        checkpoint?.Enable(source);
        if (checkpoint is { Enabled: true, State.CompletedResult: { Succeeded: true } completed })
            return completed; // Final source bookkeeping/closures were already durably committed.

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

        checkpoint?.Enable(source);
        var durable = checkpoint is { Enabled: true };
        var previousSuccessfulRun = source.LastSuccessfulRunAtUtc;
        var previousFailures = source.ConsecutiveFailures;
        var previousError = source.LastError;
        var runTimer = System.Diagnostics.Stopwatch.StartNew();
        var processed = 0;
        var partialResult = new JobSourceRunResult { JobSourceId = source.Id };
        WorkdayListingDiagnostics? listingDiagnostics = checkpoint?.State?.ListingDiagnostics;

        try
        {
            publicationPolicy?.ApplyLicensedLogo(source);
            ExternalJobSourceSnapshot? snapshot = null;
            var providerSnapshotComplete = false;
            var reconciliationSafe = false;
            IReadOnlyList<string> reconciliationBlockReasons = [];
            var rawJobs = new List<RawExternalJob>();
            var observedJobs = new List<RawExternalJob>(durable ? checkpoint!.State?.ObservedJobs ?? [] : []);
            if (durable && checkpoint!.State is { } restored)
            {
                snapshot = selectionPolicy.Select(source, new(observedJobs, restored.ProviderSkipped, false));
                rawJobs.AddRange(snapshot.Jobs); // Earlier eligible items are already durably acknowledged.
            }
            var continuationPending = false;
            var traversalRebaseCount = durable ? checkpoint!.State?.RebaseCount ?? 0 : 0;
            var processingTimer = System.Diagnostics.Stopwatch.StartNew();
            var enrichmentMilliseconds = 0d;
            var ingestionMilliseconds = 0d;
            var savedCalls = 0;
            var saveMilliseconds = 0d;
            var batchPrepared = false;
            (categoryResolver as IJobSourceCategoryRunCache)?.BeginRun();

            var created = 0;
            var updated = 0;
            var unchanged = 0;
            var matched = 0;
            var skipped = 0;
            var failed = 0;
            var published = 0;
            var needsReview = 0;
            var qualityRejected = 0;
            var publishFailed = 0;
            var disabled = 0;
            var qualityReasons = new Dictionary<JobQualityReasonCode, int>();
            var reasonCounts = new Dictionary<JobIngestionReasonCode, int>();
            var validationReasons = new Dictionary<string, int>(StringComparer.Ordinal);

            void ReportProgress(string phase)
            {
                if (durable)
                {
                    var committed = checkpoint!.CommittedCounters;
                    created = committed.Created; updated = committed.Updated; unchanged = committed.Unchanged;
                    matched = committed.Matched; skipped = (snapshot?.Skipped ?? 0) + committed.Skipped;
                    failed = committed.Failed; published = committed.Published; needsReview = committed.NeedsReview;
                    qualityRejected = committed.QualityRejected; publishFailed = committed.PublishFailed;
                    disabled = committed.AutoPublishDisabled; processed = committed.TotalReceived;
                    reasonCounts.Clear(); validationReasons.Clear(); qualityReasons.Clear();
                    foreach (var (key, count) in committed.ReasonCounts ?? new Dictionary<JobIngestionReasonCode, int>()) reasonCounts[key] = count;
                    foreach (var (key, count) in committed.ValidationReasonCounts ?? new Dictionary<string, int>()) validationReasons[key] = count;
                    foreach (var (key, count) in committed.QualityReasonCounts ?? new Dictionary<JobQualityReasonCode, int>()) qualityReasons[key] = count;
                    if (snapshot?.Skipped > 0) reasonCounts[JobIngestionReasonCode.ProviderError] = snapshot.Skipped;
                }
                partialResult = new JobSourceRunResult
                {
                    JobSourceId = source.Id, RunId = durable ? checkpoint!.RunId : null,
                    ListingDiagnostics = listingDiagnostics,
                    ListingTotal = durable ? checkpoint!.State?.ListingTotal ?? 0 : listingDiagnostics?.UniquePaths ?? 0,
                    RebaseCount = durable ? checkpoint!.State?.RebaseCount ?? 0 : 0,
                    ListingPages = durable ? checkpoint!.State?.ListingPages ?? 0 : listingDiagnostics?.PageCount ?? 0,
                    CheckpointCursor = durable ? checkpoint!.State?.Cursor ?? 0 : 0,
                    LastCheckpointAtUtc = durable ? checkpoint!.State?.CommittedAtUtc : null,
                    RemainingWork = durable && checkpoint!.State is { } state ? state.ListingTotal - state.Cursor : null,
                    ObservedIdentities = observedJobs.Select(x => x.ExternalId).Distinct(StringComparer.Ordinal).Count(),
                    DetailAttempted = observedJobs.Count + (snapshot?.Skipped ?? 0), DetailSucceeded = observedJobs.Count,
                    DetailFailed = snapshot?.Skipped ?? 0, DurationMilliseconds = runTimer.Elapsed.TotalMilliseconds,
                    TotalReceived = observedJobs.Count + (snapshot?.Skipped ?? 0),
                    Parsed = observedJobs.Count,
                    EligibilityReasonCounts = snapshot?.EligibilityReasonCounts,
                    ScanComplete = providerSnapshotComplete,
                    ReconciliationSafe = reconciliationSafe,
                    ReconciliationBlockReasons = reconciliationBlockReasons,
                    Created = created, Updated = updated, Unchanged = unchanged, Matched = matched,
                    Skipped = skipped, Failed = failed, Published = published, NeedsReview = needsReview,
                    QualityRejected = qualityRejected, PublishFailed = publishFailed, AutoPublishDisabled = disabled,
                    QualityReasonCounts = new Dictionary<JobQualityReasonCode, int>(qualityReasons),
                    ReasonCounts = new Dictionary<JobIngestionReasonCode, int>(reasonCounts),
                    ValidationReasonCounts = new Dictionary<string, int>(validationReasons),
                    SelectionReasonCounts = snapshot?.SelectionReasonCounts
                };
                if (durable) partialResult = partialResult.WithDetailProgress(checkpoint!.Details);
                runProgress?.Report(new(phase, processed, partialResult));
            }
            ReportProgress("Ingestion");

            await foreach (var batch in FetchBatchesAsync(provider, source, cancellationToken))
            {
                // Provider prepares the replacement before its first yield. Old observed
                // identities and selection skips cannot join the new census.
                if (durable && checkpoint!.State?.RebaseCount != traversalRebaseCount)
                {
                    traversalRebaseCount = checkpoint.State?.RebaseCount ?? 0;
                    observedJobs.Clear(); rawJobs.Clear(); snapshot = null;
                    providerSnapshotComplete = false; reconciliationSafe = false;
                }
                listingDiagnostics = batch.ListingDiagnostics ?? listingDiagnostics;
                if (batch.ListingDiagnostics is { IngestionSafe: false } unsafeListing)
                    throw new JobSourceSnapshotUnsafeException(unsafeListing.ReasonCode, unsafeListing);
                observedJobs.AddRange(batch.Jobs);
                continuationPending = batch.ContinuationPending;
                var providerSkipped = (snapshot?.Skipped ?? 0) + batch.Skipped;
                providerSnapshotComplete = provider is ICompleteExternalJobProvider && batch.IsComplete;
                snapshot = new(observedJobs, providerSkipped, providerSnapshotComplete);
                // Apply the policy cumulatively: a configured 20-job cap must not
                // become 20 jobs PER batch. Geography selection preserves census completeness.
                var selected = selectionPolicy.Select(source, snapshot);
                if (selected is not null) snapshot = selected;
                var accepted = selected?.Jobs ?? observedJobs;
                var newJobs = accepted.Skip(rawJobs.Count).Select(x => x with
                {
                    CompanyId = source.CompanyId,
                    JobSourceId = source.Id,
                    CompanyName = source.Company?.Name ?? x.CompanyName
                }).ToArray();
                rawJobs.AddRange(newJobs);
                if (_logger is not null) Progress(_logger, source.Id, "Eligible batch received", newJobs.Length, runTimer.Elapsed.TotalMilliseconds, null);
                skipped += batch.Skipped;
                if (batch.Skipped > 0)
                    reasonCounts[JobIngestionReasonCode.ProviderError] =
                        reasonCounts.GetValueOrDefault(JobIngestionReasonCode.ProviderError) + batch.Skipped;
                ReportProgress("DbPreload");
                if (newJobs.Length > 0 && ingestionService is IBulkJobIngestionService bulkIngestion)
                {
                    if (batchPrepared && bulkIngestion.RunMetrics is { } previousMetrics)
                    {
                        savedCalls += previousMetrics.SaveCalls;
                        saveMilliseconds += previousMetrics.SaveMilliseconds;
                    }
                    var preloadTimer = System.Diagnostics.Stopwatch.StartNew();
                    var deferredPreload = source.AtsType == JobPortal.Domain.Enums.AtsType.Workday &&
                        newJobs.All(x => !x.ExpiresAtUtc.HasValue) && ingestionService is IBatchedJobIngestionService;
                    if (!deferredPreload)
                        await bulkIngestion.PrepareRunAsync(newJobs, cancellationToken);
                    batchPrepared = true;
                    if (_logger is not null && !deferredPreload) Progress(_logger, source.Id, "DB batch preload completed", newJobs.Length, preloadTimer.Elapsed.TotalMilliseconds, null);
                }

                IReadOnlyList<JobIngestionResult>? committedBatch = null;
                RawExternalJob[]? preparedBatch = null;
                if (newJobs.Length > 0 && source.AtsType == JobPortal.Domain.Enums.AtsType.Workday &&
                    newJobs.All(x => !x.ExpiresAtUtc.HasValue) && ingestionService is IBatchedJobIngestionService batchedIngestion &&
                    (!durable || !newJobs.Any(x => checkpoint!.Replay(x) is not null)))
                {
                    try
                    {
                        var preparationStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        preparedBatch = new RawExternalJob[newJobs.Length];
                        for (var i = 0; i < newJobs.Length; i++)
                        {
                            var normalized = normalizer.Normalize(newJobs[i]);
                            normalized = enricher?.Enrich(normalized) ?? normalized;
                            preparedBatch[i] = normalized with
                            { CategoryId = await categoryResolver.ResolveCategoryIdAsync(source, normalized, cancellationToken) };
                        }
                        enrichmentMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(preparationStart).TotalMilliseconds;
                        var commitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (preparedBatch.All(x => !x.ExpiresAtUtc.HasValue))
                            committedBatch = await batchedIngestion.IngestBatchAsync(preparedBatch, cancellationToken);
                        else
                            preparedBatch = null; // Preserve interleaved publication for newly enriched expiry.
                        if (committedBatch is { Count: 0 }) committedBatch = null;
                        ingestionMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(commitStart).TotalMilliseconds;
                        if (_logger is not null && committedBatch is not null)
                        {
                            if ((ingestionService as IBulkJobIngestionService)?.RunMetrics is { } batchMetrics)
                                Progress(_logger, source.Id, "DB locked batch preload completed", newJobs.Length, batchMetrics.PreloadMilliseconds, null);
                            Progress(_logger, source.Id, "Bounded batch committed", newJobs.Length,
                                System.Diagnostics.Stopwatch.GetElapsedTime(commitStart).TotalMilliseconds, null);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch
                    {
                        // Isolate malformed records/failed commits using the existing
                        // per-item path. Ambiguous commits are rechecked under locks.
                        unitOfWork.ResetAfterFailure();
                        (ingestionService as IBulkJobIngestionService)?.ResetRunAfterFailure();
                        if (durable) await checkpoint!.RefreshAsync(cancellationToken);
                        preparedBatch = null;
                        if (_logger is not null) ItemFailed(_logger, source.Id, null);
                    }
                }

                IReadOnlyDictionary<Guid, JobAutoPublishResult>? batchReview = null;
                if (committedBatch is not null && autoPublishService is IJobAutoPublishBatchReview reviews)
                {
                    try
                    {
                        batchReview = await reviews.ReviewBatchAsync(committedBatch
                            .Where(x => x.Outcome == JobIngestionOutcome.Created && x.JobId.HasValue)
                            .Select(x => x.JobId!.Value).ToArray(), cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    { /* Count already committed outcomes below, then propagate cancellation. */ }
                    catch { /* Existing per-item publication handling isolates failures. */ }
                }
                var batchIndex = 0;
                foreach (var rawJob in newJobs)
                {
                    if (committedBatch is null) cancellationToken.ThrowIfCancellationRequested();

                    var publicationAttempt = false;
                    JobIngestionResult? durableResult = null;
                    JobAutoPublishResult? durablePublication = null;
                    checkpoint?.SetCurrentItem(rawJob);
                    try
                    {
                        var enrichmentStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        RawExternalJob preparedJob;
                        try
                        {
                            if (preparedBatch is not null) preparedJob = preparedBatch[batchIndex];
                            else
                            {
                                var normalized = normalizer.Normalize(rawJob);
                                normalized = enricher?.Enrich(normalized) ?? normalized;
                                var categoryId = await categoryResolver.ResolveCategoryIdAsync(source, normalized, cancellationToken);
                                preparedJob = normalized with { CategoryId = categoryId };
                            }
                        }
                        finally { enrichmentMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(enrichmentStart).TotalMilliseconds; }

                        var ingestionStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        JobIngestionResult result;
                        try { result = durable ? checkpoint!.Replay(rawJob) ??
                            (committedBatch is not null ? committedBatch[batchIndex] : await ingestionService.IngestAsync(preparedJob, cancellationToken)) :
                            committedBatch is not null ? committedBatch[batchIndex] : await ingestionService.IngestAsync(preparedJob, cancellationToken); }
                        finally { ingestionMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(ingestionStart).TotalMilliseconds; }

                        durableResult = result;
                        switch (result.Outcome)
                        {
                            case JobIngestionOutcome.Created:
                                created++;
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
                                var validation = JobIngestionReasonCodes.ValidationDetail(result.Message);
                                validationReasons[validation] = validationReasons.GetValueOrDefault(validation) + 1;
                                break;

                            default:
                                failed++;
                                break;
                        }

                        // Count publication transitions, including eligible source-owned drafts from earlier runs.
                        if (result.Outcome is (JobIngestionOutcome.Created or JobIngestionOutcome.Updated or JobIngestionOutcome.Unchanged or
                            JobIngestionOutcome.MatchedByUrl or JobIngestionOutcome.MatchedByFingerprint or JobIngestionOutcome.MatchedByFuzzy) &&
                            result.JobId.HasValue && autoPublishService is not null && !cancellationToken.IsCancellationRequested)
                        {
                            publicationAttempt = true;
                            var publication = durable ? checkpoint!.ReplayedPublication(rawJob) : null;
                            publication ??= batchReview is not null && batchReview.TryGetValue(result.JobId.Value, out var reviewed)
                                ? reviewed : await autoPublishService.TryPublishAsync(result.JobId.Value, cancellationToken);
                            durablePublication = publication;
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

                        if (durable) await checkpoint!.CompleteItemAsync(rawJob, result, durablePublication, cancellationToken);

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
                        if (committedBatch is null) throw;
                    }
                    catch (JobSourceSnapshotUnsafeException) { throw; }
                    catch
                    {
                        // A malformed/problematic individual record must not
                        // abort processing of the remaining provider records.
                        // Raw exception text may contain URLs or credentials and is
                        // therefore never persisted; only the structured reason tally
                        // records the failure classification.
                        unitOfWork.ResetAfterFailure();
                        (ingestionService as IBulkJobIngestionService)?.ResetRunAfterFailure();
                        if (durable)
                        {
                            await checkpoint!.RefreshAsync(cancellationToken);
                            var recovered = checkpoint.Replay(rawJob);
                            if (!publicationAttempt && recovered is not null)
                                throw new JobSourceContinuationException("CommittedBatchRecoveryRequired");
                            await checkpoint.CompleteItemAsync(rawJob, recovered ?? durableResult ?? new JobIngestionResult
                            { Outcome = JobIngestionOutcome.Failed, ExplicitReasonCode = JobIngestionReasonCode.PersistenceError },
                                publicationAttempt ? new JobAutoPublishResult { Outcome = JobAutoPublishOutcome.JobNotFound } : null,
                                cancellationToken);
                        }
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
                        batchIndex++;
                        processed++;
                        ReportProgress("Ingestion");
                        if (_logger is not null && processed % 25 == 0)
                            Progress(_logger, source.Id, "Enrichment and ingestion progress", processed, processingTimer.Elapsed.TotalMilliseconds, null);
                    }
                }
                if (durable && batch.CheckpointCursor is { } cursor)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (autoPublishService is not null && newJobs.Any(x => checkpoint!.Replay(x)?.JobId is not null &&
                        checkpoint.ReplayedPublication(x) is null))
                        throw new JobSourceContinuationException("PublicationRecoveryRequired");
                    await checkpoint!.CommitAsync(cursor, snapshot?.Skipped ?? 0, observedJobs, cancellationToken);
                    ReportProgress("CheckpointCommitted");
                }

            }
            cancellationToken.ThrowIfCancellationRequested();
            var closed = 0;
            var externalIdsForReconciliation = observedJobs.Select(x => x.ExternalId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToArray();
            var hasCompleteIdentities = externalIdsForReconciliation.Length == observedJobs.Count &&
                externalIdsForReconciliation.Distinct(StringComparer.Ordinal).Count() == observedJobs.Count;
            var blockers = new List<string>();
            if (!providerSnapshotComplete) blockers.Add("ProviderSnapshotIncomplete");
            if (snapshot is not { IsComplete: true } && providerSnapshotComplete)
                blockers.Add("SelectionSnapshotIncomplete"); // Includes configured test caps.
            if (snapshot is null || snapshot.Skipped != 0) blockers.Add("ProviderSkippedItems");
            if (!hasCompleteIdentities) blockers.Add("IncompleteOrDuplicateSourceIdentities");
            if (skipped != 0) blockers.Add("SkippedItems");
            if (failed != 0) blockers.Add("FailedItems");
            reconciliationBlockReasons = blockers.ToArray();
            var canReconcile = blockers.Count == 0;
            reconciliationSafe = canReconcile;
            ReportProgress("Reconciliation");
            if (_logger is not null)
                foreach (var reason in reconciliationBlockReasons)
                    IngestionReason(_logger, source.Id, "Reconciliation", reason, 1, null);
            if (_logger is not null) Progress(_logger, source.Id, canReconcile ? "Reconciliation allowed" : "Reconciliation blocked: incomplete or unsafe snapshot",
                observedJobs.Count, runTimer.Elapsed.TotalMilliseconds, null);
            var reconciliationTimer = System.Diagnostics.Stopwatch.StartNew();
            if (provider is ICompleteExternalJobProvider && canReconcile && _jobRepository is not null)
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
                    Progress(_logger, source.Id, "Item SaveChanges attempts", savedCalls + metrics.SaveCalls, saveMilliseconds + metrics.SaveMilliseconds, null);
                Progress(_logger, source.Id, "Reconciliation completed", closed, reconciliationTimer.Elapsed.TotalMilliseconds, null);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            // A complete-source provider's partial scan must never claim a successful
            // full run. Already committed items remain resumable on the next attempt.
            var successful = provider is ICompleteExternalJobProvider ? canReconcile : skipped == 0 && failed == 0 &&
                !(snapshot?.EligibilityReasonCounts?.Keys.Any(x => x is "AmbiguousLocation" or "ContradictoryLocation" or "UnknownCountry") ?? false);
            source.LastSuccessfulRunAtUtc = successful ? now : previousSuccessfulRun;
            source.LastError = continuationPending ? previousError : successful ? null : "External job source scan was incomplete or had failed/skipped items.";
            source.ConsecutiveFailures = continuationPending ? previousFailures : successful ? 0 : previousFailures + 1;

            sources.Update(source);

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
                RunId = partialResult.RunId, ListingTotal = partialResult.ListingTotal, ListingPages = partialResult.ListingPages,
                RebaseCount = partialResult.RebaseCount,
                ObservedIdentities = partialResult.ObservedIdentities, DetailAttempted = partialResult.DetailAttempted,
                DetailSucceeded = partialResult.DetailSucceeded, DetailFailed = partialResult.DetailFailed,
                CheckpointCursor = partialResult.CheckpointCursor, LastCheckpointAtUtc = partialResult.LastCheckpointAtUtc,
                RemainingWork = partialResult.RemainingWork, DurationMilliseconds = runTimer.Elapsed.TotalMilliseconds,
                ContinuationPending = continuationPending,
                StopReason = successful ? "Complete" : continuationPending ? "ExecutionSliceComplete" :
                    listingDiagnostics is { ReasonCode: not "Complete" } ? listingDiagnostics.ReasonCode : "IncompleteOrUnsafeScan",
                ListingDiagnostics = listingDiagnostics,
                TotalReceived = observedJobs.Count + (snapshot?.Skipped ?? 0),
                    Parsed = observedJobs.Count,
                    EligibilityReasonCounts = snapshot?.EligibilityReasonCounts,
                    ScanComplete = providerSnapshotComplete,
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
                ValidationReasonCounts = validationReasons.Count == 0 ? null : validationReasons,
                SelectionReasonCounts = snapshot?.SelectionReasonCounts,
                ReconciliationSafe = canReconcile,
                ReconciliationBlockReasons = reconciliationBlockReasons,
                Succeeded = successful,
                Error = source.LastError
            };

            checkpoint?.StageCompletedResult(runResult);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (_logger is not null)
            {
                if (successful) LogSucceeded(_logger, source.Id, processed, runTimer.Elapsed.TotalMilliseconds);
                else if (!continuationPending) LogFailed(_logger, source.Id, "IncompleteOrUnsafeScan");
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

            }

            return runResult;
        }
        catch (JobSourceContinuationException exception)
        {
            unitOfWork.ResetAfterFailure();
            source.LastSuccessfulRunAtUtc = previousSuccessfulRun;
            source.LastError = previousError; source.ConsecutiveFailures = previousFailures;
            if (durable) partialResult = partialResult.WithDetailProgress(checkpoint!.Details);
            return partialResult with { Succeeded = false, ContinuationPending = true, StopReason = exception.ReasonCode, DurationMilliseconds = runTimer.Elapsed.TotalMilliseconds,
                ReconciliationSafe = false, ReconciliationBlockReasons = ["ExecutionInterrupted"] };
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

            var snapshotReason = exception is JobSourceSnapshotUnsafeException unsafeSnapshot ? unsafeSnapshot.ReasonCode :
                exception is InvalidDataException && exception.Data["WorkdayFailureReason"] is { } providerReason ? providerReason.ToString() : null;
            return partialResult with { Succeeded = false, Error = source.LastError,
                ListingDiagnostics = (exception as JobSourceSnapshotUnsafeException)?.ListingDiagnostics ?? listingDiagnostics,
                StopReason = snapshotReason ?? "InfrastructureFailure",
                ReconciliationSafe = false, ReconciliationBlockReasons = snapshotReason is not null
                    ? ["SnapshotUnsafe"] : ["ExecutionFailed"] };
        }
        finally
        {
            // Keep partial-run evidence even when the provider budget/cancellation
            // prevents the normal completion block from running.
            if (_logger is not null)
            {
                LogPersistenceSummary(_logger, source.Id, partialResult.Created, partialResult.Existing,
                    partialResult.IngestionRejected, partialResult.ProviderSkipped, partialResult.SelectionSkipped, partialResult.Failed);
                foreach (var (reason, count) in partialResult.ReasonCounts ?? new Dictionary<JobIngestionReasonCode, int>())
                    IngestionReason(_logger, source.Id, "Ingestion", reason.ToString(), count, null);
                foreach (var (reason, count) in partialResult.ValidationReasonCounts ?? new Dictionary<string, int>())
                    IngestionReason(_logger, source.Id, "Validation", reason, count, null);
                foreach (var (reason, count) in partialResult.SelectionReasonCounts ?? new Dictionary<string, int>())
                    IngestionReason(_logger, source.Id, "Selection", reason, count, null);
            }
            (categoryResolver as IJobSourceCategoryRunCache)?.EndRun();
            if (ingestionService is IBulkJobIngestionService bulk) await bulk.CompleteRunAsync();
            if (_logger is not null) Progress(_logger, source.Id, "Total duration (including cleanup)", 0, runTimer.Elapsed.TotalMilliseconds, null);
        }
    }

    private static async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(
        IExternalJobProvider provider, JobPortal.Domain.Entities.JobSource source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (provider is IBatchedExternalJobProvider batched)
        {
            await foreach (var batch in batched.FetchBatchesAsync(source, cancellationToken)) yield return batch;
        }
        else if (provider is ICompleteExternalJobProvider complete)
            yield return await complete.FetchSnapshotAsync(source, cancellationToken);
        else
            yield return new(await provider.FetchJobsAsync(source, cancellationToken), 0, false);
    }
}
