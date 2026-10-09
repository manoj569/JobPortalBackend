using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.JobAggregation;

public sealed record JobSourceRunProgressSnapshot(string Phase, int Processed, JobSourceRunResult Counters);

public interface IJobSourceRunProgress
{
    JobSourceRunProgressSnapshot Snapshot { get; }
    void Report(JobSourceRunProgressSnapshot snapshot);
}

public sealed class JobSourceRunProgress : IJobSourceRunProgress
{
    private JobSourceRunProgressSnapshot current = new("ProviderFetch", 0, new());
    public JobSourceRunProgressSnapshot Snapshot => Volatile.Read(ref current);
    public void Report(JobSourceRunProgressSnapshot snapshot) => Volatile.Write(ref current, snapshot);
}

public interface IJobSourceRunStore
{
    Task<JobSourceRun> EnqueueAsync(Guid sourceId, Guid? requestedBy, DateTime now, CancellationToken ct);
    Task<JobSourceRun?> GetAsync(Guid sourceId, Guid runId, CancellationToken ct);
    Task<bool> HasActiveAsync(Guid sourceId, CancellationToken ct);
    Task<IReadOnlyList<JobSourceRun>> CandidatesAsync(DateTime now, CancellationToken ct);
    Task<JobSourceRun?> TryClaimAsync(Guid runId, Guid owner, DateTime now, DateTime expiry, CancellationToken ct);
    Task<bool> HeartbeatAsync(JobSourceRun run, DateTime now, DateTime expiry, JobSourceRunProgressSnapshot p, CancellationToken ct);
    // Called ONLY under the shared source execution lock. Live but delayed workers cannot be recovered concurrently.
    Task RecoverAsync(Guid runId, DateTime now, DateTime retryAt, CancellationToken ct);
    Task<bool> FinishAsync(JobSourceRun run, JobSourceRunStatus status, DateTime now, DateTime retryAt,
        JobSourceRunProgressSnapshot p, CancellationToken ct);
}

public sealed record JobSourceRunResponse(
    Guid RunId, Guid JobSourceId, string Status, int AttemptCount, bool RetryPending,
    DateTime QueuedAtUtc, DateTime? StartedAtUtc, DateTime? CompletedAtUtc, DateTime? InterruptedAtUtc,
    DateTime NextAttemptAtUtc, DateTime? HeartbeatAtUtc, string Phase, int Processed,
    int TotalReceived, int Created, int Updated, int Unchanged, int Closed, int Matched, int Skipped, int Failed,
    int Published, int NeedsReview, int QualityRejected, int PublishFailed, int AutoPublishDisabled,
    bool Succeeded, string? Error)
{
    public string StatusUrl => $"/api/admin/job-sources/{JobSourceId:D}/runs/{RunId:D}";
    public int ExistingDuplicate => Matched;
    public int Rejected => Skipped;
    public static JobSourceRunResponse From(JobSourceRun row) => new(row.Id, row.JobSourceId, row.Status.ToString(),
        row.AttemptCount, row.Status == JobSourceRunStatus.Interrupted && row.AttemptCount < JobSourceRun.MaximumAttempts,
        row.QueuedAtUtc, row.StartedAtUtc, row.CompletedAtUtc, row.InterruptedAtUtc, row.NextAttemptAtUtc,
        row.HeartbeatAtUtc, row.Phase, row.Processed, row.TotalReceived, row.Created, row.Updated, row.Unchanged,
        row.Closed, row.Matched, row.Skipped, row.Failed, row.Published, row.NeedsReview, row.QualityRejected,
        row.PublishFailed, row.AutoPublishDisabled, row.Status == JobSourceRunStatus.Succeeded,
        row.ErrorCode); // Fixed internal codes only; no provider messages/URLs.
}

public sealed class JobSourceRunService(IJobSourceRepository sources, IJobSourceRunStore store, TimeProvider clock)
{
    public async Task<JobSourceRunResponse> EnqueueAsync(Guid sourceId, Guid? requestedBy, CancellationToken ct)
    {
        var source = await sources.GetByIdAsync(sourceId, ct)
            ?? throw new NotFoundException("Job source was not found.");
        if (!source.IsActive) throw new BadRequestException("Job source is inactive.", "job_source_inactive");
        return JobSourceRunResponse.From(await store.EnqueueAsync(sourceId, requestedBy, clock.GetUtcNow().UtcDateTime, ct));
    }

    public async Task<JobSourceRunResponse> GetAsync(Guid sourceId, Guid runId, CancellationToken ct) =>
        JobSourceRunResponse.From(await store.GetAsync(sourceId, runId, ct)
            ?? throw new NotFoundException("Job source run was not found."));
}
