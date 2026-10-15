using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.FresherAcquisition;

public sealed record FresherReceiptInput(int Index, FresherImportCandidate Candidate, string IdentityKey,
    string EligibilityDecision, FresherImportItemOutcome Outcome, DateTime SeenAtUtc);

public interface IFresherAcquisitionStore
{
    Task<AcquisitionSource?> GetEnabledSourceAsync(string providerKey, CancellationToken cancellationToken);
    Task<AcquisitionSource?> GetSourceByIdAsync(Guid sourceId, CancellationToken cancellationToken);
    Task<(AcquisitionImportRun Run, bool IsReplay)> StartRunAsync(Guid sourceId, string batchId,
        string correlationId, int received, CancellationToken cancellationToken);
    Task SaveReceiptsAsync(Guid runId, IReadOnlyCollection<FresherReceiptInput> receipts,
        CancellationToken cancellationToken);
    Task CompleteRunAsync(Guid runId, FresherBulkImportResponse response, CancellationToken cancellationToken);
    Task<FresherBulkImportResponse?> GetReplayResponseAsync(Guid runId, string batchId,
        CancellationToken cancellationToken);
}
