using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIApply;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIApplyReliabilityTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RetryPolicyRetriesOnlyTransientFailuresWithBoundedDelay()
    {
        var policy = new AIApplyRetryPolicy(Options.Create(new AIApplyOptions { MaxRetryAttempts = 2, RetryBaseDelaySeconds = 30, RetryMaximumDelaySeconds = 120, RetryJitterRatio = 0 }));
        var transient = policy.Decide(AIApplyFailureKind.Timeout, "navigation_timeout", 0, false, Now);
        Assert.True(transient.ShouldRetry); Assert.Equal(Now.AddSeconds(30), transient.NextAttemptAtUtc);
        Assert.False(policy.Decide(AIApplyFailureKind.LoginRequired, "login_required", 0, false, Now).ShouldRetry);
        Assert.True(policy.Decide(AIApplyFailureKind.Timeout, "navigation_timeout", 2, false, Now).DeadLetter);
    }

    [Fact]
    public void SubmissionAttemptNeverAutomaticallyRetries()
    {
        var policy = new AIApplyRetryPolicy(Options.Create(new AIApplyOptions()));
        var result = policy.Decide(AIApplyFailureKind.Timeout, "browser_crashed", 0, true, Now);
        Assert.False(result.ShouldRetry); Assert.Equal(AIApplyFailureClassification.SubmissionUnconfirmed, result.Classification);
    }

    [Fact]
    public async Task StaleLeaseBeforeSubmissionRequeuesButAttemptedSubmissionNeedsReview()
    {
        await using var context = Context(); var safe = Application(false); var uncertain = Application(true); context.AIApplyApplications.AddRange(safe, uncertain); await context.SaveChangesAsync();
        var repository = new AIApplyRepository(context); await repository.RecoverExpiredLeasesAsync(Now, 10);
        Assert.Equal(AIApplyRunStatus.Queued, safe.Status); Assert.Equal(AIApplyRunStatus.NeedsReview, uncertain.Status);
        Assert.Equal(AIApplyFailureKind.SubmissionUnconfirmed, uncertain.FailureKind); Assert.Null(safe.LeaseOwner);
    }

    [Fact]
    public async Task CircuitIgnoresSingleFailureAndOpensAfterConfiguredTechnicalWindow()
    {
        var options = new AIApplyOptions { Reliability = new() { SiteHealthMinimumSamples = 3, SiteFailureThreshold = .66m, SiteObservationMinutes = 10, CircuitCooldownSeconds = 60 } }; options.Sites.Greenhouse.Enabled = true;
        var store = new FakeOperationalStore(options); var health = new ConfiguredJobSiteAdapterHealthService(Options.Create(options), store, new FixedWorker("one"));
        await health.RecordOutcomeAsync(JobSiteIdentifier.Greenhouse, false, Now, default); Assert.True(await health.CanExecuteAsync(JobSiteIdentifier.Greenhouse, Now, default));
        await health.RecordOutcomeAsync(JobSiteIdentifier.Greenhouse, false, Now, default); await health.RecordOutcomeAsync(JobSiteIdentifier.Greenhouse, true, Now, default);
        Assert.False(await health.CanExecuteAsync(JobSiteIdentifier.Greenhouse, Now, default)); Assert.True(await health.CanExecuteAsync(JobSiteIdentifier.Greenhouse, Now.AddSeconds(61), default));
        var other = new ConfiguredJobSiteAdapterHealthService(Options.Create(options), store, new FixedWorker("two"));
        Assert.False(await other.CanExecuteAsync(JobSiteIdentifier.Greenhouse, Now.AddSeconds(61), default));
        await health.RecordOutcomeAsync(JobSiteIdentifier.Greenhouse, true, Now.AddSeconds(61), default);
        Assert.True(await other.CanExecuteAsync(JobSiteIdentifier.Greenhouse, Now.AddSeconds(62), default));
    }

    [Fact]
    public async Task ManualCircuitControlBlocksAndThenRestoresExecution()
    {
        var options = new AIApplyOptions(); options.Sites.Lever.Enabled = true;
        var store = new FakeOperationalStore(options); var health = new ConfiguredJobSiteAdapterHealthService(Options.Create(options), store, new FixedWorker("one"));
        await health.OpenCircuitAsync(JobSiteIdentifier.Lever, Now, default); Assert.False(await health.CanExecuteAsync(JobSiteIdentifier.Lever, Now, default));
        await health.CloseCircuitAsync(JobSiteIdentifier.Lever, default); Assert.True(await health.CanExecuteAsync(JobSiteIdentifier.Lever, Now, default));
    }

    [Fact]
    public void ProductionConfigurationRejectsLoopbackBrowserAccess()
    {
        var options = ValidOptions(); options.Browser.AllowLoopbackForTests = true;
        Assert.True(new AIApplyOptionsValidator(false).Validate(null, options).Failed);
    }

    [Fact]
    public void EnabledConfigurationRejectsInvalidLeaseRetryAndThresholdValues()
    {
        var options = ValidOptions(); options.LeaseRenewalSeconds = options.LeaseSeconds;
        options.RetryBaseDelaySeconds = -1; options.Reliability.SiteFailureThreshold = 2;
        Assert.True(new AIApplyOptionsValidator(false).Validate(null, options).Failed);
    }

    [Fact]
    public void ValidProductionLikeConfigurationPassesValidation() =>
        Assert.False(new AIApplyOptionsValidator(false).Validate(null, ValidOptions()).Failed);

    [Fact]
    public void ReliabilityColumnsAndLeaseIndexAreInModel()
    {
        using var context = Context(); var entity = context.Model.FindEntityType(typeof(AIApplyApplication))!;
        Assert.NotNull(entity.FindProperty(nameof(AIApplyApplication.LeaseExpiresAtUtc))); Assert.NotNull(entity.FindProperty(nameof(AIApplyApplication.SubmissionAttemptedAtUtc)));
        Assert.Contains(entity.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(AIApplyApplication.Status), nameof(AIApplyApplication.LeaseExpiresAtUtc)]));
    }

    private static JobPortalDbContext Context() => new(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static AIApplyOptions ValidOptions() => new() { Enabled = true, MaxConcurrentApplications = 1, PerUserMaxConcurrentApplications = 1, LeaseSeconds = 180, LeaseRenewalSeconds = 45, RetryBaseDelaySeconds = 30, RetryMaximumDelaySeconds = 900, MaximumBrowserSecondsPerApplication = 900, MaximumAIRequestsPerApplication = 10, MaximumEstimatedCostPerApplication = 100 };
    private static AIApplyApplication Application(bool attempted) => new() { UserId = Guid.NewGuid(), JobId = Guid.NewGuid(), ExternalApplicationUrl = "https://example.test/apply", NormalizedApplicationUrl = $"https://example.test/{Guid.NewGuid():N}", Status = AIApplyRunStatus.Processing, ScheduledAtUtc = Now.AddMinutes(-5), LeaseOwner = "stale-worker", LeaseExpiresAtUtc = Now.AddMinutes(-1), SubmissionAttemptedAtUtc = attempted ? Now.AddMinutes(-2) : null };
}

file sealed record FixedWorker(string Id) : JobPortal.Application.Abstractions.AIApply.IAIApplyWorkerIdentity;

file sealed class FakeOperationalStore(AIApplyOptions options) : JobPortal.Application.Abstractions.AIApply.IAIApplyOperationalStore
{
    private readonly object gate = new();
    private readonly Dictionary<JobSiteIdentifier, State> sites = [];
    public Task<bool> TryAcquireSiteExecutionAsync(JobSiteIdentifier site, string workerId, DateTime now, CancellationToken ct)
    {
        lock (gate) { var s = Get(site); if (s.Manual == true || s.OpenUntil > now || s.Probe is not null && s.Probe != workerId) return Task.FromResult(false); if (s.OpenUntil.HasValue) s.Probe = workerId; return Task.FromResult(true); }
    }
    public Task RecordSiteOutcomeAsync(JobSiteIdentifier site, string workerId, bool success, DateTime now, CancellationToken ct)
    {
        lock (gate) { var s = Get(site); if (s.Probe == workerId) { s.Probe = null; s.OpenUntil = success ? null : now.AddSeconds(options.Reliability.CircuitCooldownSeconds); s.Samples = s.Failures = 0; return Task.CompletedTask; } s.Samples++; if (!success) s.Failures++; if (s.Samples >= options.Reliability.SiteHealthMinimumSamples && (decimal)s.Failures / s.Samples >= options.Reliability.SiteFailureThreshold) s.OpenUntil = now.AddSeconds(options.Reliability.CircuitCooldownSeconds); return Task.CompletedTask; }
    }
    public Task SetManualCircuitAsync(JobSiteIdentifier site, bool open, DateTime now, CancellationToken ct) { lock (gate) { var s = Get(site); s.Manual = open; s.OpenUntil = open ? now.AddSeconds(options.Reliability.CircuitCooldownSeconds) : null; s.Probe = null; } return Task.CompletedTask; }
    public Task<JobPortal.Application.Abstractions.AIApply.AIApplySiteOperationalSnapshot> GetSiteAsync(JobSiteIdentifier site, CancellationToken ct) { lock (gate) { var s = Get(site); return Task.FromResult(new JobPortal.Application.Abstractions.AIApply.AIApplySiteOperationalSnapshot(site, s.OpenUntil.HasValue ? AIApplyCircuitState.Open : AIApplyCircuitState.Closed, s.Samples, s.Failures, s.OpenUntil, null, null, s.Manual)); } }
    public async Task<IReadOnlyList<JobPortal.Application.Abstractions.AIApply.AIApplySiteOperationalSnapshot>> GetSitesAsync(CancellationToken ct) { var result = new List<JobPortal.Application.Abstractions.AIApply.AIApplySiteOperationalSnapshot>(); foreach (var site in sites.Keys) result.Add(await GetSiteAsync(site, ct)); return result; }
    public Task UpsertWorkerAsync(string workerId, DateTime startedAtUtc, DateTime heartbeatAtUtc, int processingCount, int maximumConcurrency, bool pollSucceeded, bool pollFailed, CancellationToken ct) => Task.CompletedTask;
    public Task MarkWorkerInactiveAsync(string workerId, DateTime stoppedAtUtc, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<JobPortal.Application.Abstractions.AIApply.AIApplyWorkerOperationalSnapshot>> GetWorkersAsync(DateTime staleBeforeUtc, CancellationToken ct) => Task.FromResult<IReadOnlyList<JobPortal.Application.Abstractions.AIApply.AIApplyWorkerOperationalSnapshot>>([]);
    public Task<int> CleanupWorkersAsync(DateTime retentionBeforeUtc, int maximum, CancellationToken ct) => Task.FromResult(0);
    private State Get(JobSiteIdentifier site) { if (!sites.TryGetValue(site, out var value)) sites[site] = value = new(); return value; }
    private sealed class State { public int Samples; public int Failures; public DateTime? OpenUntil; public string? Probe; public bool? Manual; }
}
