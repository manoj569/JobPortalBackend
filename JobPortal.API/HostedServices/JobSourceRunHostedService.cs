using JobPortal.API.Services;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;

namespace JobPortal.API.HostedServices;

// Intentionally independent of Scheduler.Enabled: admins can run sources while scheduling is disabled.
public sealed partial class JobSourceRunHostedService(JobSourceRunWorker worker, TimeProvider clock,
    ILogger<JobSourceRunHostedService> logger, IOptions<JobAggregationOptions>? options = null) : BackgroundService
{
    [LoggerMessage(EventId = 4371, Level = LogLevel.Warning,
        Message = "Job source durable worker iteration failed; retrying on the next bounded poll.")]
    private static partial void IterationFailed(ILogger logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        => await Task.WhenAll(Enumerable.Range(0, options?.Value.Scheduler.MaxConcurrentSources ?? 1)
            .Select(_ => PollAsync(stoppingToken)));

    private async Task PollAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await worker.RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { IterationFailed(logger); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
