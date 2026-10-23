using System.Text.RegularExpressions;

namespace JobPortal.Application.Features.JobAggregation;

public static partial class SmartRecruitersSourceConfiguration
{
    public static ResolvedJobSourceConfiguration Resolve(string? url, string? identifier)
    {
        if (url is null || url.Contains('%') || url.Contains('\\') || url.Any(char.IsControl) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "careers.smartrecruiters.com" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return Invalid("Use the HTTPS SmartRecruiters company career root.");
        var start = url.Trim().IndexOf('/', "https://".Length);
        var path = start < 0 ? "" : url.Trim()[start..];
        var token = path.Trim('/');
        if (!Token().IsMatch(token) || path != "/" + token && path != "/" + token + "/")
            return Invalid("Company identifier must be one safe path segment.");
        if (identifier?.Any(char.IsControl) == true || !string.IsNullOrWhiteSpace(identifier) && identifier != token)
            return Invalid("Company identifier must match the career URL, including case.");
        return new(true, "CONFIGURATION_VALID", "Configuration validated.", token,
            new Uri($"https://api.smartrecruiters.com/v1/companies/{token}/postings?destination=PUBLIC&limit=100&offset=0"));
    }
    private static ResolvedJobSourceConfiguration Invalid(string message) => new(false, "SMARTRECRUITERS_CONFIGURATION_INVALID", message);
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,254}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
