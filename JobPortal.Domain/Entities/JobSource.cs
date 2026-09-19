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
    /// Timestamp of the last aggregation run (successful or not).
    /// </summary>
    public DateTime? LastRunAtUtc { get; set; }
    
    /// <summary>
    /// Timestamp of the last successful aggregation run.
    /// </summary>
    public DateTime? LastSuccessfulRunAtUtc { get; set; }
    
    /// <summary>
    /// Error message from the most recent failed run.
    /// </summary>
    public string? LastError { get; set; }
    
    /// <summary>
    /// Count of consecutive failed runs (for circuit breaker pattern).
    /// </summary>
    public int ConsecutiveFailures { get; set; }
}
