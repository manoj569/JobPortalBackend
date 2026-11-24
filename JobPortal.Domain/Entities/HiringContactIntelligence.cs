using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

// Licensed professional intelligence only. Never references a candidate or their messages/resume.
public sealed class HiringContactIntelligence : BaseEntity
{
    public Guid CompanyId { get; set; }
    public string CompanyDomain { get; set; } = "";
    public string EmailHash { get; set; } = "";
    public string ProtectedContact { get; set; } = "";
    public string Title { get; set; } = "";
    public string Department { get; set; } = "";
    public string Location { get; set; } = "";
    public string JobFamily { get; set; } = "";
    public string ContactType { get; set; } = "";
    public string Verification { get; set; } = "valid";
    public int Confidence { get; set; }
    public string Provider { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime FirstDiscoveredAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime LastVerifiedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int SuccessfulReuseCount { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Revision { get; set; } = Guid.NewGuid();
}

// A durable expiring claim, not a transaction spanning a provider request.
public sealed class HiringContactDiscoveryLease : BaseEntity
{
    public string ScopeHash { get; set; } = "";
    public Guid OwnerSessionId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
