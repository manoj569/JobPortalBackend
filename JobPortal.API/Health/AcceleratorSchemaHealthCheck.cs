using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobPortal.API.Health;

public sealed partial class AcceleratorSchemaHealthCheck(JobPortalDbContext db,
    ILogger<AcceleratorSchemaHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await db.AcceleratorAiDispatchRecords.AsNoTracking().Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            _ = await db.AcceleratorCandidateProjects.AsNoTracking().Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            _ = await db.AcceleratorInterviewSessions.AsNoTracking().Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            _ = await db.Payments.AsNoTracking()
                .Select(x => new { x.PlanVersion, x.AcceleratorRefundReviewRequired })
                .FirstOrDefaultAsync(cancellationToken);
            return HealthCheckResult.Healthy("Accelerator schema is queryable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SchemaUnavailable(logger, exception);
            return HealthCheckResult.Unhealthy("Accelerator schema is unavailable.");
        }
    }

    [LoggerMessage(8805, LogLevel.Warning, "Accelerator schema readiness check failed.")]
    private static partial void SchemaUnavailable(ILogger logger, Exception exception);
}
