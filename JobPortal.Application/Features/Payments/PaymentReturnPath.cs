using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Features.Payments;

public static class PaymentReturnPath
{
    public const string BrowseJobs = "/dashboard/jobs";
    // Referral Jobs is a tab on Browse Jobs, selected by this exact query string.
    public const string ReferralJobs = "/dashboard/jobs?mode=referral";
    public const string InterviewInsights = "/dashboard/interview-insights";
    public const string Membership = "/dashboard/membership";
    public const string ResumeMaker = "/dashboard/resume-maker";

    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value is BrowseJobs or ReferralJobs or InterviewInsights or Membership or ResumeMaker) return value;
        const string referralPrefix = "/dashboard/jobs/referral/";
        const string contactSuffix = "/contact";
        if (value.Length == referralPrefix.Length + 36 + contactSuffix.Length &&
            value.StartsWith(referralPrefix, StringComparison.Ordinal) && value.EndsWith(contactSuffix, StringComparison.Ordinal) &&
            Guid.TryParseExact(value[referralPrefix.Length..^contactSuffix.Length], "D", out var jobId) && jobId != Guid.Empty)
            return $"{referralPrefix}{jobId:D}{contactSuffix}";
        throw new BadRequestException("The return destination is invalid.", "invalid_return_to");
    }
}
