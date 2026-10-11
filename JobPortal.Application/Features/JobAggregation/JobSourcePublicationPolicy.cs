using System.Text.Json;
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

// Optional import/geography and licensed-logo metadata. Never a publication authorization gate.
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
        var jobs = new List<RawExternalJob>();
        var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var held = false;
        foreach (var job in snapshot.Jobs)
        {
            var result = IndiaJobEligibilityPolicy.Evaluate(job);
            reasons[result.ReasonCode] = reasons.GetValueOrDefault(result.ReasonCode) + 1;
            if (result.Decision != IndiaJobEligibilityDecision.Accepted)
            {
                held |= result.Decision == IndiaJobEligibilityDecision.Review;
                rejected[result.ReasonCode] = rejected.GetValueOrDefault(result.ReasonCode) + 1;
                continue;
            }
            jobs.Add(job with
            {
                Location = IndiaJobEligibilityPolicy.DisplayLocation(job.Location),
                EligibilityReasonCode = result.ReasonCode,
                SourceMetadataJson = JsonSerializer.Serialize(job with { SourceMetadataJson = null, EligibilityReasonCode = result.ReasonCode })
            });
        }
        var limited = approval.TestImportLimit > 0;
        if (limited && jobs.Count > approval.TestImportLimit) rejected["TestImportLimit"] = jobs.Count - approval.TestImportLimit;
        return new(limited ? jobs.Take(approval.TestImportLimit).ToArray() : jobs,
            snapshot.Skipped, snapshot.IsComplete && !held && !limited)
        { SelectionReasonCounts = rejected.Count == 0 ? null : rejected, EligibilityReasonCounts = reasons };
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
