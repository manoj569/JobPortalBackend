using JobPortal.API.Services;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;

namespace JobPortal.API.HostedServices;

public sealed class JobAggregationSchedulerHostedService(
    JobAggregationScheduler scheduler, IOptions<JobAggregationOptions> options,
    TimeProvider clock, ILogger<JobAggregationSchedulerHostedService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> Disabled = LoggerMessage.Define(
        LogLevel.Information, new EventId(4310, nameof(Disabled)), "Job aggregation scheduler is disabled.");
    private static readonly Action<ILogger, Exception?> Started = LoggerMessage.Define(
        LogLevel.Information, new EventId(4311, nameof(Started)), "Job aggregation scheduler started.");
    private static readonly Action<ILogger, Exception?> Stopping = LoggerMessage.Define(
        LogLevel.Information, new EventId(4312, nameof(Stopping)), "Job aggregation scheduler stopping.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Scheduler;
        if (!settings.Enabled) { Disabled(logger, null); return; }
        Started(logger, null);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await scheduler.RunOnceAsync(stoppingToken);
                // Wait after the batch, so slow batches cannot overlap or cause a
                // catch-up loop. Timing is cancellation-aware and TimeProvider-based.
                await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), clock, stoppingToken);
            }
        }
        finally { Stopping(logger, null); }
    }
}
