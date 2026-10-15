using System.Text.RegularExpressions;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Domain.Common;

namespace JobPortal.Application.Features.FresherAcquisition;

// Only the authenticated acquisition DTO's employer field is considered website
// evidence. Application, discovery and canonical job URLs are never inferred.
public static partial class FresherCompanyIdentity
{
    private static readonly string[] NonEmployerDomains =
    ["google.com", "google.co.in", "linkedin.com", "indeed.com", "indeed.co.in", "naukri.com",
        "glassdoor.com", "glassdoor.co.in", "foundit.in", "monster.com", "shine.com", "simplyhired.com",
        "ziprecruiter.com", "jooble.org", "talent.com", "timesjobs.com", "freshersworld.com",
        "wellfound.com", "internshala.com", "serpapi.com", "greenhouse.io", "lever.co", "ashbyhq.com",
        "myworkdayjobs.com", "successfactors.com", "successfactors.eu", "taleo.net"];
    private static readonly HashSet<string> NonEmployerBrands = NonEmployerDomains
        .Select(x => x[..x.IndexOf('.', StringComparison.Ordinal)]).ToHashSet(StringComparer.Ordinal);

    public static string? ValidName(string? value)
    {
        if (value is null || value.Any(char.IsControl)) return null;
        var name = CompanyNameNormalizer.Display(value);
        if (name.Length is < 2 or > 160 || !PlainName().IsMatch(name) || !name.Any(char.IsLetter) ||
            name.Contains("www.", StringComparison.OrdinalIgnoreCase) || name.Contains("://", StringComparison.Ordinal)) return null;
        return CompanyNameNormalizer.Normalize(name) is "confidential" or "unknown" or "n/a" or "na" or
            "undisclosed" or "not disclosed" or "company" or "important company of the sector" ? null : name;
    }

    public static string? EmployerWebsite(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var website = value.Contains("://", StringComparison.Ordinal) ? value.Trim() : "https://" + value.Trim();
        if (!FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(website)) return null;
        var domain = CompanyDomainNormalizer.Normalize(website);
        if (domain is null || domain.Split('.').Any(NonEmployerBrands.Contains)) return null;
        // No query/path from a provider is stored as a company's website.
        return new Uri(website).GetLeftPart(UriPartial.Authority);
    }

    public static IEnumerable<string> Locks(FresherImportCandidate candidate)
    {
        if (ValidName(candidate.CompanyName) is { } name)
            yield return "acquisition-company-name:" + CompanyNameNormalizer.Normalize(name);
        if (CompanyDomainNormalizer.Normalize(EmployerWebsite(candidate.CompanyWebsiteUrl)) is { } domain)
            yield return "acquisition-company-domain:" + domain;
    }

    [GeneratedRegex(@"^[\p{L}\p{M}\p{N}][\p{L}\p{M}\p{N}\p{Zs}.,&'’()\-+/@]*$")]
    private static partial Regex PlainName();
}
