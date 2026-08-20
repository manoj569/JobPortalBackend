using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class CandidatePortfolio : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Slug { get; set; } = string.Empty;
    public string NormalizedSlug { get; set; } = string.Empty;
    public CandidatePortfolioStatus Status { get; set; } = CandidatePortfolioStatus.Draft;
    public CandidatePortfolioTemplate Template { get; set; } = CandidatePortfolioTemplate.Professional;
    public DateTime? PublishedAtUtc { get; set; }
    public ICollection<PortfolioSectionSetting> SectionSettings { get; set; } = [];
}

public sealed class PortfolioSectionSetting : BaseEntity
{
    public Guid PortfolioId { get; set; }
    public CandidatePortfolio Portfolio { get; set; } = null!;
    public PortfolioSectionType SectionType { get; set; }
    public bool IsVisible { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class CandidateExperience : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string JobTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Location { get; set; }
    public EmploymentType? EmploymentType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public string? Description { get; set; }
    public decimal? AnnualSalary { get; set; }
    public string SkillsUsedJson { get; set; } = "[]";
    public CandidateAvailability? NoticePeriod { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class CandidateEducation : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Qualification { get; set; } = string.Empty;
    public string Institution { get; set; } = string.Empty;
    public string? FieldOfStudy { get; set; }
    public int? StartYear { get; set; }
    public int? EndYear { get; set; }
    public string? Grade { get; set; }
    public string? Description { get; set; }
    public EducationCourseType? CourseType { get; set; }
    public bool IsCurrentlyStudying { get; set; }
    public string? GradingSystem { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class CandidateProfilePhoto : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class CandidateProject : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string Description { get; set; } = string.Empty;
    public string TechnologiesJson { get; set; } = "[]";
    public string? SourceUrl { get; set; }
    public string? LiveUrl { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class CandidateCertification : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Issuer { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public bool DoesNotExpire { get; set; }
    public string? CredentialId { get; set; }
    public string? CredentialUrl { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class CandidateProfessionalLink : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ProfessionalLinkType Type { get; set; }
    public string? Label { get; set; }
    public string Url { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public sealed class PortfolioCustomSection : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public ICollection<PortfolioCustomItem> Items { get; set; } = [];
}

public sealed class PortfolioCustomItem : BaseEntity
{
    public Guid SectionId { get; set; }
    public PortfolioCustomSection Section { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? Date { get; set; }
    public string? Url { get; set; }
    public int DisplayOrder { get; set; }
}
