using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Features.Payments;

public static class PaymentReturnPath
{
    public const string InterviewInsights = "/dashboard/interview-insights";

    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (string.Equals(value, InterviewInsights, StringComparison.Ordinal)) return InterviewInsights;
        throw new BadRequestException("The return destination is invalid.", "invalid_return_to");
    }
}
