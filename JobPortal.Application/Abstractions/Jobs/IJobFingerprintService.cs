namespace JobPortal.Application.Abstractions.Jobs;

/// <summary>
/// Service for generating deterministic fingerprints for job deduplication.
/// </summary>
public interface IJobFingerprintService
{
    /// <summary>
    /// Generates a SHA-256 fingerprint from job title, company, and location.
    /// </summary>
    /// <param name="title">Job title.</param>
    /// <param name="company">Company name.</param>
    /// <param name="location">Job location.</param>
    /// <returns>Lowercase hexadecimal SHA-256 string (64 characters).</returns>
    string GenerateFingerprint(string? title, string? company, string? location);
}
