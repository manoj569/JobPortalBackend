using JobPortal.Application.Features.CareerAccelerator;

namespace JobPortal.API.HostedServices;

public sealed partial class AcceleratorReconciliationHostedService(IServiceScopeFactory scopes,
    ILogger<AcceleratorReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await ProcessTutorAsync(stoppingToken)) break;
            if (!await ProcessProjectAsync(stoppingToken)) break;
            if (!await ProcessInterviewAsync(stoppingToken)) break;
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task<bool> ProcessTutorAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AcceleratorDispatchReconciliationService>()
                .ProcessBatchAsync(25, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return false; }
        catch (Exception ex) { ReconciliationDeferred(logger, "Tutor", ex); return true; }
    }

    private async Task<bool> ProcessProjectAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AcceleratorProjectAiService>()
                .ProcessBatchAsync(25, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return false; }
        catch (Exception ex) { ReconciliationDeferred(logger, "Project", ex); return true; }
    }

    private async Task<bool> ProcessInterviewAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AcceleratorInterviewAiService>()
                .ProcessBatchAsync(25, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return false; }
        catch (Exception ex) { ReconciliationDeferred(logger, "Interview", ex); return true; }
    }

    [LoggerMessage(8802, LogLevel.Warning,
        "Accelerator {Operation} reconciliation deferred; dispatch claims remain recoverable.")]
    private static partial void ReconciliationDeferred(ILogger logger, string operation, Exception exception);
}
