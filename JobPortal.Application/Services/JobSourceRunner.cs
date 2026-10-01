using Microsoft.Extensions.Logging;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;

namespace JobPortal.Application.Services;

public sealed class JobSourceRunner(
    IJobSourceRepository sources,
    IEnumerable<IExternalJobProvider> providers,
    IJobIngestionService ingestionService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IJobSourceCategoryResolver categoryResolver,
    IExternalJobNormalizer normalizer,
    ILogger<JobSourceRunner>? logger = null,
    IJobAutoPublishService? autoPublishService = null) : IJobSourceRunner
{
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

        var provider = providers.FirstOrDefault(
            x => x.AtsType == source.AtsType);

        if (provider is null)
        {
            // Unsupported configurations are failed attempts too: automatic polling
            // must respect their ScanIntervalMinutes rather than retry every poll.
            source.LastRunAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            source.LastError = $"No provider is registered for ATS type '{source.AtsType}'.";
            source.ConsecutiveFailures++;
            sources.Update(source);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new JobSourceRunResult
            {
                JobSourceId = source.Id,
                Succeeded = false,
                Error = source.LastError
            };
        }

        var previousSuccessfulRun = source.LastSuccessfulRunAtUtc;
        var previousFailures = source.ConsecutiveFailures;

        try
        {
            var rawJobs = await provider.FetchJobsAsync(
                source,
                cancellationToken);

            var created = 0;
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

            foreach (var rawJob in rawJobs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var publicationAttempt = false;
                try
                {
                    var normalized = normalizer.Normalize(rawJob);
                    var categoryId = await categoryResolver.ResolveCategoryIdAsync(source, normalized, cancellationToken);
                    var result = await ingestionService.IngestAsync(
                        normalized with { CategoryId = categoryId },
                        cancellationToken);

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
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            source.LastRunAtUtc = now;
            source.LastSuccessfulRunAtUtc = now;
            source.LastError = null;
            source.ConsecutiveFailures = 0;

            sources.Update(source);
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
                TotalReceived = rawJobs.Count,
                Created = created,
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
                Succeeded = true
            };

            if (_logger is not null)
            {
                PublicationCompleted(_logger, source.Id, published, needsReview, qualityRejected, publishFailed, disabled, null);
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
            throw;
        }
        catch (Exception)
        {
            // Infrastructure/provider-wide failure: distinguishable from the
            // per-item failures above. The raw exception is logged structurally
            // but never persisted to source.LastError (which may leak secrets).
            unitOfWork.ResetAfterFailure();

            if (_logger is not null)
            {
                SourceRunFailed(_logger, source.Id, null);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            source.LastRunAtUtc = now;
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
    }
}
