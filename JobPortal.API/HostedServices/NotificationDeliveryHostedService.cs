using JobPortal.Application.Features.Notifications;
using Microsoft.Extensions.Options;

namespace JobPortal.API.HostedServices;

public sealed partial class NotificationDeliveryHostedService(IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<NotificationDeliveryOptions> options, ILogger<NotificationDeliveryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    try
                    {
                        await scope.ServiceProvider.GetRequiredService<JobPortal.Application.Features.Referrals.IReferralNotificationScheduler>()
                            .EnqueueDueAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception) { DeliveryFailed(logger); }
                }
                for (var i = 0; i < options.Value.BatchSize && !stoppingToken.IsCancellationRequested; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    try
                    {
                        if (!await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().ProcessOneAsync(stoppingToken)) break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception) { DeliveryFailed(logger); }
                }
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    [LoggerMessage(4201, LogLevel.Warning, "Notification delivery attempt failed; its lease will recover if completion was not persisted.")]
    private static partial void DeliveryFailed(ILogger logger);
}
