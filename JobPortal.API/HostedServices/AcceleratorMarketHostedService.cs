using JobPortal.Application.Features.CareerAccelerator;
using Microsoft.Extensions.Options;

namespace JobPortal.API.HostedServices;

public sealed partial class AcceleratorMarketHostedService(IServiceScopeFactory scopes,
    IOptions<MarketIntelligenceOptions> options, ILogger<AcceleratorMarketHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.RefreshMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MarketIntelligenceService>()
                    .ProcessBatchAsync(null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { BatchDeferred(logger, exception); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    [LoggerMessage(8853, LogLevel.Warning,
        "Market intelligence batch deferred; persisted checkpoint permits later recovery.")]
    private static partial void BatchDeferred(ILogger logger, Exception exception);
}
