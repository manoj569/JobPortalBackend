using JobPortal.Application.Abstractions.Payments;

namespace JobPortal.Application.Features.Payments;

public sealed record MembershipPricing(decimal BaseAmount, decimal TaxRate, decimal TaxAmount, decimal TotalAmount)
{
    public const decimal DefaultGstRate = 18m;

    public static MembershipPricing Calculate(MembershipPlan plan, decimal rate)
    {
        if (rate is < 0m or > 100m || decimal.Round(rate, 4) != rate)
            throw new InvalidOperationException("Membership GST rate must be between 0 and 100 with at most four decimal places.");
        if (plan.Amount <= 0m || decimal.Round(plan.Amount, 2) != plan.Amount || plan.DurationDays <= 0)
            throw new InvalidOperationException("Membership pricing is invalid.");
        var tax = decimal.Round(plan.Amount * rate / 100m, 2, MidpointRounding.AwayFromZero);
        return new(plan.Amount, rate, tax, checked(plan.Amount + tax));
    }
}
