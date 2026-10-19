namespace JobPortal.Domain.Entities;

// Source membership/absence evidence, not another job or ingestion platform.
public sealed class JobSourceVacancy
{
    public Guid JobSourceId { get; set; }
    public string ExternalJobId { get; set; } = string.Empty;
    public Guid JobId { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public int MissingCompleteSnapshots { get; set; }
    public Guid? LastSnapshotId { get; set; }
    public bool IsCurrent { get; set; } = true;
}
