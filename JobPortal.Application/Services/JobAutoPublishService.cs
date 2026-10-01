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
    TimeProvider timeProvider) : IJobAutoPublishService
{
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
