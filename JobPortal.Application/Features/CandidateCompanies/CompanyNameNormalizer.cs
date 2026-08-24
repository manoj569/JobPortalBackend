using System.Text.RegularExpressions;

namespace JobPortal.Application.Features.CandidateCompanies;

public static partial class CompanyNameNormalizer
{
    public static string Display(string value) => Whitespace().Replace(value.Trim(), " ");
    public static string Normalize(string value) => Display(value).ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
