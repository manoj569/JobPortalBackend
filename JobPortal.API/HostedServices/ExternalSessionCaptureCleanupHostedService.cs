using JobPortal.Application.Abstractions.AIApply;

namespace JobPortal.API.HostedServices;

public sealed class ExternalSessionCaptureCleanupHostedService(
    IExternalSessionCaptureCleanup cleanup,
    ILogger<ExternalSessionCaptureCleanupHostedService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> CleanupFailed = LoggerMessage.Define(
        LogLevel.Warning, new EventId(5401, "ExternalSessionCaptureCleanupFailed"),
        "External session capture cleanup iteration failed.");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await cleanup.CleanupAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception) { CleanupFailed(logger, null); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
