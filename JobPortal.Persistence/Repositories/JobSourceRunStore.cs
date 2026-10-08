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
        x.Status == JobSourceRunStatus.Queued || x.Status == JobSourceRunStatus.Running ||
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

    public Task<JobSourceRun?> GetAsync(Guid sourceId, Guid runId, CancellationToken ct) => db.JobSourceRuns.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == runId && x.JobSourceId == sourceId, ct);

    public Task<bool> HasActiveAsync(Guid sourceId, CancellationToken ct) => db.JobSourceRuns.Where(ActivePredicate)
        .AnyAsync(x => x.JobSourceId == sourceId, ct);

    public async Task<IReadOnlyList<JobSourceRun>> CandidatesAsync(DateTime now, CancellationToken ct) =>
        await CandidatesQuery(now).ToArrayAsync(ct);

    internal IQueryable<JobSourceRun> CandidatesQuery(DateTime now) => db.JobSourceRuns.AsNoTracking()
        .Where(x => (x.Status == JobSourceRunStatus.Queued || x.Status == JobSourceRunStatus.Interrupted && x.AttemptCount < JobSourceRun.MaximumAttempts)
            && x.NextAttemptAtUtc <= now || x.Status == JobSourceRunStatus.Running && x.LeaseExpiresAtUtc <= now)
        .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.QueuedAtUtc).ThenBy(x => x.Id).Take(25);

    public async Task<JobSourceRun?> TryClaimAsync(Guid runId, Guid owner, DateTime now, DateTime expiry, CancellationToken ct)
    {
        await db.JobSourceRuns.Where(x => x.Id == runId && x.NextAttemptAtUtc <= now && x.AttemptCount < JobSourceRun.MaximumAttempts &&
            (x.Status == JobSourceRunStatus.Queued || x.Status == JobSourceRunStatus.Interrupted))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, JobSourceRunStatus.Running)
                .SetProperty(x => x.LeaseOwner, owner).SetProperty(x => x.LeaseExpiresAtUtc, expiry)
                .SetProperty(x => x.HeartbeatAtUtc, now).SetProperty(x => x.StartedAtUtc, now)
                .SetProperty(x => x.CompletedAtUtc, (DateTime?)null).SetProperty(x => x.ErrorCode, (string?)null)
                .SetProperty(x => x.Phase, "ProviderFetch").SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                .SetProperty(x => x.Processed, 0).SetProperty(x => x.TotalReceived, 0).SetProperty(x => x.Created, 0)
                .SetProperty(x => x.Updated, 0).SetProperty(x => x.Unchanged, 0).SetProperty(x => x.Closed, 0)
                .SetProperty(x => x.Matched, 0).SetProperty(x => x.Skipped, 0).SetProperty(x => x.Failed, 0)
                .SetProperty(x => x.Published, 0).SetProperty(x => x.NeedsReview, 0).SetProperty(x => x.QualityRejected, 0)
                .SetProperty(x => x.PublishFailed, 0).SetProperty(x => x.AutoPublishDisabled, 0), ct);
        // Also confirms a claim after an ambiguous/transient SQL response.
        return await db.JobSourceRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId &&
            x.Status == JobSourceRunStatus.Running && x.LeaseOwner == owner && x.LeaseExpiresAtUtc > now, ct);
    }

    private IQueryable<JobSourceRun> Owned(JobSourceRun run, DateTime now) => db.JobSourceRuns.Where(x =>
        x.Id == run.Id && x.Status == JobSourceRunStatus.Running && x.LeaseOwner == run.LeaseOwner && x.LeaseExpiresAtUtc > now);

    public async Task<bool> HeartbeatAsync(JobSourceRun run, DateTime now, DateTime expiry, JobSourceRunProgressSnapshot p, CancellationToken ct) =>
        await Owned(run, now).ExecuteUpdateAsync(s => s.SetProperty(x => x.HeartbeatAtUtc, now)
            .SetProperty(x => x.LeaseExpiresAtUtc, expiry).SetProperty(x => x.Phase, p.Phase)
            .SetProperty(x => x.Processed, p.Processed).SetProperty(x => x.TotalReceived, p.Counters.TotalReceived)
            .SetProperty(x => x.Created, p.Counters.Created).SetProperty(x => x.Updated, p.Counters.Updated)
            .SetProperty(x => x.Unchanged, p.Counters.Unchanged).SetProperty(x => x.Closed, p.Counters.Closed)
            .SetProperty(x => x.Matched, p.Counters.Matched).SetProperty(x => x.Skipped, p.Counters.Skipped)
            .SetProperty(x => x.Failed, p.Counters.Failed).SetProperty(x => x.Published, p.Counters.Published)
            .SetProperty(x => x.NeedsReview, p.Counters.NeedsReview).SetProperty(x => x.QualityRejected, p.Counters.QualityRejected)
            .SetProperty(x => x.PublishFailed, p.Counters.PublishFailed).SetProperty(x => x.AutoPublishDisabled, p.Counters.AutoPublishDisabled), ct) == 1;

    public async Task RecoverAsync(Guid runId, DateTime now, DateTime retryAt, CancellationToken ct) =>
        await db.JobSourceRuns.Where(x => x.Id == runId && x.Status == JobSourceRunStatus.Running && x.LeaseExpiresAtUtc <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, JobSourceRunStatus.Interrupted)
                .SetProperty(x => x.InterruptedAtUtc, now).SetProperty(x => x.NextAttemptAtUtc, retryAt)
                .SetProperty(x => x.CompletedAtUtc, x => x.AttemptCount >= JobSourceRun.MaximumAttempts ? now : (DateTime?)null)
                .SetProperty(x => x.ErrorCode, "worker_lease_expired").SetProperty(x => x.Phase, "Interrupted")
                .SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), ct);

    public async Task<bool> FinishAsync(JobSourceRun run, JobSourceRunStatus status, DateTime now, DateTime retryAt,
        JobSourceRunProgressSnapshot p, CancellationToken ct)
    {
        if (status is not (JobSourceRunStatus.Succeeded or JobSourceRunStatus.Failed or JobSourceRunStatus.Interrupted))
            throw new ArgumentOutOfRangeException(nameof(status));
        return await Owned(run, now).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
            .SetProperty(x => x.CompletedAtUtc, status == JobSourceRunStatus.Interrupted && run.AttemptCount < JobSourceRun.MaximumAttempts ? (DateTime?)null : now)
            .SetProperty(x => x.InterruptedAtUtc, status == JobSourceRunStatus.Interrupted ? now : run.InterruptedAtUtc)
            .SetProperty(x => x.NextAttemptAtUtc, retryAt).SetProperty(x => x.Phase, status.ToString())
            .SetProperty(x => x.ErrorCode, status == JobSourceRunStatus.Succeeded ? null :
                status == JobSourceRunStatus.Interrupted ? "worker_interrupted" : "source_run_failed")
            .SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
            .SetProperty(x => x.Processed, p.Processed).SetProperty(x => x.TotalReceived, p.Counters.TotalReceived)
            .SetProperty(x => x.Created, p.Counters.Created).SetProperty(x => x.Updated, p.Counters.Updated)
            .SetProperty(x => x.Unchanged, p.Counters.Unchanged).SetProperty(x => x.Closed, p.Counters.Closed)
            .SetProperty(x => x.Matched, p.Counters.Matched).SetProperty(x => x.Skipped, p.Counters.Skipped)
            .SetProperty(x => x.Failed, p.Counters.Failed).SetProperty(x => x.Published, p.Counters.Published)
            .SetProperty(x => x.NeedsReview, p.Counters.NeedsReview).SetProperty(x => x.QualityRejected, p.Counters.QualityRejected)
            .SetProperty(x => x.PublishFailed, p.Counters.PublishFailed).SetProperty(x => x.AutoPublishDisabled, p.Counters.AutoPublishDisabled), ct) == 1;
    }

}
