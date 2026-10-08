using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

/// <summary>
/// Represents a configured external job source (ATS/career page) for aggregation.
/// </summary>
public sealed class JobSource : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// Base URL for the career page to scrape.
    /// </summary>
    public string CareerPageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Type of ATS system used by this source.
    /// </summary>
    public AtsType AtsType { get; set; }

    /// <summary>
    /// ATS-specific identifier or token (e.g., Greenhouse token, Lever ID).
    /// </summary>
    public string? AtsIdentifier { get; set; }

    /// <summary>
    /// Whether this source is currently active for aggregation.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Interval in minutes between aggregation runs for this source.
    /// </summary>
    public int ScanIntervalMinutes { get; set; } = 720; // Default: 12 hours

    /// <summary>
    /// UTC start timestamp of the last attempted aggregation run, durably saved
    /// before provider fetching. Does not imply completion or success.
    /// </summary>
    public DateTime? LastRunAtUtc { get; set; }

    /// <summary>
    /// UTC completion timestamp of the last complete successful aggregation run.
    /// </summary>
    public DateTime? LastSuccessfulRunAtUtc { get; set; }

    /// <summary>
    /// Sanitized outcome of the last completed failed run; cleared on success.
    /// An interrupted/cancelled attempt preserves this previous outcome.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Count of consecutive completed failures; reset on success. Normal host
    /// shutdown cancellation does not increment this counter.
    /// </summary>
    public int ConsecutiveFailures { get; set; }
}
