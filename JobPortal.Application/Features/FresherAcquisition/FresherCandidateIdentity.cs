using System.Security.Cryptography;
using System.Text;

namespace JobPortal.Application.Features.FresherAcquisition;

public static class FresherCandidateIdentity
{
    public static string Create(Guid sourceId, FresherImportCandidate? candidate, bool website = false)
    {
        if (candidate is null) return Hash($"{sourceId:N}:invalid:null");
        var identity = !string.IsNullOrWhiteSpace(candidate.ExternalJobId) ? "external:" + Normalize(candidate.ExternalJobId)
            : website ? WebsiteFallback(candidate)
            : !string.IsNullOrWhiteSpace(candidate.CanonicalJobUrl) ? "canonical:" + NormalizeUrl(candidate.CanonicalJobUrl)
            : !string.IsNullOrWhiteSpace(candidate.ApplyUrl) ? "apply:" + NormalizeUrl(candidate.ApplyUrl)
            : "fallback:" + string.Join('|', Normalize(candidate.CompanyName), Normalize(candidate.Title),
                Normalize(candidate.LocationText ?? candidate.City ?? candidate.Country));
        return Hash($"{sourceId:N}:{identity}");
    }

    public static string WebsiteFallback(FresherImportCandidate candidate) => "website-fallback:" + Hash(
        string.Join('|', NormalizeUrl(candidate.DiscoveredUrl), Normalize(candidate.CompanyName), Normalize(candidate.Title), Normalize(candidate.LocationText)));

    public static string Fingerprint(IEnumerable<string> candidateIdentityKeys) =>
        Hash(string.Join('\n', candidateIdentityKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));

    public static string PayloadFingerprint(FresherBulkImportRequest request) =>
        Hash("payload-v2:" + request.SourceType.Trim().ToLowerInvariant() + "\n" + request.SourceName + "\n" +
            string.Join('\n', request.Jobs.Select(x => System.Text.Json.JsonSerializer.Serialize(x))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));

    public static string Normalize(string? value) => string.Join(' ',
        (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().ToUpperInvariant();

    private static string NormalizeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return Normalize(value);
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
