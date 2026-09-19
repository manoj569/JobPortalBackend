namespace JobPortal.Application.Abstractions.Jobs;

/// <summary>
/// Provides cross-instance coordination for execution of a job source.
/// A non-null lease means the caller owns the lock until the lease is disposed.
/// Null means another application instance currently owns the lock.
/// </summary>
public interface IJobSourceExecutionLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        Guid jobSourceId,
        CancellationToken cancellationToken = default);
}
