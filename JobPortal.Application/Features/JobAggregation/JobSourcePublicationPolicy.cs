using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.JobAggregation;

public interface IJobSourcePublicationPolicy
{
    ExternalJobSourceSnapshot Select(JobSource source, ExternalJobSourceSnapshot snapshot);
    void ApplyLicensedLogo(JobSource source);
}

// Optional import/geography and licensed-logo metadata. Never a publication authorization gate.
public sealed partial class JobSourcePublicationPolicy(IOptions<JobAggregationOptions> options, TimeProvider clock)
    : IJobSourcePublicationPolicy
{
    private JobSourcePublicationApproval Settings(JobSource source)
    {
        var settings = options.Value.SourceApprovals.GetValueOrDefault(source.Id.ToString("D"))
            ?? new JobSourcePublicationApproval();
        if (settings.TestImportLimit is < 0 or > 1000)
            throw new BadRequestException("Test import limit must be between 0 and 1000.", "invalid_test_import_limit");
        return settings;
    }

    public ExternalJobSourceSnapshot Select(JobSource source, ExternalJobSourceSnapshot snapshot)
    {
        var approval = Settings(source);
        var jobs = new List<RawExternalJob>();
        var selectionReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknownEligibility = false;
        foreach (var job in snapshot.Jobs)
        {
            if (!approval.IndiaOnly) { jobs.Add(job); continue; }
            var locations = job.AdditionalLocations.Prepend(job.Location ?? string.Empty).ToArray();
            if (job.CountryCodes.Any(IsIndia) || job.CountryCodes.Count == 0 && locations.Any(x => !string.IsNullOrWhiteSpace(x) && (IndiaLocation().IsMatch(x) ||
                    approval.VerifiedIndiaLocations.Contains(x.Trim(), StringComparer.OrdinalIgnoreCase))) ||
                job.CountryCodes.Count == 0 && approval.AllowExplicitWorldwideRemote &&
                string.Equals(job.WorkplaceTypeText, "remote", StringComparison.OrdinalIgnoreCase) &&
                locations.Any(x => x.Trim().ToUpperInvariant() is "WORLDWIDE" or "GLOBAL" or "ANYWHERE"))
            {
                jobs.Add(job);
                continue;
            }
            // A structured foreign country can be excluded intentionally. Unknown geography is NOT evidence of India eligibility.
            // Do not close jobs on an ambiguous geographic snapshot (including a generic "Remote").
            if (job.CountryCodes.Count == 0 || !job.CountryCodes.All(IsKnownCountry) || job.AdditionalLocations.Count > 0)
                unknownEligibility = true;
            var reason = job.CountryCodes.Count == 0 || !job.CountryCodes.All(IsKnownCountry) || job.AdditionalLocations.Count > 0
                ? string.Equals(job.WorkplaceTypeText, "remote", StringComparison.OrdinalIgnoreCase)
                    ? "AmbiguousRemoteEligibility" : "UnknownIndiaEligibility"
                : "NonIndiaLocation";
            selectionReasons[reason] = selectionReasons.GetValueOrDefault(reason) + 1;
        }
        var limited = approval.TestImportLimit > 0;
        if (limited && jobs.Count > approval.TestImportLimit)
            selectionReasons["TestImportLimit"] = jobs.Count - approval.TestImportLimit;
        return new(limited ? jobs.Take(approval.TestImportLimit).ToArray() : jobs,
            snapshot.Skipped, snapshot.IsComplete && !unknownEligibility && !limited)
        { SelectionReasonCounts = selectionReasons.Count == 0 ? null : selectionReasons };
    }

    public void ApplyLicensedLogo(JobSource source)
    {
        // Retain strict matching for optional logo assets only. Missing/expired metadata simply skips the logo.
        if (!options.Value.SourceApprovals.TryGetValue(source.Id.ToString("D"), out var approval) ||
            source.Company is null || source.Company.IsDeleted || !source.Company.IsVerified ||
            source.CompanyId != source.Company.Id || approval.CompanyId != source.CompanyId ||
            approval.AtsType != source.AtsType || approval.AtsIdentifier != source.AtsIdentifier?.Trim() ||
            !SafeUrl(source.CareerPageUrl, out var career) || !SafeUrl(approval.CareerPageUrl, out var approvedCareer) ||
            career != approvedCareer || approval.RightsExpireAtUtc <= clock.GetUtcNow()) return;
        // A manually configured/company-uploaded logo always wins. No download, image search, or name matching.
        if (!string.IsNullOrWhiteSpace(source.Company.LogoUrl) || string.IsNullOrWhiteSpace(approval.LogoRightsEvidence) ||
            !SafeUrl(approval.LogoUrl, out var logo) || !string.IsNullOrEmpty(logo!.Query)) return;
        source.Company.LogoUrl = logo.AbsoluteUri;
    }

    private static bool IsIndia(string code) => code.Trim().ToUpperInvariant() is "IN" or "IND" or "INDIA";

    private static readonly HashSet<string> CountryCodes = System.Globalization.CultureInfo
        .GetCultures(System.Globalization.CultureTypes.SpecificCultures)
        .Where(culture => culture.Name.Length > 0)
        .Select(culture => new System.Globalization.RegionInfo(culture.Name))
        .SelectMany(region => new[] { region.TwoLetterISORegionName, region.ThreeLetterISORegionName, region.EnglishName })
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static bool IsKnownCountry(string code) => CountryCodes.Contains(code.Trim());

    private static bool SafeUrl(string? text, out Uri? uri) => Uri.TryCreate(text, UriKind.Absolute, out uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Fragment) && uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback;

    [GeneratedRegex(@"\bIndia\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex IndiaLocation();
}
