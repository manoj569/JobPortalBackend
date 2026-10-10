namespace JobPortal.Application.Abstractions.Jobs;

public enum JobAutoPublishOutcome
{
    Disabled = 0,
    Published = 1,
    NeedsReview = 2,
    Rejected = 3,
    JobNotFound = 4
}

public sealed record JobAutoPublishResult
{
    public JobAutoPublishOutcome Outcome { get; init; }

    public IReadOnlyList<JobQualityReasonCode> Reasons { get; init; } =
        Array.Empty<JobQualityReasonCode>();

    public bool Published =>
        Outcome == JobAutoPublishOutcome.Published;
}

public interface IJobAutoPublishService
{
    Task<JobAutoPublishResult> TryPublishAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}

public interface IJobAutoPublishBatchReview
{
    // Non-publishing decisions only; eligible jobs still use the existing authority.
    Task<IReadOnlyDictionary<Guid, JobAutoPublishResult>> ReviewBatchAsync(IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default);
}
