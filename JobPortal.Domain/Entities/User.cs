using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? NormalizedPhoneNumber { get; set; }
    public DateTime? TermsAndPrivacyAcceptedAtUtc { get; set; }
    public string? TermsAndPrivacyVersion { get; set; }
    public bool PhoneConfirmed { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? Headline { get; set; }
    public string? Bio { get; set; }
    public string? Location { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? PortfolioUrl { get; set; }
    public string SkillsJson { get; set; } = "[]";
    public string EducationJson { get; set; } = "[]";
    public string ExperienceJson { get; set; } = "[]";
    public string PreferredJobTypesJson { get; set; } = "[]";
    public CareerStage? CareerStage { get; set; }
    public string DesiredOpportunitiesJson { get; set; } = "[]";
    public string WorkPreferencesJson { get; set; } = "[]";
    public string? College { get; set; }
    public string? Degree { get; set; }
    public int? GraduationYear { get; set; }
    public decimal? YearsOfExperience { get; set; }
    public DateTime? OnboardingCompletedAtUtc { get; set; }
    public string? ResumeStorageKey { get; set; }
    public string? ResumeFileName { get; set; }
    public string? ResumeContentType { get; set; }
    public long? ResumeSizeBytes { get; set; }
    public DateTime? ResumeUploadedAtUtc { get; set; }
    public CandidateResumeProfile? ResumeProfile { get; set; }
    public ICollection<CandidateSkill> CandidateSkills { get; set; } = new List<CandidateSkill>();
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public bool EmailConfirmed { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAtUtc { get; set; }
    public string? EmailVerificationTokenHash { get; set; }
    public DateTime? EmailVerificationTokenExpiresAtUtc { get; set; }
    public DateTime? EmailVerificationSentAtUtc { get; set; }
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Company> OwnedCompanies { get; set; } = new List<Company>();
    public ICollection<SavedJob> SavedJobs { get; set; } = new List<SavedJob>();
    public ICollection<UserJobHistory> JobHistory { get; set; } = new List<UserJobHistory>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<Setting> Settings { get; set; } = new List<Setting>();
    public ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();
    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();
}
