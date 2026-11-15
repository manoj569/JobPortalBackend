using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum TalentSubmissionStatus { Submitted = 1, Viewed, Shortlisted, InterviewRequested, Rejected, Withdrawn }
public enum EmployerContactVerification { Unknown = 0, Candidate, Verified, Invalid, Risky, Unavailable }
public enum EmployerContactRole { Other = 0, Recruiter, TalentAcquisition, HumanResources, TechnicalRecruiter,
    HiringManager, EngineeringManager, ProductHiringManager, CampusRecruiter }
public enum TalentOutreachStatus { Draft = 1, Ready, Pending, Dispatching, Sent, Failed, Unknown, Cancelled }

public sealed class TalentShareLink : BaseEntity
{
    public Guid CandidateId { get; set; }
    public Guid GoalId { get; set; }
    public Guid? JobId { get; set; }
    public Guid? CompanyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Purpose { get; set; } = "EmployerReview";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? LastAccessedAtUtc { get; set; }
    public int AccessCount { get; set; }
}

public sealed class TalentSubmission : BaseEntity
{
    public Guid CandidateId { get; set; }
    public Guid JobId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid GoalId { get; set; }
    public Guid? ShareLinkId { get; set; }
    public TalentSubmissionStatus Status { get; set; } = TalentSubmissionStatus.Submitted;
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? ViewedAtUtc { get; set; }
    public DateTime? WithdrawnAtUtc { get; set; }
    public string? InterviewMessage { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public sealed class TalentSubmissionEvent : BaseEntity
{
    public Guid SubmissionId { get; set; }
    public Guid ActorId { get; set; }
    public TalentSubmissionStatus Status { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class EmployerContact : BaseEntity
{
    public Guid CompanyId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string ProfessionalEmail { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public EmployerContactRole Role { get; set; }
    public EmployerContactVerification Verification { get; set; }
    public string Source { get; set; } = "Administrator";
    public string? VerificationEvidence { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }
    public DateTime? DiscoveryExpiresAtUtc { get; set; }
    public bool IsSuppressed { get; set; }
}

public sealed class TalentOutreachDraft : BaseEntity
{
    public Guid CandidateId { get; set; }
    public Guid JobId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ShareLinkId { get; set; }
    public Guid? MailboxConnectionId { get; set; }
    public Guid? UsageReservationId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public TalentOutreachStatus Status { get; set; } = TalentOutreachStatus.Draft;
    public string SendIdempotencyKey { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public string? FailureEvidenceReference { get; set; }
    public DateTime? DispatchStartedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public sealed class TalentOutreachRecipient : BaseEntity
{
    public Guid DraftId { get; set; }
    public Guid ContactId { get; set; }
    public string EmailSnapshot { get; set; } = string.Empty;
}

public sealed class CandidateMailboxConnection : BaseEntity
{
    public Guid CandidateId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string ProviderAccountIdentifier { get; set; } = string.Empty;
    public string GrantedScopes { get; set; } = string.Empty;
    public string TokenReference { get; set; } = string.Empty;
    public DateTime ConnectedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
