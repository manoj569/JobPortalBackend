using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class AcquisitionImportRun : BaseEntity
{
    public Guid AcquisitionSourceId { get; set; }
    public AcquisitionSource AcquisitionSource { get; set; } = null!;
    public string BatchId { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Status { get; set; } = "Running";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ReceivedCount { get; set; }
    public int EligibleCount { get; set; }
    public int RejectedCount { get; set; }
    public int RejectedGeographyCount { get; set; }
    public int RejectedExperienceCount { get; set; }
    public int RejectedInvalidDataCount { get; set; }
    public int InvalidUrlCount { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int ExistingCount { get; set; }
    public int PublishedCount { get; set; }
    public int FailedCount { get; set; }
    public string? FailureSummary { get; set; }
    public ICollection<AcquisitionImportItem> Items { get; set; } = new List<AcquisitionImportItem>();
}
