using JobPortal.Application.Features.FresherAcquisition;

namespace JobPortal.API.HostedServices;

public sealed partial class FresherImportHostedService(IServiceScopeFactory scopes,
    ILogger<FresherImportHostedService> logger) : BackgroundService
{
    [LoggerMessage(EventId = 8582, Level = LogLevel.Warning, Message = "Durable acquisition poll failed; retrying.")]
    private static partial void PollFailed(ILogger logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<IDurableFresherImportProcessor>()
                    .ProcessNextAsync(scope.ServiceProvider.GetRequiredService<IFresherBulkImportService>(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { PollFailed(logger); }
            try { await Task.Delay(TimeSpan.FromSeconds(processed ? 1 : 5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
