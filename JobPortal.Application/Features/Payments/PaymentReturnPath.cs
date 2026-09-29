using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Features.Payments;

public static class PaymentReturnPath
{
    public const string InterviewInsights = "/dashboard/interview-insights";

    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (string.Equals(value, InterviewInsights, StringComparison.Ordinal)) return InterviewInsights;
        const string referralPrefix = "/dashboard/jobs/referral/";
        const string contactSuffix = "/contact";
        if (value.StartsWith(referralPrefix, StringComparison.Ordinal) && value.EndsWith(contactSuffix, StringComparison.Ordinal) &&
            Guid.TryParseExact(value[referralPrefix.Length..^contactSuffix.Length], "D", out var jobId) && jobId != Guid.Empty)
            return $"{referralPrefix}{jobId:D}{contactSuffix}";
        throw new BadRequestException("The return destination is invalid.", "invalid_return_to");
    }
}
