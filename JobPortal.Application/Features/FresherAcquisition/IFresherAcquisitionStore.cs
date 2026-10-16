using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.FresherAcquisition;

public sealed record FresherReceiptInput(int Index, FresherImportCandidate? Candidate, string IdentityKey,
    string EligibilityDecision, FresherImportItemOutcome Outcome, DateTime SeenAtUtc, bool PublicationRequested = false);
public sealed record FresherImportReceiptSnapshot(FresherImportItemOutcome Outcome, string PublicationStatus, string? PublicationError);
public sealed record FresherCompanyResolution(Guid? CompanyId, string? ReasonCode = null);

public sealed class ImportIdPayloadConflictException() : Exception("IMPORT_ID_PAYLOAD_CONFLICT");
public sealed class ImportAlreadyRunningException() : Exception("IMPORT_IN_PROGRESS");
public sealed class NoopAsyncDisposable : IAsyncDisposable
{
    public static NoopAsyncDisposable Instance { get; } = new();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
public sealed class AsyncDisposableHolder : IAsyncDisposable
{
    private IAsyncDisposable? _inner;
    public void Set(IAsyncDisposable inner) => _inner = inner;
    public async ValueTask DisposeAsync() { if (_inner is not null) await _inner.DisposeAsync(); }
}

public interface IFresherAcquisitionChunk : IAsyncDisposable
{
    Task CommitAsync(Guid runId, IReadOnlyCollection<FresherReceiptInput> receipts, CancellationToken cancellationToken);
}

public interface IFresherAcquisitionStore
{
    Task PopulateMissingLogoAsync(Guid companyId, string url, CancellationToken cancellationToken) => Task.CompletedTask;
    Task<FresherImportStatus?> GetImportStatusAsync(string providerKey, string importId, CancellationToken cancellationToken) => Task.FromResult<FresherImportStatus?>(null);
    Task<FresherCompanyResolution> ResolveCompanyAsync(FresherImportCandidate candidate, CancellationToken cancellationToken);
    Task<FresherCompanyResolution> ResolveWebsiteCompanyAsync(FresherImportCandidate candidate, CancellationToken cancellationToken) => ResolveCompanyAsync(candidate, cancellationToken);
    Task<T> ExecuteImportAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) => operation(cancellationToken);
    Task<AcquisitionSource?> GetEnabledSourceAsync(string providerKey, CancellationToken cancellationToken);
    Task<AcquisitionSource?> GetSourceByIdAsync(Guid sourceId, CancellationToken cancellationToken);
    Task<(AcquisitionImportRun Run, bool IsReplay)> StartRunAsync(Guid sourceId, string batchId,
        string correlationId, string requestFingerprint, int received, CancellationToken cancellationToken);
    Task<(AcquisitionImportRun Run, bool IsReplay)> StartPayloadRunAsync(Guid sourceId, string batchId,
        string correlationId, string payloadFingerprint, string legacyFingerprint, int received, CancellationToken cancellationToken) =>
        StartRunAsync(sourceId, batchId, correlationId, payloadFingerprint, received, cancellationToken);
    Task CompleteRunAsync(Guid runId, FresherBulkImportResponse response, CancellationToken cancellationToken);
    Task<FresherBulkImportResponse?> GetReplayResponseAsync(Guid runId, string batchId,
        CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, FresherImportReceiptSnapshot>> GetReceiptsAsync(Guid runId, CancellationToken cancellationToken);
    Task<IFresherAcquisitionChunk> BeginChunkAsync(IReadOnlyCollection<string> externalIdentityLocks, CancellationToken cancellationToken);
    Task<IAsyncDisposable> AcquireRunLockAsync(Guid sourceId, string importId, CancellationToken cancellationToken) =>
        Task.FromResult<IAsyncDisposable>(NoopAsyncDisposable.Instance);
    Task UpdatePublicationAsync(Guid runId, string candidateIdentityKey, string status, string? error, CancellationToken cancellationToken) => Task.CompletedTask;
}
