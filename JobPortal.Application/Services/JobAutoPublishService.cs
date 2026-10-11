using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Services;

public sealed class JobAutoPublishService(
    IJobRepository jobs,
    IJobQualityGate qualityGate,
    IJobService jobService,
    IOptions<JobAggregationOptions> options,
    TimeProvider timeProvider,
    IExternalJobCreationLock? publicationLock = null) : IJobAutoPublishService, IJobAutoPublishBatchReview
{
    public async Task<IReadOnlyDictionary<Guid, JobAutoPublishResult>> ReviewBatchAsync(IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        _ = options; // Legacy flag no longer gates eligible imported jobs.
        cancellationToken.ThrowIfCancellationRequested();
        var results = new Dictionary<Guid, JobAutoPublishResult>();
        var snapshots = await jobs.FindAggregationReviewJobsAsync(jobIds, cancellationToken);
        if (snapshots is null) return results;
        foreach (var job in snapshots)
        {
            var quality = qualityGate.Evaluate(job, timeProvider.GetUtcNow().UtcDateTime);
            if (quality.Decision is JobQualityDecision.Rejected or JobQualityDecision.NeedsReview)
                results[job.Id] = new()
                {
                    Outcome = quality.Decision == JobQualityDecision.Rejected ? JobAutoPublishOutcome.Rejected : JobAutoPublishOutcome.NeedsReview,
                    Reasons = quality.Reasons
                };
        }
        return results;
    }

    public async Task<JobAutoPublishResult> TryPublishAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        _ = options; // Legacy flag no longer gates eligible imported jobs.
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
        if (job.Status == JobStatus.Published) return new() { Outcome = JobAutoPublishOutcome.AlreadyPublished };
        if (job.JobSourceId is null || job.IsHidden || job.Status == JobStatus.Archived)
            return new() { Outcome = JobAutoPublishOutcome.NotImported };

        var quality = qualityGate.Evaluate(
            job,
            timeProvider.GetUtcNow().UtcDateTime);

        if (quality.Decision == JobQualityDecision.Rejected)
        {
            return new JobAutoPublishResult
            {
                Outcome = JobAutoPublishOutcome.Rejected,
                Reasons = quality.Reasons
            };
        }

        if (quality.Decision == JobQualityDecision.NeedsReview)
        {
            return new JobAutoPublishResult
            {
                Outcome = JobAutoPublishOutcome.NeedsReview,
                Reasons = quality.Reasons
            };
        }

        // Never set Job.Status directly.
        // Existing JobService remains the final publication authority.
        await jobService.PublishAsync(
            jobId,
            cancellationToken);

        return new JobAutoPublishResult
        {
            Outcome = JobAutoPublishOutcome.Published
        };
    }
}
