using System.Net;
using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.FresherAcquisition;

public enum FresherGeographyDecision { IndiaEligible, GlobalRemoteEligible, NonIndia, RemoteRestricted, EligibilityUnknown }
public enum FresherExperienceDecision { Eligible, Ineligible, Unknown }
public sealed record FresherEligibilityResult(
    FresherGeographyDecision Geography,
    FresherExperienceDecision Experience,
    string GeographyReasonCode,
    string ExperienceReasonCode)
{
    public bool Eligible => (Geography is FresherGeographyDecision.IndiaEligible or FresherGeographyDecision.GlobalRemoteEligible)
        && Experience == FresherExperienceDecision.Eligible;
}

/// <summary>Fresher acquisition policy. Kept separate from employer ATS eligibility.</summary>
public static partial class FresherAcquisitionEligibilityPolicy
{
    public static FresherEligibilityResult Evaluate(FresherCandidate candidate)
    {
        var geography = EvaluateGeography(candidate);
        var experience = EvaluateExperience(candidate);
        return new(geography.Decision, experience.Decision, geography.Code, experience.Code);
    }

    public static (FresherGeographyDecision Decision, string Code) EvaluateGeography(FresherCandidate candidate)
    {
        var evidence = string.Join(" ", candidate.LocationText, candidate.Description, candidate.City, candidate.State,
            candidate.Country, candidate.RemoteEligibility, candidate.WorkplaceType);
        var country = candidate.Country?.Trim();
        var explicitIndia = EqualsCountry(country, "India", "IN", "IND") || IndiaSignal().IsMatch(evidence);
        if (IndiaExcluded().IsMatch(evidence))
            return (FresherGeographyDecision.RemoteRestricted, "INDIA_EXPLICITLY_EXCLUDED");
        var restricted = Restriction().IsMatch(evidence);
        if (restricted)
        {
            if (explicitIndia && !ForeignOnlyRestriction().IsMatch(evidence))
                return (FresherGeographyDecision.IndiaEligible, "INDIA_LOCATION_ELIGIBLE");
            return (FresherGeographyDecision.RemoteRestricted, "REMOTE_COUNTRY_RESTRICTED");
        }
        if (explicitIndia) return (FresherGeographyDecision.IndiaEligible, "INDIA_LOCATION_ELIGIBLE");
        if (GlobalRemote().IsMatch(evidence) && !ContradictoryRemoteRestriction().IsMatch(evidence))
            return (FresherGeographyDecision.GlobalRemoteEligible, "GLOBAL_REMOTE_ELIGIBLE");
        if (ForeignCountry().IsMatch(evidence)) return (FresherGeographyDecision.NonIndia, "NON_INDIA_LOCATION");
        if (Remote().IsMatch(evidence)) return (FresherGeographyDecision.EligibilityUnknown, "REMOTE_ELIGIBILITY_UNKNOWN");
        return (FresherGeographyDecision.EligibilityUnknown, "GEOGRAPHY_EVIDENCE_UNKNOWN");
    }

    public static (FresherExperienceDecision Decision, string Code) EvaluateExperience(FresherCandidate candidate)
    {
        var text = string.Join(" ", candidate.Title, candidate.Description, candidate.ExperienceText);
        if (SeniorTitle().IsMatch(candidate.Title ?? string.Empty))
            return (FresherExperienceDecision.Ineligible, "SENIORITY_TITLE");

        var min = candidate.MinimumExperienceYears;
        var max = candidate.MaximumExperienceYears;
        var range = ExperienceRange().Match(candidate.ExperienceText ?? candidate.Description ?? string.Empty);
        if (range.Success)
        {
            if (int.TryParse(range.Groups[1].Value, out var parsedMin)) min ??= parsedMin;
            if (int.TryParse(range.Groups[2].Value, out var parsedMax)) max ??= parsedMax;
        }
        if (min is > 0)
            return (FresherExperienceDecision.Ineligible, "MANDATORY_MINIMUM_EXPERIENCE_POSITIVE");
        var requiredPositive = range.Success ? min is > 0 : PositiveMinimumText().IsMatch(text);
        if (requiredPositive && !PreferredExperience().IsMatch(text))
            return (FresherExperienceDecision.Ineligible, "MANDATORY_MINIMUM_EXPERIENCE_POSITIVE");
        if (min == 0 || ZeroMinimumText().IsMatch(candidate.ExperienceText ?? candidate.Description ?? string.Empty))
            return (FresherExperienceDecision.Eligible, "ZERO_MINIMUM_EXPERIENCE");
        if (!range.Success && SinglePositiveExperience().IsMatch(candidate.ExperienceText ?? string.Empty) &&
            !PreferredExperience().IsMatch(text))
            return (FresherExperienceDecision.Ineligible, "EXPERIENCE_ABOVE_FRESHER_RANGE");
        if (InternOrFresherSignal().IsMatch(candidate.Title ?? string.Empty) ||
            InternOrFresherSignal().IsMatch(candidate.EmploymentType ?? string.Empty) ||
            ExplicitFresherDescription().IsMatch(candidate.Description ?? string.Empty))
            return (FresherExperienceDecision.Eligible, "EXPLICIT_FRESHER_SIGNAL");
        if ((range.Success && min is > 0) || max is > 0)
            return (FresherExperienceDecision.Ineligible, "EXPERIENCE_ABOVE_FRESHER_RANGE");
        return (FresherExperienceDecision.Unknown, "FRESHER_EXPERIENCE_EVIDENCE_UNKNOWN");
    }

    public static bool IsSafePublicUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.HostNameType != UriHostNameType.Dns ||
            uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) return false;
        if (IPAddress.TryParse(host, out var address))
        {
            if (IPAddress.IsLoopback(address)) return false;
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                (bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                 (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254))) return false;
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
        }
        return true;
    }

    private static bool EqualsCountry(string? actual, params string[] valid) => actual is not null &&
        valid.Contains(actual, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\b(?:India|Pune|Bengaluru|Bangalore|Hyderabad|Mumbai|Chennai|New Delhi|Delhi|Gurugram|Gurgaon|Noida|Kolkata|Maharashtra|Karnataka|Telangana|Tamil Nadu|Kerala|Gujarat|West Bengal|IN)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex IndiaSignal();
    [GeneratedRegex(@"\b(?:India\s+(?:is\s+)?(?:not eligible|excluded|not supported|not permitted)|(?:not eligible|cannot hire|not hiring|excluding)\s+(?:candidates?\s+)?(?:in|from)?\s*India|outside\s+India\s+only)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex IndiaExcluded();
    [GeneratedRegex(@"\b(?:only|must reside in|residents of|based in|eligible in|hiring in)\s+(?:the\s+)?(?:United States|USA|US|United Kingdom|UK|Canada|Europe|European Union|EU|Australia|Germany|France|Ireland)\b|\b(?:United States|USA|US|United Kingdom|UK|Canada|Europe|European Union|EU|Australia|Germany|France|Ireland)\s+only\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex Restriction();
    [GeneratedRegex(@"\b(?:Remote\s*[-:]?\s*(?:United States|USA|US|United Kingdom|UK|Canada|Europe|EU)|(?:United States|USA|US|United Kingdom|UK|Canada|Europe|EU)\s+only)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex ForeignOnlyRestriction();
    [GeneratedRegex(@"\b(?:must reside in|applicants must reside in|residents of|only in|restricted to)\s+(?:the\s+)?(?:United States|USA|US|United Kingdom|UK|Canada|Europe|EU|Australia)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex ContradictoryRemoteRestriction();
    [GeneratedRegex(@"\b(?:(?:worldwide|global)\s+remote|remote\s+(?:worldwide|globally)|work from anywhere|anywhere in the world|remote anywhere)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex GlobalRemote();
    [GeneratedRegex(@"\bremote\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex Remote();
    [GeneratedRegex(@"\b(?:United States|USA|US|United Kingdom|UK|Canada|Europe|European Union|EU|Australia|Germany|France|Ireland|Singapore|Japan)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex ForeignCountry();
    [GeneratedRegex(@"\b(?:senior|sr\.?|lead|staff|principal|manager|architect|director|head of|vp|vice president)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex SeniorTitle();
    [GeneratedRegex(@"\b(?:fresher|freshers|intern(?:ship)?|graduate(?: trainee| engineer trainee)?|new graduate|entry[- ]level|campus hire|campus hiring|apprentice)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex InternOrFresherSignal();
    [GeneratedRegex(@"\b(?:students|fresh graduates|new graduates|fresher|internship|interns)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex ExplicitFresherDescription();
    [GeneratedRegex(@"\b(\d+)\s*(?:-|–|—|to)\s*(\d+)\s*(?:\+?\s*)?(?:years?|yrs?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex ExperienceRange();
    [GeneratedRegex(@"\b0\s*(?:\+|years?|yrs?)|\b0\s*(?:-|–|—|to)\s*\d+\s*(?:years?|yrs?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex ZeroMinimumText();
    [GeneratedRegex(@"\b(?:minimum|min\.?|at least|must have|requires?|required)\s*[1-9]\d*\s*(?:\+|years?|yrs?)|\b[1-9]\d*\s*\+?\s*(?:years?|yrs?)\s+(?:of\s+)?(?:professional\s+)?experience\s+(?:required|minimum|must)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex PositiveMinimumText();
    [GeneratedRegex(@"\b(?:preferred|nice to have|bonus|desired but not required|not required)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex PreferredExperience();
    [GeneratedRegex(@"\b[1-9]\d*\s*\+?\s*(?:years?|yrs?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 500)]
    private static partial Regex SinglePositiveExperience();
}

public sealed record FresherCandidate
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ExperienceText { get; init; }
    public int? MinimumExperienceYears { get; init; }
    public int? MaximumExperienceYears { get; init; }
    public string? EmploymentType { get; init; }
    public string? LocationText { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Country { get; init; }
    public string? WorkplaceType { get; init; }
    public string? RemoteEligibility { get; init; }
}
