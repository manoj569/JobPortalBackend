namespace JobPortal.Application.Abstractions.Jobs;

public interface IExternalJobCreationLock
{
    // Held across the second dedup check and save, never across provider HTTP.
    Task<IAsyncDisposable> AcquireAsync(string? canonicalUrl, string fingerprintHash, CancellationToken cancellationToken = default);
}

// Optional optimization for sequential source runs. Each item still acquires/releases
// the exact existing keys around a fresh dedup check and its durable save.
public interface IExternalJobCreationLockRunFactory
{
    IExternalJobCreationLockRun CreateRun();
}

public interface IExternalJobCreationLockRun : IExternalJobCreationLock, IAsyncDisposable { }
