using System.Text.RegularExpressions;

namespace JobPortal.Application.Features.JobAggregation;

// The career URL supplies the host and exact site path. No company lookup,
// region allowlist or provider-specific publication rule is involved.
public sealed partial record WorkdaySourceConfiguration(string Host, string Tenant, string CareerSite)
{
    public string Identifier => $"{Tenant}/{CareerSite}";
    public Uri Origin => new($"https://{Host}/");
    public Uri ApiRoot => new(Origin, $"wday/cxs/{Tenant}/{CareerSite}");
    public Uri ListingEndpoint => new(ApiRoot.AbsoluteUri + "/jobs");

    public static WorkdaySourceValidation Validate(string? careerUrl, string? identifier)
    {
        if (!Uri.TryCreate(careerUrl?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback)
            return Failure("WORKDAY_ENDPOINT_INVALID", "Use an HTTPS Workday career-site root without credentials, query, fragment or a nonstandard port.");
        var match = HostPattern().Match(uri.IdnHost);
        if (!match.Success)
            return Failure("WORKDAY_HOST_INVALID", "Host must match tenant.wd<number>.myworkdayjobs.com.");
        var host = uri.IdnHost.ToLowerInvariant();
        var tenant = match.Groups["tenant"].Value.ToLowerInvariant();
        // Inspect the original path before Uri can remove dot segments or decode
        // separators. Only literal, safe locale/site segments are accepted.
        var text = careerUrl!.Trim();
        var pathStart = text.IndexOf('/', text.IndexOf("://", StringComparison.Ordinal) + 3);
        var path = pathStart < 0 ? string.Empty : text[pathStart..];
        if (path is "" or "/")
            return Failure("WORKDAY_CAREER_SITE_MISSING", "A Workday career-site path is required.", host, tenant);
        var pathMatch = CareerPathPattern().Match(path);
        if (text.Contains('\\') || !pathMatch.Success)
            return Failure("WORKDAY_ENDPOINT_INVALID", "Use a site root or locale/site root with safe literal path segments.", host, tenant);
        var site = pathMatch.Groups["site"].Value;
        var configuration = new WorkdaySourceConfiguration(host, tenant, site);
        var value = identifier?.Trim();
        // Old/admin callers can supply an unambiguous tenant or site shorthand,
        // or omit redundant metadata. Persist the canonical tenant/site on save.
        if (!string.IsNullOrEmpty(value))
        {
            if (value.StartsWith('/'))
                return Failure("WORKDAY_TENANT_MISSING", "Workday identifier requires a tenant before the slash.", host, tenant, site);
            if (value.EndsWith('/'))
                return Failure("WORKDAY_CAREER_SITE_MISSING", "Workday identifier requires a career site after the slash.", host, tenant, site);
            if (!string.Equals(value, configuration.Identifier, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, tenant, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, site, StringComparison.OrdinalIgnoreCase))
                return Failure("WORKDAY_CONFIGURATION_CONFLICT", "Workday identifier must match the URL's tenant/site, tenant or career site.", host, tenant, site);
        }
        return new(configuration, "WORKDAY_CONFIGURATION_VALID", "Workday configuration validated.", host, tenant, site);
    }

    private static WorkdaySourceValidation Failure(string code, string message, string host = "", string tenant = "", string site = "") =>
        new(null, code, message, host, tenant, site);

    [GeneratedRegex(@"\A(?<tenant>[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)\.wd[0-9]+\.myworkdayjobs\.com\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();
    [GeneratedRegex(@"\A/(?:[a-z]{2}-[a-z]{2}/)?(?<site>[A-Za-z0-9][A-Za-z0-9_-]{0,127})/?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CareerPathPattern();
}

public sealed record WorkdaySourceValidation(WorkdaySourceConfiguration? Configuration, string ReasonCode, string Message,
    string Host, string Tenant, string CareerSite)
{
    public bool IsValid => Configuration is not null;
}
