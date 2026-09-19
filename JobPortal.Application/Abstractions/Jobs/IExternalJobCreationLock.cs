namespace JobPortal.Application.Abstractions.Jobs;

public interface IExternalJobCreationLock
{
    // Held across the second dedup check and save, never across provider HTTP.
    Task<IAsyncDisposable> AcquireAsync(string? canonicalUrl, string fingerprintHash, CancellationToken cancellationToken = default);
}
