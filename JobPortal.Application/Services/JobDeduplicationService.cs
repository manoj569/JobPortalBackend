using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Services;

/// <summary>
/// Implements job deduplication using a tiered detection approach.
/// </summary>
public sealed class JobDeduplicationService : IJobDeduplicationService
{
    private readonly IJobRepository _jobRepository;
    private readonly IJobFingerprintService _fingerprintService;
    private readonly IUrlCanonicalizer _urlCanonicalizer;
    private const double FuzzyThreshold = 0.85;
    private const int MaxFuzzyCandidates = 100;

    public JobDeduplicationService(
        IJobRepository jobRepository,
        IJobFingerprintService fingerprintService,
        IUrlCanonicalizer urlCanonicalizer)
    {
        _jobRepository = jobRepository;
        _fingerprintService = fingerprintService;
        _urlCanonicalizer = urlCanonicalizer;
    }

    /// <summary>
    /// Checks for duplicate jobs using a tiered approach:
    /// 1. Canonicalized ExternalUrl exact match
    /// 2. FingerprintHash exact match
    /// 3. Fuzzy Title + Company + Location comparison (same-company scope)
    /// </summary>
    public async Task<DeduplicationResult> FindDuplicateAsync(
        string title,
        string companyName,
        string? location,
        string? externalUrl,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Check for exact ExternalUrl match using canonicalized URL (if URL provided)
        if (!string.IsNullOrWhiteSpace(externalUrl))
        {
            var canonicalUrl = _urlCanonicalizer.Canonicalize(externalUrl);
            if (!string.IsNullOrEmpty(canonicalUrl))
            {
                var byUrl = await _jobRepository.FindByExternalUrlAsync(canonicalUrl!, cancellationToken);
                if (byUrl != null)
                    return DeduplicationResult.SourceUrlMatch(byUrl);
            }
        }

        // Step 2: Check for exact FingerprintHash match
        var fingerprint = _fingerprintService.GenerateFingerprint(title, companyName, location);
        var byFingerprint = await _jobRepository.FindByFingerprintHashAsync(fingerprint, cancellationToken);
        if (byFingerprint != null)
            return DeduplicationResult.FingerprintMatch(byFingerprint);

        // Step 3: Fuzzy match within same company scope (if companyId known)
        if (companyId.HasValue)
        {
            var candidates = await _jobRepository.FindCandidatesForFuzzyMatchAsync(
                companyId.Value, title, location ?? string.Empty, MaxFuzzyCandidates, cancellationToken);

            if (candidates.Count > 0)
            {
                var bestMatch = FindBestFuzzyMatch(candidates, title, companyName, location);
                if (bestMatch.Job != null && bestMatch.Score >= FuzzyThreshold)
                    return DeduplicationResult.FuzzyMatch(bestMatch.Job, bestMatch.Score);
            }
        }

        return DeduplicationResult.NoMatch();
    }

    /// <summary>
    /// Finds the best fuzzy match from a list of candidates.
    /// Returns the candidate with the HIGHEST similarity score.
    /// </summary>
    private (Job? Job, double Score) FindBestFuzzyMatch(
        IReadOnlyList<Job> candidates, string title, string company, string? location)
    {
        Job? bestMatch = null;
        double bestScore = 0.0;

        foreach (var candidate in candidates)
        {
            var score = ComputeLevenshteinSimilarity(candidate.Title, candidate.Company?.Name, candidate.Location, title, company, location);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = candidate;
            }
        }

        return (bestMatch, bestScore);
    }

    /// <summary>
    /// Computes similarity between two jobs based on Title + Company + Location.
    /// Uses normalized Levenshtein similarity: similarity = 1 - distance / max(lengthA, lengthB)
    /// Rules:
    /// - both empty => 1.0
    /// - one empty => 0.0
    /// - threshold => 0.85
    /// </summary>
    private static double ComputeLevenshteinSimilarity(
        string title1, string? company1, string? loc1,
        string title2, string? company2, string? loc2)
    {
        // Build comparison value: Title + Company + Location
        var value1 = $"{title1} {company1 ?? ""} {loc1 ?? ""}".Trim();
        var value2 = $"{title2} {company2 ?? ""} {loc2 ?? ""}".Trim();

        return LevenshteinSimilarity(value1, value2);
    }

    /// <summary>
    /// Computes normalized Levenshtein similarity between two strings.
    /// similarity = 1 - distance / max(lengthA, lengthB)
    /// </summary>
    private static double LevenshteinSimilarity(string s1, string s2)
    {
        if (s1.Length == 0 && s2.Length == 0)
            return 1.0;

        if (s1.Length == 0 || s2.Length == 0)
            return 0.0;

        var distance = LevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);

        return 1.0 - (double)distance / maxLength;
    }

    /// <summary>
    /// Computes the Levenshtein distance between two strings.
    /// The distance is the minimum number of single-character edits
    /// (insertions, deletions, substitutions) required to change one string into the other.
    /// </summary>
    private static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;

        // Use two rows to optimize space
        var previousRow = new int[n + 1];
        var currentRow = new int[n + 1];

        // Initialize first row
        for (int j = 0; j <= n; j++)
            previousRow[j] = j;

        for (int i = 1; i <= m; i++)
        {
            currentRow[0] = i;

            for (int j = 1; j <= n; j++)
            {
                int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;

                currentRow[j] = Math.Min(
                    Math.Min(
                        currentRow[j - 1] + 1,      // insertion
                        previousRow[j] + 1),        // deletion
                    previousRow[j - 1] + cost);     // substitution
            }

            // Swap rows
            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[n];
    }
}
