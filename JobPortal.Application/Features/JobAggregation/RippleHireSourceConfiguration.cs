using System.Text.RegularExpressions;

namespace JobPortal.Application.Features.JobAggregation;

public static partial class RippleHireSourceConfiguration
{
    public static bool IsPublicDiscoveryUrl(string? value, out Uri? uri)
    {
        uri = null;
        return value is { Length: > 0 and <= 2048 } && Uri.TryCreate(value, UriKind.Absolute, out uri) &&
        uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback && uri.Host.Contains('.') &&
        !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
        !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains('\\');
    }

    public static bool IsTenant(Uri uri) => TenantHost().IsMatch(uri.Host);

    public static string Normalize(Uri uri)
    {
        var builder = new UriBuilder(uri) { Fragment = "" };
        var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        builder.Query = string.Join("&", pairs.Order(StringComparer.Ordinal));
        return builder.Uri.AbsoluteUri;
    }

    public static ResolvedJobSourceConfiguration Resolve(string? url, string? identifier)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !TenantHost().IsMatch(uri.Host) ||
            uri.AbsolutePath != "/candidate/" || url!.Contains('\\') || url.Contains('%'))
            return new(false, "RIPPLEHIRE_SOURCE_UNSUPPORTED", "Use a public HTTPS https://{tenant}.ripplehire.com/candidate/?token={public-career-token} URL without a job fragment. Legacy /ripplehire/ and custom domains are unsupported.");
        var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2)).ToArray();
        if (pairs.Any(x => x.Length != 2 || x[0] is not ("token" or "source" or "lang") || !SafeValue().IsMatch(x[1])) ||
            pairs.GroupBy(x => x[0]).Any(x => x.Count() != 1) || pairs.Count(x => x[0] == "token") != 1 ||
            pairs.Any(x => x[0] == "lang" && x[1] != "en"))
            return new(false, "RIPPLEHIRE_CONFIGURATION_INVALID", "Provide one public career token; optional source and lang=en only. Never use a private candidate link.");
        var token = pairs.Single(x => x[0] == "token")[1];
        if (!string.IsNullOrWhiteSpace(identifier) && identifier.Trim() != token)
            return new(false, "RIPPLEHIRE_CONFIGURATION_CONFLICT", "AtsIdentifier must match the public token in CareerPageUrl, or be omitted.");
        return new(true, "CONFIGURATION_VALID", "RippleHire public career configuration validated.", token, new Uri(uri, "candidatejobsearch"));
    }

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]{0,62}\.ripplehire\.com\z", RegexOptions.CultureInvariant)]
    private static partial Regex TenantHost();
    [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,255}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeValue();
}
