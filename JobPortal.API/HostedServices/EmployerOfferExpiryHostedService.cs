using JobPortal.Application.Abstractions.Persistence;

namespace JobPortal.API.HostedServices;

public sealed class EmployerOfferExpiryHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<EmployerOfferExpiryHostedService> logger)
    : BackgroundService
{
    private static readonly Action<ILogger, Exception?> WorkerDisabled =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2101, nameof(WorkerDisabled)),
            "Automatic employer offer expiry is disabled.");

    private static readonly Action<ILogger, int, Exception?> OffersExpired =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(2102, nameof(OffersExpired)),
            "Automatic employer offer expiry marked {ExpiredOfferCount} offers as Expired.");

    private static readonly Action<ILogger, Exception?> ExpiryFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2103, nameof(ExpiryFailed)),
            "Automatic employer offer expiry cycle failed.");

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!configuration.GetValue(
                "EmployerOfferExpiry:Enabled",
                true))
        {
            WorkerDisabled(logger, null);
            return;
        }

        var intervalMinutes = Math.Clamp(
            configuration.GetValue(
                "EmployerOfferExpiry:IntervalMinutes",
                5),
            1,
            1440);

        if (configuration.GetValue(
                "EmployerOfferExpiry:RunOnStartup",
                true))
        {
            await RunCycleAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(intervalMinutes),
            timeProvider);

        while (await timer.WaitForNextTickAsync(
                   stoppingToken))
        {
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope =
                scopeFactory.CreateAsyncScope();

            var repository =
                scope.ServiceProvider
                    .GetRequiredService<IEmployerOfferRepository>();

            var expiredCount =
                await repository.ExpireOverdueAsync(
                    timeProvider.GetUtcNow().UtcDateTime,
                    cancellationToken);

            if (expiredCount == 0)
                return;

            OffersExpired(
                logger,
                expiredCount,
                null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ExpiryFailed(
                logger,
                exception);
        }
    }
}
