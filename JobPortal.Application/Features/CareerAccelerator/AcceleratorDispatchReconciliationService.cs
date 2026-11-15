using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

// Provider status verification only. Never resends the original generation request.
public sealed class AcceleratorDispatchReconciliationService(
    IAcceleratorPhase3Store store,
    AcceleratorQuotaService quota,
    IEnumerable<IAiTutorDispatchVerifier> verifiers,
    IAuditWriter audit,
    TimeProvider clock)
{
    private const int MaxAttempts = 6;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ReconciliationBatchResult> ProcessBatchAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var candidates = await store.EligibleReconciliationDispatchesAsync(Now, batchSize, ct);
        var examined = 0; var resolved = 0; var pending = 0; var manual = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var claimId = Guid.NewGuid();
            var record = await store.ClaimReconciliationAsync(candidate.Id, claimId, Now, Now.AddMinutes(3), ct);
            if (record is null) continue;
            examined++;
            try
            {
                var verifier = verifiers.FirstOrDefault(x => x.ProviderName == record.Provider);
                TutorDispatchVerification verification;
                if (verifier is null || string.IsNullOrWhiteSpace(record.ProviderRequestId))
                    verification = new(AcceleratorDispatchVerificationStatus.Unsupported, null, null, null, null, null, null, "provider_status_unavailable");
                else
                    verification = await verifier.VerifyAsync(record.ProviderRequestId, ct);

                if (verification.Status == AcceleratorDispatchVerificationStatus.VerifiedSuccess &&
                    verification.InputTokens is >= 0 && verification.OutputTokens is >= 0 && verification.EstimatedCostInr is >= 0 &&
                    (verification.ProviderRequestId is null || verification.ProviderRequestId == record.ProviderRequestId))
                {
                    // CompleteAsync is idempotent for identical usage. A crash between completion and
                    // dispatch transition is recoverable by the next reconciliation pass.
                    await quota.CompleteAsync(record.UserId, record.ReservationId, verification.InputTokens.Value,
                        verification.OutputTokens.Value, verification.EstimatedCostInr.Value, ct);
                    record.Status = AcceleratorAiDispatchStatus.Reconciled;
                    record.ProviderRequestId = verification.ProviderRequestId ?? record.ProviderRequestId;
                    record.Model = verification.Model ?? record.Model;
                    record.FinishedAtUtc = Now;
                    record.RequiresManualReview = true;
                    record.ManualReviewReason = "Verified usage finalized; assistant response recovery requires audited review.";
                    record.NextReconciliationAtUtc = null;
                    resolved++;
                }
                else
                {
                    // Existing quota ledger intentionally forbids release of dispatched reservations.
                    // Even verified failures remain under manual review until a safe ledger transition exists.
                    record.Status = AcceleratorAiDispatchStatus.Uncertain;
                    record.LastReconciliationError = verification.ErrorCode ?? verification.Status.ToString();
                    record.RequiresManualReview = verification.Status is AcceleratorDispatchVerificationStatus.Unsupported or AcceleratorDispatchVerificationStatus.VerifiedFailure
                        || record.ReconciliationAttemptCount >= MaxAttempts;
                    record.ManualReviewReason = record.RequiresManualReview ? "Provider outcome or chargeability requires authorized review." : null;
                    var delayMinutes = Math.Min(60, 1 << Math.Min(6, record.ReconciliationAttemptCount));
                    record.NextReconciliationAtUtc = record.RequiresManualReview ? null : Now.AddMinutes(delayMinutes);
                    if (record.RequiresManualReview) manual++; else pending++;
                }
                if (!await store.UpdateClaimedDispatchAsync(record, claimId, ct))
                    throw new InvalidOperationException("Reconciliation claim expired or was superseded.");
                await audit.AppendAsync(new AuditEvent(AuditAction.Update, "AcceleratorAiDispatchRecord", record.Id.ToString(),
                    new Dictionary<string, string?> { ["status"] = record.Status.ToString(), ["manualReview"] = record.RequiresManualReview.ToString() }), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                // A claim remains leased until expiration; another worker can safely recover it.
                // No provider request is regenerated and no reservation is released.
                throw;
            }
        }
        return new ReconciliationBatchResult(examined, resolved, pending, manual);
    }
}
