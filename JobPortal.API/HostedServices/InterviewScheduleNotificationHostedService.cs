using JobPortal.Application.Abstractions.InterviewInsights;

namespace JobPortal.API.HostedServices;

public sealed class InterviewScheduleNotificationHostedService(
    IServiceScopeFactory scopes, TimeProvider timeProvider,
    ILogger<InterviewScheduleNotificationHostedService> logger,
    TimeSpan? retryDelay = null) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private readonly TimeSpan retryDelay = retryDelay ?? TimeSpan.FromSeconds(15);
    private static readonly Action<ILogger, int, Exception?> Completed = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(4201, nameof(Completed)),
        "Interview schedule notification cycle created {NotificationCount} notifications.");
    private static readonly Action<ILogger, Exception?> Failed = LoggerMessage.Define(
        LogLevel.Error, new EventId(4202, nameof(Failed)),
        "Interview schedule notification cycle failed; the worker will retry.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await RunIterationAsync(stoppingToken))
            {
                await Task.Delay(retryDelay, timeProvider, stoppingToken);
                continue;
            }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    internal async Task<bool> RunIterationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var count = await scope.ServiceProvider.GetRequiredService<IInterviewScheduleNotificationProcessor>()
                .CreateDueScheduleNotificationsAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            if (count > 0) Completed(logger, count, null);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Failed(logger, exception);
            return false;
        }
    }
}
