using JobPortal.Application.Features.CareerAccelerator;
using Microsoft.Extensions.Options;

namespace JobPortal.API.HostedServices;

public sealed partial class AcceleratorMaintenanceHostedService(IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<AcceleratorOptions> options, ILogger<AcceleratorMaintenanceHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.Enabled)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AcceleratorQuotaService>().MaintainAsync(stoppingToken);
                }
                await Task.Delay(TimeSpan.FromSeconds(options.Value.MaintenanceSeconds), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                MaintenanceDeferred(logger);
                await Task.Delay(TimeSpan.FromSeconds(options.Value.MaintenanceSeconds), clock, stoppingToken);
            }
        }
    }
    [LoggerMessage(8801, LogLevel.Warning, "Accelerator maintenance deferred; durable usage records remain available for recovery.")]
    private static partial void MaintenanceDeferred(ILogger logger);
}
