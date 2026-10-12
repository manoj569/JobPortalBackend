using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Jobs;

/// <summary>A complete-source-capable provider's current view and whether every listing was read.</summary>
public sealed record ExternalJobSourceSnapshot(
    IReadOnlyCollection<RawExternalJob> Jobs,
    int Skipped,
    bool IsComplete)
{
    public int? CheckpointCursor { get; init; }
    public bool ContinuationPending { get; init; }
    public WorkdayListingDiagnostics? ListingDiagnostics { get; init; }
    // Selection diagnostics only: these items never reached ingestion validation.
    public IReadOnlyDictionary<string, int>? SelectionReasonCounts { get; init; }
    public IReadOnlyDictionary<string, int>? EligibilityReasonCounts { get; init; }
}

// Count equality is insufficient to authorize absence reconciliation after observed drift.
public sealed record WorkdayListingDiagnostics
{
    public int InitialReportedTotal { get; init; }
    public int? FinalReportedTotal { get; init; }
    public int RawSlots { get; init; }
    public int UniquePaths { get; init; }
    public int DuplicatePaths { get; init; }
    public int PlaceholderCount { get; init; }
    public bool FirstPageChanged { get; init; }
    public int LastOffset { get; init; }
    public int LastPageSize { get; init; }
    public int PageCount { get; init; }
    public bool SnapshotStable { get; init; }
    public bool IngestionSafe { get; init; }
    public bool ReconciliationSafe { get; init; }
    public string ReasonCode { get; init; } = "SnapshotIncomplete";
    public bool RetryRecommended { get; init; }
}

/// <summary>
/// Optional extension for sources whose complete snapshot can be reconciled safely.
/// Implementations must set IsComplete=false when any listing/detail was omitted.
/// </summary>
public interface ICompleteExternalJobProvider : IExternalJobProvider
{
    Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(
        JobSource source,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Bounded batches with backpressure. Only the final batch may certify completeness
/// of the entire enumeration; early disposal, cancellation or failure is incomplete.
/// Jobs in earlier batches are durable replay checkpoints, not evidence of absence.
/// </summary>
public interface IBatchedExternalJobProvider : ICompleteExternalJobProvider
{
    IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(
        JobSource source, CancellationToken cancellationToken = default);
}
