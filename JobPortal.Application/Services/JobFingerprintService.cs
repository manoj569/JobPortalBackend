using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Abstractions.Jobs;

namespace JobPortal.Application.Services;

/// <summary>
/// Implements deterministic job fingerprinting using SHA-256.
/// </summary>
public class JobFingerprintService : IJobFingerprintService
{
    /// <summary>
    /// Generates a SHA-256 fingerprint from job title, company, and location.
    /// The fingerprint is deterministic: same input always produces the same output.
    /// </summary>
    public string GenerateFingerprint(string? title, string? company, string? location)
    {
        var normalizedTitle = NormalizeForFingerprint(title);
        var normalizedCompany = NormalizeForFingerprint(company);
        var normalizedLocation = NormalizeForFingerprint(location);

        // Use pipe separator which is unlikely to appear in normalized text
        var combined = $"{normalizedTitle}|{normalizedCompany}|{normalizedLocation}";
        
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hash = SHA256.HashData(bytes);
        
        // Convert to lowercase hexadecimal (64 characters)
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes a string for fingerprint generation.
    /// - Null-safe (returns empty string for null/whitespace)
    /// - Unicode-aware (NFC normalization)
    /// - Trims leading/trailing whitespace
    /// - Converts to lowercase invariant
    /// - Removes punctuation
    /// - Collapses consecutive whitespace to single space
    /// </summary>
    private static string NormalizeForFingerprint(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Trim and convert to lowercase invariant
        var result = input.Trim().ToLowerInvariant();

        // NFC Unicode normalization
        result = result.Normalize(NormalizationForm.FormC);

        // Remove punctuation and special characters (keep letters, digits, spaces)
        var sb = new StringBuilder(result.Length);
        foreach (var c in result)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                sb.Append(c);
        }
        result = sb.ToString();

        // Collapse consecutive whitespace to single space
        var parts = result.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        result = string.Join(" ", parts);

        return result;
    }
}
