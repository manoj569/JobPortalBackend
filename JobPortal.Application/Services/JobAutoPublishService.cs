using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Services;

public sealed class JobAutoPublishService(
    IJobRepository jobs,
    IJobQualityGate qualityGate,
    IJobService jobService,
    IOptions<JobAggregationOptions> options,
    TimeProvider timeProvider) : IJobAutoPublishService, IJobAutoPublishBatchReview
{
    public async Task<IReadOnlyDictionary<Guid, JobAutoPublishResult>> ReviewBatchAsync(IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new Dictionary<Guid, JobAutoPublishResult>();
        if (!options.Value.AutoPublishEnabled)
        {
            foreach (var id in jobIds) results[id] = new() { Outcome = JobAutoPublishOutcome.Disabled };
            return results;
        }
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
        cancellationToken.ThrowIfCancellationRequested();

        // Fail closed. Automatic publishing must be explicitly enabled.
        if (!options.Value.AutoPublishEnabled)
        {
            return new JobAutoPublishResult
            {
                Outcome = JobAutoPublishOutcome.Disabled
            };
        }

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
