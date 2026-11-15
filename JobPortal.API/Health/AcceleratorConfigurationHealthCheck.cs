using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.CareerAccelerator;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace JobPortal.API.Health;

public sealed class AcceleratorConfigurationHealthCheck(IOptions<AcceleratorOptions> options,
    IMembershipPlanProvider plans) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = options.Value;
            if (!settings.IsValid())
                return Task.FromResult(HealthCheckResult.Unhealthy("Accelerator limits are invalid."));
            if (!settings.Enabled)
                return Task.FromResult(HealthCheckResult.Healthy("Accelerator is disabled by configuration."));
            var plan = plans.GetRequired(AcceleratorOptions.PlanCode);
            if (plan.Amount != settings.BaseAmountInr || plan.CurrencyCode != "INR" || plan.DurationDays != 30)
                return Task.FromResult(HealthCheckResult.Unhealthy("Accelerator plan configuration disagrees with checkout."));
            // An optional AI provider being disabled is a feature state, not a service failure.
            return Task.FromResult(HealthCheckResult.Healthy(settings.AIEnabled
                ? "Accelerator configuration is valid; AI availability depends on provider policy."
                : "Accelerator configuration is valid; AI is disabled by configuration."));
        }
        catch (Exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Accelerator configuration is unavailable."));
        }
    }
}
