using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;
using JobPortal.Domain.Enums;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace JobPortal.Application.Services;

public sealed partial class JobAutoPublishService(
    IJobRepository jobs,
    IJobQualityGate qualityGate,
    IJobService jobService,
    IOptions<JobAggregationOptions> options,
    TimeProvider timeProvider,
    IJobSourceRepository sources,
    IExternalJobCreationLock? publicationLock = null,
    IJobSourceRunCheckpoint? checkpoint = null,
    ILogger<JobAutoPublishService>? logger = null) : IJobAutoPublishService, IJobAutoPublishBatchReview
{
    public async Task<IReadOnlyDictionary<Guid, JobAutoPublishResult>> ReviewBatchAsync(IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        _ = options; // Source configuration is the authority, not the legacy global flag.
        cancellationToken.ThrowIfCancellationRequested();
        var results = new Dictionary<Guid, JobAutoPublishResult>();
        var snapshots = await jobs.FindAggregationReviewJobsAsync(jobIds, cancellationToken);
        if (snapshots is null) return results;
        foreach (var job in snapshots)
        {
            var decision = await EvaluateAsync(job, cancellationToken);
            if (decision is not null) results[job.Id] = decision;
        }
        return results;
    }

    public async Task<JobAutoPublishResult> TryPublishAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        _ = options; // Source configuration is the authority, not the legacy global flag.
        cancellationToken.ThrowIfCancellationRequested();

        var job = await jobs.GetByIdAsync(
            jobId,
            includeDeleted: false,
            cancellationToken);

        if (job is null)
        {
            return new JobAutoPublishResult
            {
                Outcome = JobAutoPublishOutcome.JobNotFound
            };
        }

        if (job.JobSourceId is null) return new() { Outcome = JobAutoPublishOutcome.NotImported };
        await using var publicationLease = publicationLock is null ? null : await publicationLock.AcquireAsync(
            new UrlCanonicalizer().Canonicalize(job.ApplicationUrl), new JobFingerprintService().GenerateFingerprint(job.Title, job.Company?.Name ?? job.CompanyId.ToString(), job.Location), cancellationToken);
        job = await jobs.GetFreshByIdAsync(jobId, cancellationToken);
        if (job is null) return new() { Outcome = JobAutoPublishOutcome.JobNotFound };
        var decision = await EvaluateAsync(job, cancellationToken);
        if (decision is not null) return decision;

        // Never set Job.Status directly.
        // Existing JobService remains the final publication authority.
        // The receipt joins the existing publication/audit SaveChanges transaction.
        checkpoint?.StagePublication(jobId, new JobAutoPublishResult { Outcome = JobAutoPublishOutcome.Published });
        await jobService.PublishAsync(
            jobId,
            cancellationToken);

        checkpoint?.ConfirmSaved();
        return new JobAutoPublishResult
        {
            Outcome = JobAutoPublishOutcome.Published
        };
    }

    private async Task<JobAutoPublishResult?> EvaluateAsync(Job job, CancellationToken token)
    {
        if (job.JobSourceId is not { } sourceId)
            return new() { Outcome = JobAutoPublishOutcome.NotImported };
        var source = await sources.GetByIdAsync(sourceId, token);
        if (source is null || source.IsDeleted || !source.IsActive)
            return Decision("SOURCE_INACTIVE", JobAutoPublishOutcome.Disabled);
        // Preserve the existing configuration-only integration's authorization block.
        // Every executable adapter otherwise inherits this same publication policy.
        if (source.CompanyId != job.CompanyId || source.AtsType == AtsType.OracleRecruitingCloud)
            return Decision("PUBLICATION_NOT_AUTHORIZED", JobAutoPublishOutcome.Disabled);
        if (!source.AutoPublishEnabled)
            return Decision("AUTO_PUBLISH_DISABLED", JobAutoPublishOutcome.Disabled);
        if (job.Status == JobStatus.Published)
            return Decision("ALREADY_PUBLISHED", JobAutoPublishOutcome.AlreadyPublished);
        // Only drafts can transition automatically. Closed/paused/expired states
        // require an explicit editorial decision, even if source ownership remains.
        if (job.IsDeleted || job.IsHidden || job.Status != JobStatus.Draft || !job.SourceOwnsPublication)
            return Decision(job.IsDeleted || job.IsHidden || job.Status != JobStatus.Draft
                ? "MODERATION_BLOCKED" : "MANUALLY_UNPUBLISHED_OR_OWNERSHIP_UNVERIFIED",
                JobAutoPublishOutcome.NeedsReview, [JobQualityReasonCode.PublicationOwnershipUnverified]);

        var quality = qualityGate.Evaluate(job, timeProvider.GetUtcNow().UtcDateTime);
        if (!quality.IsEligible)
            return Decision(quality.Decision == JobQualityDecision.Rejected ? "INVALID_JOB" : "INDIA_ELIGIBILITY_UNKNOWN",
                quality.Decision == JobQualityDecision.Rejected ? JobAutoPublishOutcome.Rejected : JobAutoPublishOutcome.NeedsReview,
                quality.Reasons);
        // The existing import policy permits a missing-location draft. It is not evidence
        // of India eligibility for unattended publication.
        if (ImportedJobIntegrity.Eligibility(job).ReasonCode == "MissingLocationAccepted")
            return Decision("INDIA_ELIGIBILITY_UNKNOWN", JobAutoPublishOutcome.NeedsReview, [JobQualityReasonCode.MissingLocation]);
        return null;

        JobAutoPublishResult Decision(string reason, JobAutoPublishOutcome outcome,
            IReadOnlyList<JobQualityReasonCode>? reasons = null)
        {
            if (logger is not null)
                LogDecision(logger, sourceId, source?.Company?.Name ?? string.Empty, source?.AtsType.ToString() ?? string.Empty,
                    source?.AutoPublishEnabled ?? false, job.Id, reason);
            return new() { Outcome = outcome, Reasons = reasons ?? [] };
        }
    }

    [LoggerMessage(EventId = 4390, Level = LogLevel.Information,
        Message = "JobPublicationDecision SourceId={SourceId} SourceName={SourceName} Provider={Provider} AutoPublishEnabled={AutoPublishEnabled} JobId={JobId} ReasonCode={ReasonCode}.")]
    private static partial void LogDecision(ILogger logger, Guid sourceId, string sourceName, string provider,
        bool autoPublishEnabled, Guid jobId, string reasonCode);
}
