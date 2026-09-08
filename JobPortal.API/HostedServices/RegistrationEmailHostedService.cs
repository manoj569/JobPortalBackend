using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;

namespace JobPortal.API.HostedServices;

public sealed class RegistrationEmailHostedService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<RegistrationEmailHostedService> logger,
    TimeSpan? retryDelay = null) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan retryDelay = retryDelay ?? TimeSpan.FromSeconds(15);
    private static readonly Action<ILogger, Exception?> IterationFailed =
        LoggerMessage.Define(LogLevel.Error,
            new EventId(4102, nameof(IterationFailed)),
            "Registration email polling iteration failed; the worker will retry.");

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
            for (var processed = 0; processed < 20 && !cancellationToken.IsCancellationRequested; processed++)
            {
                await using var scope = scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<RegistrationEmailDispatcher>()
                    .ProcessOneAsync(cancellationToken)) break;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            IterationFailed(logger, exception);
            return false;
        }
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
