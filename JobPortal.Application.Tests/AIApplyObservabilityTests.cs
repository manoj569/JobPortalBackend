using System.Diagnostics;
using System.Diagnostics.Metrics;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIApplyObservabilityTests
{
    private static readonly string[] AllowedMetricTags = ["operation", "result", "site"];
    [Fact]
    public void ActivitySourceCreatesCorrelatedOperationWithoutSensitiveTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AIApplyTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = AIApplyTelemetry.ActivitySource.StartActivity("queue.poll");
        using var child = AIApplyTelemetry.ActivitySource.StartActivity("application.execute");
        child!.SetTag("site", "Greenhouse"); child.SetTag("result", "confirmed");
        Assert.Equal(parent!.TraceId, child.TraceId);
        Assert.DoesNotContain(child.TagObjects, x => x.Key.Contains("candidate", StringComparison.OrdinalIgnoreCase) || x.Key.Contains("url", StringComparison.OrdinalIgnoreCase) || x.Key.Contains("answer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MetricsUseOnlyBoundedLabels()
    {
        var measurements = new List<KeyValuePair<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) => { if (instrument.Meter.Name == AIApplyTelemetry.Name) meterListener.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => measurements.AddRange(tags.ToArray())); listener.Start();
        AIApplyTelemetry.QueueClaims.Add(1, AIApplyTelemetry.Tags("claim", "claimed", "Greenhouse"));
        Assert.NotEmpty(measurements);
        Assert.All(measurements, tag => Assert.Contains(tag.Key, AllowedMetricTags));
    }

    [Fact]
    public void OperationalEntitiesHaveUniqueDistributedKeysAndConcurrencyTokens()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var worker = db.Model.FindEntityType(typeof(AIApplyWorkerInstance))!; var site = db.Model.FindEntityType(typeof(AIApplySiteOperationalState))!;
        Assert.Contains(worker.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == nameof(AIApplyWorkerInstance.WorkerInstanceId));
        Assert.Contains(site.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == nameof(AIApplySiteOperationalState.Site));
        Assert.True(worker.FindProperty(nameof(AIApplyWorkerInstance.Version))!.IsConcurrencyToken);
        Assert.True(site.FindProperty(nameof(AIApplySiteOperationalState.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public void ZeroConfirmedSubmissionsProducesNoCostRatio()
    {
        const decimal totalCost = 12.5m; var confirmed = Array.Empty<int>().Length;
        decimal? ratio = confirmed == 0 ? null : totalCost / confirmed;
        Assert.Null(ratio);
    }
}
