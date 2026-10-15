using System.Text.RegularExpressions;

namespace JobPortal.Application.Features.JobAggregation;

/// <summary>Canonical, validated parts of a public Workday career-site root.</summary>
public sealed partial record WorkdaySourceConfiguration(
    string Host,
    string Tenant,
    string Shard,
    string? Locale,
    string CareerSite,
    string CanonicalCareerPageUrl)
{
    public string Identifier => $"{Tenant}/{CareerSite}";
    public Uri Origin => new($"https://{Host}/");
    public Uri ApiRoot => new(Origin, $"wday/cxs/{Tenant}/{Uri.EscapeDataString(CareerSite)}");
    public Uri ListingEndpoint => new(ApiRoot.AbsoluteUri + "/jobs");

    public static WorkdaySourceValidation Validate(string? careerUrl, string? identifier)
    {
        var parsed = Parse(careerUrl);
        if (!parsed.IsValid) return parsed;
        var configuration = parsed.Configuration!;
        var value = identifier?.Trim();
        if (string.IsNullOrEmpty(value))
            return new(configuration, "WORKDAY_CONFIGURATION_VALID", "Workday configuration validated.",
                configuration.Host, configuration.Tenant, configuration.CareerSite);

        // Accept a public site-root URL in the identifier field as well as legacy
        // tenant/site, tenant-only, and site-only inputs. URLs are always normalized
        // to the same canonical tenant/site identity before persistence.
        if (LooksLikeUrl(value))
        {
            var identifierRoot = Parse(value);
            if (!identifierRoot.IsValid)
                return Failure("WORKDAY_IDENTIFIER_INVALID", "Workday identifier must be a valid public HTTPS career-site root.",
                    configuration.Host, configuration.Tenant, configuration.CareerSite);
            var identifierConfiguration = identifierRoot.Configuration!;
            if (!string.Equals(identifierConfiguration.Host, configuration.Host, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(identifierConfiguration.CareerSite, configuration.CareerSite, StringComparison.OrdinalIgnoreCase))
                return Failure("WORKDAY_CONFIGURATION_CONFLICT", "Workday identifier URL must match the career-page host and site.",
                    configuration.Host, configuration.Tenant, configuration.CareerSite);
        }
        else if (value.StartsWith('/'))
        {
            return Failure("WORKDAY_TENANT_MISSING", "Workday identifier requires a tenant before the slash.",
                configuration.Host, configuration.Tenant, configuration.CareerSite);
        }
        else if (value.EndsWith('/'))
        {
            return Failure("WORKDAY_CAREER_SITE_MISSING", "Workday identifier requires a career site after the slash.",
                configuration.Host, configuration.Tenant, configuration.CareerSite);
        }
        else if (!string.Equals(value, configuration.Identifier, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(value, configuration.Tenant, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(value, configuration.CareerSite, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("WORKDAY_CONFIGURATION_CONFLICT", "Workday identifier must match the URL's tenant/site, tenant or career site.",
                configuration.Host, configuration.Tenant, configuration.CareerSite);
        }

        return new(configuration, "WORKDAY_CONFIGURATION_VALID", "Workday configuration validated.",
            configuration.Host, configuration.Tenant, configuration.CareerSite);
    }

    /// <summary>Parses only public Workday site roots; no HTTP request is made.</summary>
    public static WorkdaySourceValidation Parse(string? careerUrl)
    {
        var text = careerUrl?.Trim();
        if (string.IsNullOrEmpty(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || text.Contains('?') || text.Contains('#') ||
            uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback)
            return Failure("WORKDAY_ENDPOINT_INVALID", "Use an HTTPS Workday career-site root without credentials, query, fragment or a nonstandard port.");

        var match = HostPattern().Match(uri.IdnHost);
        if (!match.Success || match.Groups["shardNumber"].Value.TrimStart('0').Length == 0)
            return Failure("WORKDAY_HOST_INVALID", "Host must match tenant.wd<number>.myworkdayjobs.com with a positive shard number.");

        var host = uri.IdnHost.ToLowerInvariant();
        var tenant = match.Groups["tenant"].Value.ToLowerInvariant();
        var shard = "wd" + match.Groups["shardNumber"].Value;

        // Read the original path before Uri normalizes dot segments or decodes
        // separators. Percent-encoded or backslash paths are never accepted.
        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        var authorityStart = schemeEnd + 3;
        var pathStart = text.IndexOf('/', authorityStart);
        var path = pathStart < 0 ? string.Empty : text[pathStart..];
        if (path is "" or "/")
            return Failure("WORKDAY_CAREER_SITE_MISSING", "A Workday career-site path is required.", host, tenant);
        if (text.Contains('\\') || path.Contains('%'))
            return Failure("WORKDAY_ENDPOINT_INVALID", "Use a site root or locale/site root with safe literal path segments.", host, tenant);

        var pathMatch = CareerPathPattern().Match(path);
        if (!pathMatch.Success)
            return Failure("WORKDAY_ENDPOINT_INVALID", "Use a site root or locale/site root with safe literal path segments.", host, tenant);

        var locale = pathMatch.Groups["locale"].Success ? pathMatch.Groups["locale"].Value : null;
        var site = pathMatch.Groups["site"].Value;
        if (site.Equals("job", StringComparison.OrdinalIgnoreCase) ||
            site.Equals("search", StringComparison.OrdinalIgnoreCase))
            return Failure("WORKDAY_ENDPOINT_INVALID", "Workday job and search routes are not career-site roots.", host, tenant, site);
        var canonicalPath = locale is null ? site : $"{locale}/{site}";
        var configuration = new WorkdaySourceConfiguration(host, tenant, shard, locale, site,
            $"https://{host}/{canonicalPath}");
        return new(configuration, "WORKDAY_CONFIGURATION_VALID", "Workday configuration validated.", host, tenant, site);
    }

    private static bool LooksLikeUrl(string value) => value.Contains("://", StringComparison.Ordinal);

    private static WorkdaySourceValidation Failure(string code, string message, string host = "", string tenant = "", string site = "") =>
        new(null, code, message, host, tenant, site);

    [GeneratedRegex(@"\A(?<tenant>[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)\.(?<shard>wd(?<shardNumber>[0-9]+))\.myworkdayjobs\.com\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    [GeneratedRegex(@"\A/(?:((?<locale>[A-Za-z]{2,3}-[A-Za-z]{2}|[A-Za-z]{2,3}-[0-9]{3})/))?(?<site>[A-Za-z0-9][A-Za-z0-9_-]{0,127})/?\z", RegexOptions.CultureInvariant)]
    private static partial Regex CareerPathPattern();
}

public sealed record WorkdaySourceValidation(WorkdaySourceConfiguration? Configuration, string ReasonCode, string Message,
    string Host, string Tenant, string CareerSite)
{
    public bool IsValid => Configuration is not null;
}
