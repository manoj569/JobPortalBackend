using JobPortal.Application.Services;
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

// Optional execution cap and licensed-logo metadata. No business eligibility gate.
public sealed class JobSourcePublicationPolicy(IOptions<JobAggregationOptions> options, TimeProvider clock)
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
        // Employer ingestion is geography/experience neutral. Preserve original
        // provider facts for downstream product and publication decisions.
        var jobs = snapshot.Jobs.Select(job => job with
        {
            Location = IndiaJobEligibilityPolicy.DisplayLocation(job.Location)
        }).ToArray();
        var limited = approval.TestImportLimit > 0;
        var omitted = limited ? Math.Max(0, jobs.Length - approval.TestImportLimit) : 0;
        // Diagnostic facts only: no geography result removes a provider record.
        var reasons = jobs.GroupBy(job => IndiaJobEligibilityPolicy.Evaluate(job).ReasonCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return snapshot with
        {
            Jobs = limited ? jobs.Take(approval.TestImportLimit).ToArray() : jobs,
            IsComplete = snapshot.IsComplete && !limited,
            SelectionLimited = limited,
            SelectionReasonCounts = omitted == 0 ? null : new Dictionary<string,int> { ["TestImportLimit"] = omitted },
            EligibilityReasonCounts = reasons
        };
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

    private static bool SafeUrl(string? text, out Uri? uri) => Uri.TryCreate(text, UriKind.Absolute, out uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Fragment) && uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback;

}
