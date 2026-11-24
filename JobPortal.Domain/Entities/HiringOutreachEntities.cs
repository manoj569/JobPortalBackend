using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class HiringOutreachSession : BaseEntity
{
    public Guid CandidateId { get; set; }
    public Guid JobId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RequestKey { get; set; }
    public string CompanyName { get; set; } = "";
    public string CompanyDomain { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string JobDescription { get; set; } = "";
    public string Location { get; set; } = "";
    public string Department { get; set; } = "";
    public string State { get; set; } = "Searching";
    public string Provider { get; set; } = "Hunter";
    public string UsageState { get; set; } = "NotReserved";
    public string AllowanceSource { get; set; } = "";
    public string? FailureCode { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
public sealed class HiringOutreachContact : BaseEntity
{
    public Guid SessionId { get; set; }
    public string FullName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Title { get; set; } = "";
    public string Department { get; set; } = "";
    public string Type { get; set; } = "Other";
    public string Verification { get; set; } = "unknown";
    public int Confidence { get; set; }
    public int Rank { get; set; }
    public string? LinkedInUrl { get; set; }
    public string ProviderReference { get; set; } = "";
    public DateTime VerifiedAtUtc { get; set; }
    public string Location { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Source { get; set; } = "";
    public int MatchScore { get; set; }
}
public sealed class HiringOutreachDiscoveryRequest : BaseEntity
{
    public Guid CandidateId { get; set; }
    public Guid JobId { get; set; }
    public Guid SessionId { get; set; }
    public Guid RequestKey { get; set; }
}
public sealed class HiringOutreachDraft : BaseEntity
{
    public Guid SessionId { get; set; }
    public Guid ResumeId { get; set; }
    public string RecipientIdsJson { get; set; } = "[]";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Source { get; set; } = "Deterministic";
}
public sealed class HiringOutreachSend : BaseEntity
{
    public Guid SessionId { get; set; }
    public Guid CandidateId { get; set; }
    public Guid JobId { get; set; }
    public Guid ContactId { get; set; }
    public Guid DraftId { get; set; }
    public Guid ResumeId { get; set; }
    public Guid RequestKey { get; set; }
    public string PayloadHash { get; set; } = "";
    public string EmailSnapshot { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string State { get; set; } = "Reserved";
    public string? ProviderMessageId { get; set; }
    public string? ProviderThreadId { get; set; }
    public string? FailureCode { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
public sealed class HiringMailboxSecret : BaseEntity
{
    public Guid MailboxId { get; set; }
    public string ProtectedRefreshToken { get; set; } = "";
    public DateTime? LastUsedAtUtc { get; set; }
}
public sealed class HiringMailboxAuthorization : BaseEntity
{
    public Guid CandidateId { get; set; }
    public string StateHash { get; set; } = "";
    public string BrowserHash { get; set; } = "";
    public string ProtectedVerifier { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
