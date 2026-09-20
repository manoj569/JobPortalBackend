using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public enum CareerProfessionalType { CurrentEmployee = 1, FormerEmployee, Recruiter, HiringManager, CareerCoach }
public enum ConsultantVerificationStatus { Pending = 1, Verified, Rejected, Suspended }
public enum ConsultantTagKind { Language = 1, Expertise }

public sealed class CareerConsultant : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }
    public string DisplayName { get; set; } = "";
    public string ProfessionalHeadline { get; set; } = "";
    public string Bio { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CurrentRole { get; set; } = "";
    public decimal YearsOfExperience { get; set; }
    public CareerProfessionalType ProfessionalType { get; set; }
    public string LinkedInUrl { get; set; } = "";
    public ConsultantVerificationStatus VerificationStatus { get; set; } = ConsultantVerificationStatus.Pending;
    public string? VerificationMethod { get; set; }
    public string? VerificationReason { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime TermsAcceptedAtUtc { get; set; }
    public string PolicyVersion { get; set; } = "";
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string? TimeZoneId { get; set; }
    public bool IsAcceptingBookings { get; set; }
    public ICollection<CareerConsultantTag> Tags { get; set; } = new List<CareerConsultantTag>();
    public ICollection<CareerConsultantService> Services { get; set; } = new List<CareerConsultantService>();
}

public sealed class CareerConsultantTag : BaseEntity
{
    public Guid ConsultantId { get; set; }
    public CareerConsultant Consultant { get; set; } = null!;
    public ConsultantTagKind Kind { get; set; }
    public string Value { get; set; } = "";
}

public sealed class CareerConsultantService : BaseEntity
{
    public Guid ConsultantId { get; set; }
    public CareerConsultant Consultant { get; set; } = null!;
    public string ServiceType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "";
    public bool IsActive { get; set; }
}
