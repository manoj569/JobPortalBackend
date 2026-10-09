using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Jobs;

/// <summary>A complete-source-capable provider's current view and whether every listing was read.</summary>
public sealed record ExternalJobSourceSnapshot(
    IReadOnlyCollection<RawExternalJob> Jobs,
    int Skipped,
    bool IsComplete);

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
