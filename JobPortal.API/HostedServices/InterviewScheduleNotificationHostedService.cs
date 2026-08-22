using JobPortal.Application.Abstractions.InterviewInsights;

namespace JobPortal.API.HostedServices;

public sealed class InterviewScheduleNotificationHostedService(
    IServiceScopeFactory scopes, TimeProvider timeProvider,
    ILogger<InterviewScheduleNotificationHostedService> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> Completed = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(4201, nameof(Completed)),
        "Interview schedule notification cycle created {NotificationCount} notifications.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            var count = await scope.ServiceProvider.GetRequiredService<IInterviewInsightRepository>()
                .CreateDueScheduleNotificationsAsync(timeProvider.GetUtcNow().UtcDateTime, stoppingToken);
            if (count > 0) Completed(logger, count, null);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
