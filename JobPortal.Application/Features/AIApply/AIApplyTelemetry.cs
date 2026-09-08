using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace JobPortal.Application.Features.AIApply;

public static class AIApplyTelemetry
{
    public const string Name = "CareerHarbor.AIApply";
    public static readonly ActivitySource ActivitySource = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> QueueClaims = Meter.CreateCounter<long>("aiapply.queue.claims");
    public static readonly Counter<long> ClaimFailures = Meter.CreateCounter<long>("aiapply.queue.claim_failures");
    public static readonly Histogram<double> QueueWaitSeconds = Meter.CreateHistogram<double>("aiapply.queue.wait.seconds", "s");
    public static readonly Counter<long> ApplicationsStarted = Meter.CreateCounter<long>("aiapply.applications.started");
    public static readonly Counter<long> ApplicationsCompleted = Meter.CreateCounter<long>("aiapply.applications.completed");
    public static readonly Counter<long> RetriesScheduled = Meter.CreateCounter<long>("aiapply.retries.scheduled");
    public static readonly Counter<long> RetryExhausted = Meter.CreateCounter<long>("aiapply.retries.exhausted");
    public static readonly Histogram<double> RetryDelaySeconds = Meter.CreateHistogram<double>("aiapply.retries.delay.seconds", "s");
    public static readonly Histogram<double> BrowserDurationSeconds = Meter.CreateHistogram<double>("aiapply.browser.duration.seconds", "s");
    public static readonly Counter<long> BrowserFailures = Meter.CreateCounter<long>("aiapply.browser.failures");
    public static readonly Counter<long> CircuitOpens = Meter.CreateCounter<long>("aiapply.site.circuit.opens");
    public static readonly Counter<long> CircuitCloses = Meter.CreateCounter<long>("aiapply.site.circuit.closes");
    public static readonly Counter<long> WorkerLoops = Meter.CreateCounter<long>("aiapply.worker.loops");
    public static readonly Counter<long> WorkerLoopFailures = Meter.CreateCounter<long>("aiapply.worker.loop_failures");
    public static readonly Counter<long> StaleLeasesRecovered = Meter.CreateCounter<long>("aiapply.queue.stale_leases_recovered");
    public static readonly Counter<long> AIRequests = Meter.CreateCounter<long>("aiapply.ai.requests");
    public static readonly Counter<long> AIFailures = Meter.CreateCounter<long>("aiapply.ai.failures");
    public static readonly Counter<long> AITokens = Meter.CreateCounter<long>("aiapply.ai.tokens");
    public static readonly Histogram<double> EstimatedCost = Meter.CreateHistogram<double>("aiapply.cost.estimated");

    public static TagList Tags(string operation, string result, string? site = null)
    {
        var tags = new TagList { { "operation", operation }, { "result", result } };
        if (!string.IsNullOrWhiteSpace(site)) tags.Add("site", site);
        return tags;
    }
}
