using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Jobs;

/// <summary>
/// Result of a job deduplication check.
/// </summary>
public sealed class DeduplicationResult
{
    /// <summary>
    /// Whether a duplicate was found.
    /// </summary>
    public bool IsDuplicate { get; init; }

    /// <summary>
    /// The matched job if a duplicate was found.
    /// </summary>
    public Job? MatchedJob { get; init; }

    /// <summary>
    /// The type of match detected.
    /// </summary>
    public MatchType MatchTypeEnum { get; init; }

    /// <summary>
    /// Similarity score for fuzzy matches (0.0 to 1.0).
    /// </summary>
    public double? SimilarityScore { get; init; }

    /// <summary>
    /// Creates a result indicating no duplicate was found.
    /// </summary>
    public static DeduplicationResult NoMatch() => new()
    {
        IsDuplicate = false,
        MatchedJob = null,
        MatchTypeEnum = MatchType.None,
        SimilarityScore = null
    };

    /// <summary>
    /// Creates a result indicating a duplicate was found by source URL.
    /// </summary>
    public static DeduplicationResult SourceUrlMatch(Job job) => new()
    {
        IsDuplicate = true,
        MatchedJob = job,
        MatchTypeEnum = MatchType.SourceUrl,
        SimilarityScore = null
    };

    /// <summary>
    /// Creates a result indicating a duplicate was found by fingerprint.
    /// </summary>
    public static DeduplicationResult FingerprintMatch(Job job) => new()
    {
        IsDuplicate = true,
        MatchedJob = job,
        MatchTypeEnum = MatchType.Fingerprint,
        SimilarityScore = null
    };

    /// <summary>
    /// Creates a result indicating a duplicate was found by fuzzy matching.
    /// </summary>
    public static DeduplicationResult FuzzyMatch(Job job, double score) => new()
    {
        IsDuplicate = true,
        MatchedJob = job,
        MatchTypeEnum = MatchType.Fuzzy,
        SimilarityScore = score
    };
}

/// <summary>
/// Type of duplicate match detected.
/// </summary>
public enum MatchType
{
    None = 0,
    SourceUrl = 1,
    Fingerprint = 2,
    Fuzzy = 3
}

/// <summary>
/// Service for detecting duplicate jobs.
/// </summary>
public interface IJobDeduplicationService
{
    /// <summary>
    /// Checks for duplicate jobs using a tiered approach:
    /// 1. Canonicalized ExternalUrl exact match
    /// 2. FingerprintHash exact match
    /// 3. Fuzzy Title + Company + Location comparison (same-company scope)
    /// </summary>
    /// <param name="title">Job title.</param>
    /// <param name="companyName">Company name.</param>
    /// <param name="location">Job location.</param>
    /// <param name="externalUrl">Source/external URL (optional).</param>
    /// <param name="companyId">Company ID if known (for scoping fuzzy search).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deduplication result with match details.</returns>
    Task<DeduplicationResult> FindDuplicateAsync(
        string title,
        string companyName,
        string? location,
        string? externalUrl,
        Guid? companyId,
        CancellationToken cancellationToken = default);
}
