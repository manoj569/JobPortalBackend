using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class Job : BaseEntity
{
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Responsibilities { get; set; }
    public string? Requirements { get; set; }
    public string? Benefits { get; set; }
    public string ApplicationUrl { get; set; } = string.Empty;
    // Derived lookup metadata. The actionable URL above remains unchanged.
    public string? CanonicalApplicationUrlHash { get; set; }
    public string? Location { get; set; }
    public decimal? MinimumSalary { get; set; }
    public decimal? MaximumSalary { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public EmploymentType EmploymentType { get; set; }
    public WorkplaceType WorkplaceType { get; set; }
    public ExperienceLevel ExperienceLevel { get; set; }
    public int? MinimumExperienceYears { get; set; }
    public int? MaximumExperienceYears { get; set; }
    public int? InternshipDurationMonths { get; set; }
    public bool IsFlexibleDuration { get; set; }
    public string? Department { get; set; }
    public string? RoleCategory { get; set; }
    public string? EducationRequirement { get; set; }
    public PostedByType? PostedByType { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Draft;
    public bool IsFeatured { get; set; }
    public bool IsHidden { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public ICollection<JobSkill> JobSkills { get; set; } = new List<JobSkill>();
    public ICollection<SavedJob> SavedByUsers { get; set; } = new List<SavedJob>();
    public ICollection<UserJobHistory> UserHistory { get; set; } = new List<UserJobHistory>();
    public ICollection<JobApplication> Applications { get; set; } = new List<JobApplication>();
    public JobRecruiterContact? RecruiterContact { get; set; }
    public JobReferral? Referral { get; set; }

    // Job Aggregation & Deduplication fields (Phase 1)
    public string? FingerprintHash { get; set; }
    public DateTime? FirstSeenAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
}
