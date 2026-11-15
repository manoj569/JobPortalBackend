using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Persistence.Repositories;
using Microsoft.Extensions.Options;

namespace JobPortal.API.HostedServices;

public sealed partial class AcceleratorOutreachReconciliationHostedService(IServiceScopeFactory scopes,
    IOptions<TalentOutreachOptions> options,
    ILogger<AcceleratorOutreachReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.OutreachReconciliationEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AcceleratorTalentService>()
                    .ReconcileOutreachAsync(25, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { ReconciliationDeferred(logger, exception); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    [LoggerMessage(8865, LogLevel.Warning,
        "Outreach reconciliation deferred; persisted dispatch states permit later recovery.")]
    private static partial void ReconciliationDeferred(ILogger logger, Exception exception);
}
