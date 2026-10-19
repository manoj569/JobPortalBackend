using System.Text.Json;
using System.Linq.Expressions;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class JobSourceRunStore(JobPortalDbContext db) : IJobSourceRunStore
{
    public static Expression<Func<JobSourceRun, bool>> ActivePredicate => x =>
        x.Status == JobSourceRunStatus.Queued || x.Status == JobSourceRunStatus.Running || x.Status == JobSourceRunStatus.Paused ||
        x.Status == JobSourceRunStatus.Interrupted && x.AttemptCount < JobSourceRun.MaximumAttempts;

    public async Task<JobSourceRun> EnqueueAsync(Guid sourceId, Guid? requestedBy, DateTime now, CancellationToken ct)
    {
        var id = Guid.NewGuid(); // Same identifier on EF transient retries/unknown commit outcomes.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "JobSourceRuns" ("Id", "JobSourceId", "RequestedByUserId", "Status", "QueuedAtUtc", "NextAttemptAtUtc",
                  "AttemptCount", "Phase", "Processed", "TotalReceived", "Created", "Updated", "Unchanged", "Closed", "Matched",
                  "Skipped", "Failed", "Published", "NeedsReview", "QualityRejected", "PublishFailed", "AutoPublishDisabled")
                SELECT {id}, {sourceId}, {requestedBy}::uuid, 0, {now}, {now}, 0, 'Queued', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
                FROM "JobSources" WHERE "Id" = {sourceId} AND NOT "IsDeleted" AND "IsActive"
                ON CONFLICT DO NOTHING
                """, ct);
            var inserted = await GetAsync(sourceId, id, ct);
            if (inserted is not null) return inserted;
            var active = await db.JobSourceRuns.AsNoTracking().Where(ActivePredicate)
                .SingleOrDefaultAsync(x => x.JobSourceId == sourceId, ct);
            if (active is not null) return active;
            if (!await db.JobSources.AnyAsync(x => x.Id == sourceId && x.IsActive, ct))
                throw new BadRequestException("Job source is unavailable.", "job_source_unavailable");
            // The conflicting active run finished between INSERT and SELECT. Bounded retry.
        }
        throw new ConflictException("Please retry the run request.", "job_source_queue_busy");
    }

    public async Task<JobSourceRun> EnqueueSnapshotAsync(Guid sourceId, Guid snapshotId, Guid? requestedBy, DateTime now, string payload, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "JobSourceRuns" ("Id", "JobSourceId", "RequestedByUserId", "Status", "QueuedAtUtc", "NextAttemptAtUtc",
              "AttemptCount", "Phase", "Processed", "TotalReceived", "Created", "Updated", "Unchanged", "Closed", "Matched",
              "Skipped", "Failed", "Published", "NeedsReview", "QualityRejected", "PublishFailed", "AutoPublishDisabled", "CheckpointJson")
            SELECT {snapshotId}, {sourceId}, {requestedBy}::uuid, 0, {now}, {now}, 0, 'Queued', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, {payload}::jsonb
            FROM "JobSources" WHERE "Id" = {sourceId} AND NOT "IsDeleted" AND "IsActive" AND "AtsType" = 0 AND "AtsIdentifier" = 'employer-direct'
            ON CONFLICT DO NOTHING
            """, ct);
        var saved = await GetAsync(sourceId, snapshotId, ct);
        if (saved is null) throw new ConflictException("Source has another active run or is unavailable.", "job_source_queue_busy");
        var incoming = JsonSerializer.Deserialize<EmployerDirectSnapshotEnvelope>(payload, EmployerDirectSnapshotService.JsonOptions);
        var retained = saved.CheckpointJson is null ? null : JsonSerializer.Deserialize<EmployerDirectSnapshotEnvelope>(saved.CheckpointJson, EmployerDirectSnapshotService.JsonOptions);
        if (retained?.Fingerprint != incoming?.Fingerprint) throw new ConflictException("SnapshotId payload conflict.", "snapshot_payload_conflict");
        return saved;
    }

    public Task<JobSourceRun?> GetAsync(Guid sourceId, Guid runId, CancellationToken ct) => db.JobSourceRuns.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == runId && x.JobSourceId == sourceId, ct);

    public Task<bool> HasActiveAsync(Guid sourceId, CancellationToken ct) => db.JobSourceRuns.Where(ActivePredicate)
        .AnyAsync(x => x.JobSourceId == sourceId, ct);

    public async Task<IReadOnlyList<JobSourceRun>> CandidatesAsync(DateTime now, CancellationToken ct) =>
        await CandidatesQuery(now).ToArrayAsync(ct);

    internal IQueryable<JobSourceRun> CandidatesQuery(DateTime now) => db.JobSourceRuns.AsNoTracking()
        .Where(x => (x.Status == JobSourceRunStatus.Queued || x.Status == JobSourceRunStatus.Paused || x.Status == JobSourceRunStatus.Interrupted && x.AttemptCount < JobSourceRun.MaximumAttempts)
            && x.NextAttemptAtUtc <= now || x.Status == JobSourceRunStatus.Running && x.LeaseExpiresAtUtc <= now)
        .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.QueuedAtUtc).ThenBy(x => x.Id).Take(25);

    public async Task<JobSourceRun?> TryClaimAsync(Guid runId, Guid owner, DateTime now, DateTime expiry, CancellationToken ct)
    {
        await db.JobSourceRuns.Where(x => x.Id == runId && x.NextAttemptAtUtc <= now && x.AttemptCount < JobSourceRun.MaximumAttempts &&
            x.ExecutionCount < 1000 && (x.Status == JobSourceRunStatus.Queued || x.Status == JobSourceRunStatus.Paused || x.Status == JobSourceRunStatus.Interrupted))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, JobSourceRunStatus.Running)
                .SetProperty(x => x.LeaseOwner, owner).SetProperty(x => x.LeaseExpiresAtUtc, expiry)
                .SetProperty(x => x.HeartbeatAtUtc, now).SetProperty(x => x.StartedAtUtc, x => x.StartedAtUtc ?? now)
                .SetProperty(x => x.CompletedAtUtc, (DateTime?)null).SetProperty(x => x.ErrorCode, (string?)null)
                .SetProperty(x => x.DiagnosticsJson, x => x.CheckpointJson == null ? null : x.DiagnosticsJson)
                .SetProperty(x => x.Phase, "ProviderFetch").SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                .SetProperty(x => x.ExecutionCount, x => x.ExecutionCount + 1)
                .SetProperty(x => x.Processed, x => x.CheckpointJson == null ? 0 : x.Processed)
                .SetProperty(x => x.TotalReceived, x => x.CheckpointJson == null ? 0 : x.TotalReceived)
                .SetProperty(x => x.Created, x => x.CheckpointJson == null ? 0 : x.Created)
                .SetProperty(x => x.Updated, x => x.CheckpointJson == null ? 0 : x.Updated)
                .SetProperty(x => x.Unchanged, x => x.CheckpointJson == null ? 0 : x.Unchanged)
                .SetProperty(x => x.Closed, x => x.CheckpointJson == null ? 0 : x.Closed)
                .SetProperty(x => x.Matched, x => x.CheckpointJson == null ? 0 : x.Matched)
                .SetProperty(x => x.Skipped, x => x.CheckpointJson == null ? 0 : x.Skipped)
                .SetProperty(x => x.Failed, x => x.CheckpointJson == null ? 0 : x.Failed)
                .SetProperty(x => x.Published, x => x.CheckpointJson == null ? 0 : x.Published)
                .SetProperty(x => x.NeedsReview, x => x.CheckpointJson == null ? 0 : x.NeedsReview)
                .SetProperty(x => x.QualityRejected, x => x.CheckpointJson == null ? 0 : x.QualityRejected)
                .SetProperty(x => x.PublishFailed, x => x.CheckpointJson == null ? 0 : x.PublishFailed)
                .SetProperty(x => x.AutoPublishDisabled, x => x.CheckpointJson == null ? 0 : x.AutoPublishDisabled), ct);
        // Also confirms a claim after an ambiguous/transient SQL response.
        return await db.JobSourceRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId &&
            x.Status == JobSourceRunStatus.Running && x.LeaseOwner == owner && x.LeaseExpiresAtUtc > now, ct);
    }

    private IQueryable<JobSourceRun> Owned(JobSourceRun run, DateTime now) => db.JobSourceRuns.Where(x =>
        x.Id == run.Id && x.Status == JobSourceRunStatus.Running && x.LeaseOwner == run.LeaseOwner && x.LeaseExpiresAtUtc > now);

    public async Task<bool> HeartbeatAsync(JobSourceRun run, DateTime now, DateTime expiry, JobSourceRunProgressSnapshot p, CancellationToken ct) =>
        await Owned(run, now).ExecuteUpdateAsync(s => s.SetProperty(x => x.HeartbeatAtUtc, now)
            .SetProperty(x => x.LeaseExpiresAtUtc, expiry).SetProperty(x => x.Phase, p.Phase)
            .SetProperty(x => x.Processed, p.Counters.TotalReceived).SetProperty(x => x.TotalReceived, p.Counters.TotalReceived)
            .SetProperty(x => x.Created, p.Counters.Created).SetProperty(x => x.Updated, p.Counters.Updated)
            .SetProperty(x => x.Unchanged, p.Counters.Unchanged).SetProperty(x => x.Closed, p.Counters.Closed)
            .SetProperty(x => x.Matched, p.Counters.Matched).SetProperty(x => x.Skipped, p.Counters.Skipped)
            .SetProperty(x => x.Failed, p.Counters.Failed).SetProperty(x => x.Published, p.Counters.Published)
            .SetProperty(x => x.NeedsReview, p.Counters.NeedsReview).SetProperty(x => x.QualityRejected, p.Counters.QualityRejected)
            .SetProperty(x => x.PublishFailed, p.Counters.PublishFailed).SetProperty(x => x.AutoPublishDisabled, p.Counters.AutoPublishDisabled)
            .SetProperty(x => x.DiagnosticsJson, JsonSerializer.Serialize(p.Counters, (JsonSerializerOptions?)null)), ct) == 1;

    public async Task RecoverAsync(Guid runId, DateTime now, DateTime retryAt, CancellationToken ct) =>
        await db.JobSourceRuns.Where(x => x.Id == runId && x.Status == JobSourceRunStatus.Running && x.LeaseExpiresAtUtc <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, x => x.ExecutionCount >= 1000 ? JobSourceRunStatus.Failed : x.CheckpointJson != null && x.AttemptCount < JobSourceRun.MaximumAttempts
                    ? JobSourceRunStatus.Paused : JobSourceRunStatus.Interrupted)
                .SetProperty(x => x.InterruptedAtUtc, now).SetProperty(x => x.NextAttemptAtUtc, retryAt)
                .SetProperty(x => x.CompletedAtUtc, x => x.AttemptCount >= JobSourceRun.MaximumAttempts || x.ExecutionCount >= 1000 ? now : (DateTime?)null)
                .SetProperty(x => x.ErrorCode, "worker_lease_expired").SetProperty(x => x.Phase, "Interrupted")
                .SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), ct);

    public async Task<bool> FinishAsync(JobSourceRun run, JobSourceRunStatus status, DateTime now, DateTime retryAt,
        JobSourceRunProgressSnapshot p, CancellationToken ct)
    {
        if (status is not (JobSourceRunStatus.Succeeded or JobSourceRunStatus.Failed or JobSourceRunStatus.Interrupted or
            JobSourceRunStatus.Paused or JobSourceRunStatus.Unsafe or JobSourceRunStatus.Cancelled))
            throw new ArgumentOutOfRangeException(nameof(status));
        var previousCursor = run.CheckpointJson is null ? 0 : JsonSerializer.Deserialize<JobPortal.Application.Abstractions.Jobs.WorkdayRunCheckpoint>(run.CheckpointJson)?.Cursor ?? 0;
        var progressed = p.Counters.CheckpointCursor > previousCursor;
        var attempts = progressed ? 0 : run.AttemptCount;
        if (status == JobSourceRunStatus.Paused && (attempts >= JobSourceRun.MaximumAttempts || run.ExecutionCount >= 1000))
        {
            status = JobSourceRunStatus.Failed;
            p = p with { Counters = p.Counters with { ContinuationPending = false,
                StopReason = run.ExecutionCount >= 1000 ? "MaximumExecutionsExceeded" : "NoProgressAttemptsExhausted" } };
        }
        return await Owned(run, now).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
            .SetProperty(x => x.AttemptCount, attempts)
            .SetProperty(x => x.CompletedAtUtc, status == JobSourceRunStatus.Paused || status == JobSourceRunStatus.Interrupted && attempts < JobSourceRun.MaximumAttempts ? (DateTime?)null : now)
            .SetProperty(x => x.InterruptedAtUtc, status == JobSourceRunStatus.Interrupted || status == JobSourceRunStatus.Paused ? now : run.InterruptedAtUtc)
            .SetProperty(x => x.NextAttemptAtUtc, retryAt).SetProperty(x => x.Phase, status.ToString())
            .SetProperty(x => x.ErrorCode, status == JobSourceRunStatus.Succeeded ? null :
                status == JobSourceRunStatus.Paused ? "continuation_pending" : status == JobSourceRunStatus.Unsafe ? "unsafe_snapshot" :
                status == JobSourceRunStatus.Cancelled ? "source_unavailable" : status == JobSourceRunStatus.Interrupted ? "worker_interrupted" : "source_run_failed")
            .SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
            .SetProperty(x => x.Processed, p.Counters.TotalReceived).SetProperty(x => x.TotalReceived, p.Counters.TotalReceived)
            .SetProperty(x => x.Created, p.Counters.Created).SetProperty(x => x.Updated, p.Counters.Updated)
            .SetProperty(x => x.Unchanged, p.Counters.Unchanged).SetProperty(x => x.Closed, p.Counters.Closed)
            .SetProperty(x => x.Matched, p.Counters.Matched).SetProperty(x => x.Skipped, p.Counters.Skipped)
            .SetProperty(x => x.Failed, p.Counters.Failed).SetProperty(x => x.Published, p.Counters.Published)
            .SetProperty(x => x.NeedsReview, p.Counters.NeedsReview).SetProperty(x => x.QualityRejected, p.Counters.QualityRejected)
            .SetProperty(x => x.PublishFailed, p.Counters.PublishFailed).SetProperty(x => x.AutoPublishDisabled, p.Counters.AutoPublishDisabled)
            .SetProperty(x => x.DiagnosticsJson, JsonSerializer.Serialize(p.Counters, (JsonSerializerOptions?)null)), ct) == 1;
    }

}
