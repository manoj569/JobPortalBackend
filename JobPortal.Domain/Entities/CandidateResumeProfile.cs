using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class CandidateResumeProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ResumeExtractionStatus ExtractionStatus { get; set; }
    public string SkillsJson { get; set; } = "[]";
    public string RoleKeywordsJson { get; set; } = "[]";
    public string EducationKeywordsJson { get; set; } = "[]";
    public string LocationsJson { get; set; } = "[]";
    public decimal? YearsOfExperience { get; set; }
    public string? ExtractionError { get; set; }
    public DateTime? ExtractedAtUtc { get; set; }
}
