using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class JobApplication : BaseEntity
{
    public JobApplicationStatus Status { get; set; } = JobApplicationStatus.Submitted;
    public ApplicationMethod ApplicationMethod { get; set; } = ApplicationMethod.Portal;
    public string? CoverLetter { get; set; }
    public string? ResumeStorageKey { get; set; }
    public string? ResumeFileName { get; set; }
    public string? ResumeContentType { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? WithdrawnAtUtc { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
    public ICollection<JobApplicationStatusHistory> StatusHistory { get; set; } =
        new List<JobApplicationStatusHistory>();
}
