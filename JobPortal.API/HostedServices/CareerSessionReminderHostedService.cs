using JobPortal.Application.Features.CareerGuidance;
using Microsoft.Extensions.Options;

namespace JobPortal.API.HostedServices;

public sealed partial class CareerSessionReminderHostedService(IServiceScopeFactory scopes, IOptions<CareerSessionOptions> options,
    ILogger<CareerSessionReminderHostedService> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SessionRemindersEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SessionReminderPollSeconds), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<CareerSessionReminderProcessor>().ProcessAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception) { BatchFailed(logger); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Career session reminder batch failed; pending records will be retried.")]
    private static partial void BatchFailed(ILogger logger);
}
