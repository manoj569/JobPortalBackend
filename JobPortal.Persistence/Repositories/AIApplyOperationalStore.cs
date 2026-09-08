using System.Data;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPortal.Persistence.Repositories;

public sealed class AIApplyOperationalStore(JobPortalDbContext db, IOptions<AIApplyOptions> options) : IAIApplyOperationalStore
{
    public async Task UpsertWorkerAsync(string workerId, DateTime startedAtUtc, DateTime heartbeatAtUtc, int processingCount, int maximumConcurrency, bool pollSucceeded, bool pollFailed, CancellationToken ct)
    {
        var updated = await db.AIApplyWorkerInstances.Where(x => x.WorkerInstanceId == workerId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Status, AIApplyWorkerStatus.Running).SetProperty(x => x.LastHeartbeatAtUtc, heartbeatAtUtc)
            .SetProperty(x => x.ProcessingCount, Math.Max(0, processingCount)).SetProperty(x => x.MaximumConcurrency, Math.Clamp(maximumConcurrency, 1, 20))
            .SetProperty(x => x.StoppedAtUtc, (DateTime?)null).SetProperty(x => x.Version, x => x.Version + 1)
            .SetProperty(x => x.LastSuccessfulPollAtUtc, x => pollSucceeded ? heartbeatAtUtc : x.LastSuccessfulPollAtUtc)
            .SetProperty(x => x.LastFailureAtUtc, x => pollFailed ? heartbeatAtUtc : x.LastFailureAtUtc), ct);
        if (updated == 0)
        {
            var row = new AIApplyWorkerInstance { WorkerInstanceId = workerId, StartedAtUtc = startedAtUtc, LastHeartbeatAtUtc = heartbeatAtUtc, ProcessingCount = Math.Max(0, processingCount), MaximumConcurrency = Math.Clamp(maximumConcurrency, 1, 20), HostVersion = typeof(AIApplyOperationalStore).Assembly.GetName().Version?.ToString() ?? "unknown", LastSuccessfulPollAtUtc = pollSucceeded ? heartbeatAtUtc : null, LastFailureAtUtc = pollFailed ? heartbeatAtUtc : null };
            db.AIApplyWorkerInstances.Add(row);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.Entry(row).State = EntityState.Detached;
                await db.AIApplyWorkerInstances.Where(x => x.WorkerInstanceId == workerId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, AIApplyWorkerStatus.Running).SetProperty(x => x.LastHeartbeatAtUtc, heartbeatAtUtc).SetProperty(x => x.ProcessingCount, Math.Max(0, processingCount)).SetProperty(x => x.MaximumConcurrency, Math.Clamp(maximumConcurrency, 1, 20)).SetProperty(x => x.Version, x => x.Version + 1), ct);
            }
        }
    }

    public async Task MarkWorkerInactiveAsync(string workerId, DateTime stoppedAtUtc, CancellationToken ct)
    {
        var row = await db.AIApplyWorkerInstances.SingleOrDefaultAsync(x => x.WorkerInstanceId == workerId, ct);
        if (row is null) return;
        row.Status = AIApplyWorkerStatus.Inactive; row.StoppedAtUtc = stoppedAtUtc; row.LastHeartbeatAtUtc = stoppedAtUtc; row.ProcessingCount = 0; row.Version++;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AIApplyWorkerOperationalSnapshot>> GetWorkersAsync(DateTime staleBeforeUtc, CancellationToken ct) =>
        await db.AIApplyWorkerInstances.AsNoTracking().OrderByDescending(x => x.LastHeartbeatAtUtc)
            .Select(x => new AIApplyWorkerOperationalSnapshot(x.WorkerInstanceId, x.StartedAtUtc, x.LastHeartbeatAtUtc, x.ProcessingCount, x.MaximumConcurrency, x.HostVersion,
                x.Status == AIApplyWorkerStatus.Inactive ? "Inactive" : x.LastHeartbeatAtUtc < staleBeforeUtc ? "Stale" : "Running",
                x.Status == AIApplyWorkerStatus.Running && x.LastHeartbeatAtUtc < staleBeforeUtc, x.LastSuccessfulPollAtUtc, x.LastFailureAtUtc)).ToListAsync(ct);

    public async Task<int> CleanupWorkersAsync(DateTime retentionBeforeUtc, int maximum, CancellationToken ct)
    {
        var ids = await db.AIApplyWorkerInstances.Where(x => x.LastHeartbeatAtUtc < retentionBeforeUtc).OrderBy(x => x.LastHeartbeatAtUtc).Select(x => x.Id).Take(Math.Clamp(maximum, 1, 500)).ToListAsync(ct);
        if (ids.Count == 0) return 0;
        return await db.AIApplyWorkerInstances.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
    }

    public async Task<AIApplySiteOperationalSnapshot> GetSiteAsync(JobSiteIdentifier site, CancellationToken ct)
    {
        ValidateSite(site);
        var row = await db.AIApplySiteOperationalStates.AsNoTracking().SingleOrDefaultAsync(x => x.Site == site, ct);
        return row is null ? new(site, AIApplyCircuitState.Closed, 0, 0, null, null, null, null) : Snapshot(row);
    }

    public async Task<IReadOnlyList<AIApplySiteOperationalSnapshot>> GetSitesAsync(CancellationToken ct)
    {
        var rows = await db.AIApplySiteOperationalStates.AsNoTracking().OrderBy(x => x.Site).ToListAsync(ct);
        return rows.Select(Snapshot).ToList();
    }

    public Task<bool> TryAcquireSiteExecutionAsync(JobSiteIdentifier site, string workerId, DateTime nowUtc, CancellationToken ct) =>
        InSerializableTransactionAsync(site, row =>
        {
            if (row.ManualOverrideOpen == true) return Task.FromResult(false);
            if (row.ManualOverrideOpen == false) return Task.FromResult(true);
            if (row.CircuitState == AIApplyCircuitState.Closed) return Task.FromResult(true);
            if (row.CircuitState == AIApplyCircuitState.Open && row.CooldownUntilUtc > nowUtc) return Task.FromResult(false);
            if (row.HalfOpenProbeLeaseExpiresAtUtc > nowUtc && row.HalfOpenProbeOwner != workerId) return Task.FromResult(false);
            row.CircuitState = AIApplyCircuitState.HalfOpen; row.HalfOpenProbeOwner = workerId;
            row.HalfOpenProbeLeaseExpiresAtUtc = nowUtc.AddSeconds(Math.Clamp(options.Value.Observability.HalfOpenProbeLeaseSeconds, 10, 300)); row.Version++;
            return Task.FromResult(true);
        }, ct);

    public Task RecordSiteOutcomeAsync(JobSiteIdentifier site, string workerId, bool technicalSuccess, DateTime nowUtc, CancellationToken ct) =>
        InSerializableTransactionAsync(site, row =>
        {
            if (row.ManualOverrideOpen.HasValue) return Task.FromResult(true);
            var window = TimeSpan.FromMinutes(Math.Clamp(options.Value.Reliability.SiteObservationMinutes, 1, 1440));
            if (row.ObservationWindowStartedAtUtc == default || row.ObservationWindowStartedAtUtc < nowUtc - window)
            { row.ObservationWindowStartedAtUtc = nowUtc; row.SampleCount = 0; row.FailureCount = 0; }
            var ownsProbe = row.CircuitState == AIApplyCircuitState.HalfOpen && row.HalfOpenProbeOwner == workerId;
            if (ownsProbe)
            {
                row.HalfOpenProbeOwner = null; row.HalfOpenProbeLeaseExpiresAtUtc = null;
                if (technicalSuccess) { row.CircuitState = AIApplyCircuitState.Closed; row.CooldownUntilUtc = null; row.SampleCount = 0; row.FailureCount = 0; row.LastSuccessAtUtc = nowUtc; }
                else Open(row, nowUtc);
            }
            else if (row.CircuitState == AIApplyCircuitState.Closed)
            {
                row.SampleCount++; if (technicalSuccess) row.LastSuccessAtUtc = nowUtc; else { row.FailureCount++; row.LastFailureAtUtc = nowUtc; }
                var minimum = Math.Clamp(options.Value.Reliability.SiteHealthMinimumSamples, 2, 100);
                if (row.SampleCount >= minimum && (decimal)row.FailureCount / row.SampleCount >= Math.Clamp(options.Value.Reliability.SiteFailureThreshold, .1m, 1m)) Open(row, nowUtc);
            }
            row.Version++; return Task.FromResult(true);
        }, ct);

    public Task SetManualCircuitAsync(JobSiteIdentifier site, bool open, DateTime nowUtc, CancellationToken ct) =>
        InSerializableTransactionAsync(site, row =>
        {
            row.ManualOverrideOpen = open;
            if (open) Open(row, nowUtc); else { row.CircuitState = AIApplyCircuitState.Closed; row.CooldownUntilUtc = null; row.HalfOpenProbeOwner = null; row.HalfOpenProbeLeaseExpiresAtUtc = null; row.SampleCount = 0; row.FailureCount = 0; }
            row.Version++; return Task.FromResult(true);
        }, ct);

    private async Task<T> InSerializableTransactionAsync<T>(JobSiteIdentifier site, Func<AIApplySiteOperationalState, Task<T>> operation, CancellationToken ct)
    {
        ValidateSite(site);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [73_000L + (int)site], ct);
            var row = await db.AIApplySiteOperationalStates.SingleOrDefaultAsync(x => x.Site == site, ct);
            if (row is null) { row = new AIApplySiteOperationalState { Site = site, ObservationWindowStartedAtUtc = DateTime.UtcNow }; db.AIApplySiteOperationalStates.Add(row); }
            var result = await operation(row); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return result;
        });
    }

    private void Open(AIApplySiteOperationalState row, DateTime nowUtc) { row.CircuitState = AIApplyCircuitState.Open; row.OpenedAtUtc = nowUtc; row.CooldownUntilUtc = nowUtc.AddSeconds(Math.Clamp(options.Value.Reliability.CircuitCooldownSeconds, 10, 86400)); row.HalfOpenProbeOwner = null; row.HalfOpenProbeLeaseExpiresAtUtc = null; row.LastFailureAtUtc = nowUtc; }
    private static AIApplySiteOperationalSnapshot Snapshot(AIApplySiteOperationalState x) => new(x.Site, x.CircuitState, x.SampleCount, x.FailureCount, x.CooldownUntilUtc, x.LastSuccessAtUtc, x.LastFailureAtUtc, x.ManualOverrideOpen);
    private static void ValidateSite(JobSiteIdentifier site) { if (!Enum.IsDefined(site)) throw new ArgumentOutOfRangeException(nameof(site), "Unknown AI Apply site."); }
}
