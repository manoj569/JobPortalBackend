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
