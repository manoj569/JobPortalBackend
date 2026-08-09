using JobPortal.Application.Features.JobDiscovery;
using Microsoft.Extensions.Options;

#pragma warning disable CA1848

namespace JobPortal.API.HostedServices;

public sealed class JobDiscoveryHostedService(IServiceScopeFactory scopes, IOptions<JobDiscoveryOptions> options,
    TimeProvider clock, ILogger<JobDiscoveryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) { logger.LogInformation("Daily job discovery is disabled"); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var next = new DateTimeOffset(now.Year, now.Month, now.Day, Math.Clamp(options.Value.RunHourUtc, 0, 23), 0, 0, TimeSpan.Zero);
            if (next <= now) next = next.AddDays(1);
            await Task.Delay(next - now, clock, stoppingToken);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IJobDiscoveryService>().RunAsync("Scheduled", null, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Scheduled job discovery failed"); }
        }
    }
}
