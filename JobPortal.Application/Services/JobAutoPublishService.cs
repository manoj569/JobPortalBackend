using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;
using JobPortal.Domain.Enums;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Services;

public sealed class JobAutoPublishService(
    IJobRepository jobs,
    IJobQualityGate qualityGate,
    IJobService jobService,
    IOptions<JobAggregationOptions> options,
    TimeProvider timeProvider,
    IJobSourceRepository sources,
    IExternalJobCreationLock? publicationLock = null,
    IJobSourceRunCheckpoint? checkpoint = null) : IJobAutoPublishService, IJobAutoPublishBatchReview
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
        if (source is null || source.IsDeleted || !source.IsActive || !source.AutoPublishEnabled ||
            source.CompanyId != job.CompanyId || source.AtsType is not
                (AtsType.Greenhouse or AtsType.Lever or AtsType.Ashby or AtsType.SuccessFactors or AtsType.Workday))
            return new() { Outcome = JobAutoPublishOutcome.Disabled };
        if (job.Status == JobStatus.Published)
            return new() { Outcome = JobAutoPublishOutcome.AlreadyPublished };
        if (job.IsDeleted || job.IsHidden || job.Status == JobStatus.Archived || !job.SourceOwnsPublication)
            return new() { Outcome = JobAutoPublishOutcome.NeedsReview,
                Reasons = [JobQualityReasonCode.PublicationOwnershipUnverified] };

        var quality = qualityGate.Evaluate(job, timeProvider.GetUtcNow().UtcDateTime);
        if (!quality.IsEligible)
            return new() { Outcome = quality.Decision == JobQualityDecision.Rejected
                ? JobAutoPublishOutcome.Rejected : JobAutoPublishOutcome.NeedsReview, Reasons = quality.Reasons };
        // The existing import policy permits a missing-location draft. It is not evidence
        // of India eligibility for unattended publication.
        if (ImportedJobIntegrity.Eligibility(job).ReasonCode == "MissingLocationAccepted")
            return new() { Outcome = JobAutoPublishOutcome.NeedsReview, Reasons = [JobQualityReasonCode.MissingLocation] };
        return null;
    }
}
