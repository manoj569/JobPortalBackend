using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

/// <summary>Small operational receipt. Candidate descriptions are intentionally not retained.</summary>
public sealed class AcquisitionImportItem : BaseEntity
{
    public Guid AcquisitionImportRunId { get; set; }
    public AcquisitionImportRun Run { get; set; } = null!;
    public Guid AcquisitionSourceId { get; set; }
    public int ItemIndex { get; set; }
    public Guid? JobId { get; set; }
    public Job? Job { get; set; }
    public string CandidateIdentityKey { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? DiscoveredUrl { get; set; }
    public string? CanonicalUrl { get; set; }
    public string? ApplyUrl { get; set; }
    public string CompanyNameNormalized { get; set; } = string.Empty;
    public string TitleNormalized { get; set; } = string.Empty;
    public string LocationNormalized { get; set; } = string.Empty;
    public string EligibilityDecision { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string PublicationStatus { get; set; } = "NotRequested";
    public string? PublicationError { get; set; }
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }
    public DateTime? SourceExpiresAtUtc { get; set; }
}
