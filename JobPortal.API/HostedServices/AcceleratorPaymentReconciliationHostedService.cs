using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.CareerAccelerator;

namespace JobPortal.API.HostedServices;

public sealed partial class AcceleratorPaymentReconciliationHostedService(IServiceScopeFactory scopes,
    ILogger<AcceleratorPaymentReconciliationHostedService> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var orders = await scope.ServiceProvider.GetRequiredService<IAcceleratorPhase6OperationsStore>()
                    .ReconcilablePhonePeOrdersAsync(clock.GetUtcNow().UtcDateTime.AddMinutes(-2), 25, stoppingToken);
                foreach (var order in orders)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    try
                    {
                        await using var paymentScope = scopes.CreateAsyncScope();
                        await paymentScope.ServiceProvider.GetRequiredService<IPaymentService>()
                            .ReconcileAcceleratorPhonePeSystemAsync(order, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception exception) { PaymentReconciliationDeferred(logger, exception); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { PaymentBatchDeferred(logger, exception); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    [LoggerMessage(8803, LogLevel.Warning,
        "Accelerator PhonePe reconciliation deferred; payment remains available for later verification.")]
    private static partial void PaymentReconciliationDeferred(ILogger logger, Exception exception);

    [LoggerMessage(8804, LogLevel.Warning,
        "Accelerator PhonePe reconciliation batch deferred; pending payments remain recoverable.")]
    private static partial void PaymentBatchDeferred(ILogger logger, Exception exception);
}
