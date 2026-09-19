namespace JobPortal.Application.Abstractions.Jobs;

/// <summary>
/// Provides deterministic URL canonicalization for deduplication comparison.
/// </summary>
public interface IUrlCanonicalizer
{
    /// <summary>
    /// Canonicalizes a URL for deterministic comparison.
    /// Returns null for null/empty/malformed inputs without throwing.
    /// </summary>
    string? Canonicalize(string? url);
}

/// <summary>
/// Implements deterministic URL canonicalization.
/// Rules:
/// - null/empty safe
/// - trim whitespace
/// - absolute URLs parsed with Uri
/// - lowercase scheme
/// - lowercase host
/// - remove default port 80 for HTTP
/// - remove default port 443 for HTTPS
/// - remove fragment
/// - normalize trailing slash consistently
/// - preserve path casing
/// - preserve meaningful query parameters
/// - remove ONLY known tracking parameters (case-insensitive):
///     utm_source, utm_medium, utm_campaign, utm_term, utm_content, utm_id, gclid, fbclid
/// - deterministically sort remaining query parameters
/// - malformed/non-absolute input handled deterministically without throwing
/// </summary>
public sealed class UrlCanonicalizer : IUrlCanonicalizer
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source",
        "utm_medium",
        "utm_campaign",
        "utm_term",
        "utm_content",
        "utm_id",
        "gclid",
        "fbclid"
    };

    public string? Canonicalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // External URLs are unbounded input; do not retain them in a static cache.
        return CanonicalizeInternal(url.Trim());
    }

    private static string? CanonicalizeInternal(string url)
    {
        try
        {
            // Try to parse as absolute URI
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Malformed/non-absolute: return normalized original as fallback
                // This ensures deterministic behavior without throwing
                return NormalizeNonAbsoluteUrl(url);
            }

            // Lowercase scheme
            var scheme = uri.Scheme.ToLowerInvariant();

            // Lowercase host
            var host = uri.Host.ToLowerInvariant();

            // Handle port - remove default ports
            int? port = uri.Port;
            if (port == 80 && scheme == "http")
                port = null;
            else if (port == 443 && scheme == "https")
                port = null;

            // Build authority
            var authority = port.HasValue ? $"{host}:{port}" : host;

            // Normalize path - ensure consistent trailing slash handling
            var path = uri.AbsolutePath;
            // Keep path as-is (preserve casing), but normalize empty path to "/"
            if (string.IsNullOrEmpty(path))
                path = "/";
            else if (path.Length > 1)
                path = path.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";

            // Process query parameters
            var queryString = uri.Query;
            var normalizedQuery = NormalizeQueryString(queryString);

            // Remove fragment (uri.Fragment is excluded)

            // Build canonical URL
            var canonicalUrl = $"{scheme}://{authority}{path}{normalizedQuery}";

            return canonicalUrl;
        }
        catch
        {
            // Any parsing error: return null for deterministic handling
            return null;
        }
    }

    private static string NormalizeQueryString(string queryString)
    {
        if (string.IsNullOrEmpty(queryString) || queryString == "?")
            return string.Empty;

        // Remove leading '?'
        var query = queryString.Length > 0 && queryString[0] == '?' ? queryString.Substring(1) : queryString;

        if (string.IsNullOrEmpty(query))
            return string.Empty;

        // Parse query parameters
        var parameters = new List<(string Key, string Value, bool HasEquals)>();
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            if (string.IsNullOrEmpty(pair))
                continue;

            var eqIndex = pair.IndexOf('=');
            string name, value;

            if (eqIndex >= 0)
            {
                name = pair[..eqIndex];
                value = pair[(eqIndex + 1)..];
            }
            else
            {
                name = pair;
                value = string.Empty;
            }

            // Decode name for comparison
            var decodedName = Uri.UnescapeDataString(name);

            // Skip tracking parameters (case-insensitive)
            if (TrackingParameters.Contains(decodedName))
                continue;

            // Unknown query names can be case-sensitive and repeated. Preserve
            // multiplicity and duplicate ordering instead of last-value-wins.
            parameters.Add((decodedName, Uri.UnescapeDataString(value), eqIndex >= 0));
        }

        if (parameters.Count == 0)
            return string.Empty;

        // Stable ordinal ordering preserves unknown key casing and duplicate order.
        var sortedParams = parameters.OrderBy(p => p.Key, StringComparer.Ordinal);

        // Build normalized query string
        var sb = new System.Text.StringBuilder();
        foreach (var param in sortedParams)
        {
            if (sb.Length > 0)
                sb.Append('&');

            // Encode name and value properly
            var encodedName = Uri.EscapeDataString(param.Key);
            var encodedValue = Uri.EscapeDataString(param.Value);
            sb.Append(encodedName);
            if (param.HasEquals) sb.Append('=').Append(encodedValue);
        }

        return "?" + sb.ToString();
    }

    private static string NormalizeNonAbsoluteUrl(string url)
    {
        // For non-absolute/malformed URLs, apply basic normalization:
        // - trim whitespace
        // - return as-is otherwise for deterministic behavior
        return url.Trim();
    }
}
