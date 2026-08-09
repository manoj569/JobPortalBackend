using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class JobDiscoveryRun : BaseEntity
{
    public string Trigger { get; set; } = "Scheduled";
    public string Status { get; set; } = "Running";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int CandidateCount { get; set; }
    public int DuplicateCount { get; set; }
    public int ImportedCount { get; set; }
    public string? ErrorSummary { get; set; }
    public ICollection<JobDiscoveryItem> Items { get; set; } = new List<JobDiscoveryItem>();
}

public sealed class JobDiscoveryItem : BaseEntity
{
    public Guid RunId { get; set; }
    public JobDiscoveryRun Run { get; set; } = null!;
    public string Provider { get; set; } = string.Empty;
    public string SourceJobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ApplicationUrl { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? EmploymentType { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string Status { get; set; } = "Candidate";
    public string? DuplicateReason { get; set; }
    public Guid? ExistingJobId { get; set; }
    public Guid? ImportedJobId { get; set; }
}
