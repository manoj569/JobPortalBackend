using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Jobs;

public sealed record WorkdayRunCheckpoint
{
    public int Version { get; init; } = 1;
    public string SourceIdentity { get; init; } = string.Empty;
    public string ListingIdentity { get; init; } = string.Empty;
    public int ListingTotal { get; init; }
    public int ListingPages { get; init; }
    public int Cursor { get; init; }
    public int ProviderSkipped { get; init; }
    public DateTime CapturedAtUtc { get; init; }
    public DateTime CommittedAtUtc { get; init; }
    public IReadOnlyCollection<RawExternalJob> ObservedJobs { get; init; } = [];
    public JobSourceRunResult? CompletedResult { get; init; }
    public WorkdayListingDiagnostics? ListingDiagnostics { get; init; }
    // Version 3 fences old binaries from ordinal receipts retired by a rebase.
    public int RebaseCount { get; init; }
    public JobSourceRunResult? ArchivedCounters { get; init; }
}

public interface IJobSourceRunCheckpoint
{
    WorkdayDetailProgress Details { get; }
    Guid? RunId { get; }
    bool Enabled { get; }
    WorkdayRunCheckpoint? State { get; }
    Task BindAsync(JobSourceRun run, CancellationToken ct);
    void Enable(JobSource source);
    Task PrepareAsync(string sourceIdentity, string listingIdentity, int total, int pages, CancellationToken ct,
        WorkdayListingDiagnostics? listingDiagnostics = null);
    Task CommitAsync(int cursor, int providerSkipped, IReadOnlyCollection<RawExternalJob> observed, CancellationToken ct);
    Task RefreshAsync(CancellationToken ct);
    JobIngestionResult? Replay(RawExternalJob raw);
    JobAutoPublishResult? ReplayedPublication(RawExternalJob raw);
    void SetCurrentItem(RawExternalJob raw);
    void StageIngestion(RawExternalJob raw, JobIngestionResult result);
    void ConfirmSaved();
    void StageCompletedResult(JobSourceRunResult result);
    void StagePublication(Guid jobId, JobAutoPublishResult result);
    Task CompleteItemAsync(RawExternalJob raw, JobIngestionResult result, JobAutoPublishResult? publication, CancellationToken ct);
    JobSourceRunResult CommittedCounters { get; }
}

// Scoped provider evidence, separate from the durable committed cursor.
public sealed class WorkdayDetailProgress
{
    private readonly object gate = new();
    private int attempted, succeeded, failed;
    private bool started;
    public void Begin(int committedCursor, int parsed, int skipped)
    { lock (gate) { attempted = committedCursor; succeeded = parsed; failed = skipped; started = true; } }
    public void RecordAttempt() { lock (gate) attempted++; }
    public void RecordSuccess() { lock (gate) succeeded++; }
    public void RecordFailure() { lock (gate) failed++; }
    public JobSourceRunResult ApplyTo(JobSourceRunResult result)
    {
        lock (gate) return !started ? result : result with
        { DetailAttempted = attempted, DetailSucceeded = succeeded, DetailFailed = failed };
    }
}

public sealed class JobSourceSnapshotUnsafeException(string reason, WorkdayListingDiagnostics? listingDiagnostics = null) : Exception("Source snapshot is unsafe.")
{
    public string ReasonCode { get; } = reason;
    public WorkdayListingDiagnostics? ListingDiagnostics { get; } = listingDiagnostics;
}

public sealed class JobSourceContinuationException(string reason) : Exception("Source execution requires continuation.")
{
    public string ReasonCode { get; } = reason;
}
