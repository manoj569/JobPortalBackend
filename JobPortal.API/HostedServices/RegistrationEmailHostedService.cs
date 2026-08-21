using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;

namespace JobPortal.API.HostedServices;

public sealed class RegistrationEmailHostedService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);
        do
        {
            for (var processed = 0; processed < 20 && !stoppingToken.IsCancellationRequested; processed++)
            {
                await using var scope = scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<RegistrationEmailDispatcher>()
                    .ProcessOneAsync(stoppingToken)) break;
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

}

public sealed class RegistrationEmailDispatcher(
    IRegistrationEmailOutbox queue,
    IEmailService email,
    TimeProvider timeProvider,
    ILogger<RegistrationEmailDispatcher> logger)
{
    private static readonly Action<ILogger, string, int, Exception?> DeliveryOutcome =
        LoggerMessage.Define<string, int>(LogLevel.Information,
            new EventId(4101, nameof(DeliveryOutcome)),
            "Background email message {MessageType} completed with outcome {OutcomeCode}.");

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var request = await queue.ClaimDueAsync(now, cancellationToken);
        if (request is null) return false;
        var result = await email.SendRegistrationVerificationAsync(
            request.User, request.VerificationToken, cancellationToken);
        if (result == EmailDeliveryResult.Sent)
        {
            request.User.EmailVerificationSentAtUtc = now;
            await queue.MarkSentAsync(request.Id, now, cancellationToken);
        }
        else
        {
            var retryMinutes = Math.Min(60, 1 << Math.Min(request.AttemptCount, 6));
            await queue.MarkFailedAsync(request.Id, now.AddMinutes(retryMinutes), cancellationToken);
        }
        DeliveryOutcome(logger, "registration-verification", (int)result, null);
        return true;
    }
}
