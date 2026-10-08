namespace JobPortal.Domain.Entities;

public enum JobSourceRunStatus { Queued = 0, Running = 1, Succeeded = 2, Failed = 3, Interrupted = 4 }

// Operational record, not a soft-deletable business entity. Lease mutations use atomic SQL.
public sealed class JobSourceRun
{
    public const int MaximumAttempts = 3;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobSourceId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public JobSourceRunStatus Status { get; set; }
    public DateTime QueuedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? InterruptedAtUtc { get; set; }
    public DateTime? HeartbeatAtUtc { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string Phase { get; set; } = "Queued";
    public string? ErrorCode { get; set; }
    public int Processed { get; set; }
    public int TotalReceived { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Closed { get; set; }
    public int Matched { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int Published { get; set; }
    public int NeedsReview { get; set; }
    public int QualityRejected { get; set; }
    public int PublishFailed { get; set; }
    public int AutoPublishDisabled { get; set; }
}
