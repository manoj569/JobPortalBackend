using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Jobs;

/// <summary>A complete-source-capable provider's current view and whether every listing was read.</summary>
public sealed record ExternalJobSourceSnapshot(
    IReadOnlyCollection<RawExternalJob> Jobs,
    int Skipped,
    bool IsComplete)
{
    // Optional complete presence census. Never an ingestion/publication payload.
    // Null preserves the legacy Jobs-derived reconciliation path exactly.
    public IReadOnlyCollection<string>? ObservedExternalIds { get; init; }
    public ExternalJobInventoryEvidence? InventoryEvidence { get; init; }
    public DateTime? InventoryAtUtc { get; init; }
    public string? InventoryVersion { get; init; }
    public string? InventoryDigest { get; init; }
    public DateTime? ObservedAtUtc { get; init; }
    public int? CheckpointCursor { get; init; }
    public bool ContinuationPending { get; init; }
    public WorkdayListingDiagnostics? ListingDiagnostics { get; init; }
    public int? ReportedListingTotal { get; init; }
    public IReadOnlyDictionary<string, int>? ProviderReasonCounts { get; init; }
    // Selection diagnostics only: these items never reached ingestion validation.
    public IReadOnlyDictionary<string, int>? SelectionReasonCounts { get; init; }
    public IReadOnlyDictionary<string, int>? EligibilityReasonCounts { get; init; }
    public bool SelectionLimited { get; init; }
}

// Workday progress and anomalies are diagnostic; they never authorize absence reconciliation.
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
    public bool ProcessingComplete { get; init; }
    public bool InfrastructureFailure { get; init; }
    public bool SnapshotStable { get; init; }
    public bool IngestionSafe { get; init; }
    public bool ReconciliationSafe { get; init; }
    public string ReasonCode { get; init; } = "SnapshotIncomplete";
    public bool RetryRecommended { get; init; }
}

/// <summary>
/// Optional extension for sources whose complete snapshot can be reconciled safely.
/// Legacy snapshots must include every observed job. An optional validated complete
/// presence census can cover jobs enriched in earlier runs; incomplete enumeration,
/// skipped data, or unproven identity coverage must still set IsComplete=false.
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
