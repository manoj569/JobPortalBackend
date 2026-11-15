using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerAccelerator;

// No provider is activated by this service. The registered default remains explicitly unavailable.
public sealed class AcceleratorProjectAiService(IAcceleratorStore writes, IAcceleratorPhase4Store projects,
    IAcceleratorPhase3Store dispatches, AcceleratorPhase4Service phase4, AcceleratorQuotaService quota,
    AcceleratorService access, IOptions<AcceleratorOptions> options, IEnumerable<IAcceleratorAIProvider> providers,
    IEnumerable<IProjectAiDispatchVerifier> verifiers, IAuditWriter audit, TimeProvider clock)
{
    private const int MaxReconciliationAttempts = 6;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AcceleratorProjectEvaluationResponse> EvaluateAsync(Guid actor, Guid projectId, Guid submissionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var project = await projects.CandidateProjectAsync(actor, projectId, ct)
            ?? throw new NotFoundException("Candidate project not found.");
        var submission = await projects.SubmissionAsync(actor, projectId, submissionId, ct)
            ?? throw new NotFoundException("Project submission not found.");
        var assignment = await projects.AssignmentAsync(project.ProjectVersionId, submission.AssignmentId, ct)
            ?? throw new ConflictException("Published assignment is missing.");
        if (assignment.EvaluationMode != AcceleratorProjectEvaluationMode.Ai)
            throw new ConflictException("Assignment does not use AI evaluation.");
        var priorEvaluation = await projects.EvaluationAsync(actor, submissionId, ct);
        var requestKey = Key(submissionId);
        var priorDispatch = await dispatches.DispatchByKeyAsync(actor, requestKey, ct);
        if (priorEvaluation is not null)
        {
            if (priorEvaluation.Evaluator == AcceleratorProjectEvaluator.ProviderAssisted &&
                priorDispatch?.ResultJson is not null && priorDispatch.Status != AcceleratorAiDispatchStatus.Reconciled)
                await FinalizeLocalSuccessAsync(priorDispatch, ct);
            return await phase4.EvaluationAsync(actor, projectId, submissionId, ct)
                ?? throw new ConflictException("Project evaluation is missing.");
        }
        if (priorDispatch is not null)
        {
            if (priorDispatch.ResultJson is not null)
                return await FinalizeLocalSuccessAsync(priorDispatch, ct);
            throw new ConflictException("Project AI evaluation outcome requires reconciliation.", "accelerator_dispatch_pending");
        }

        var settings = options.Value;
        if (!settings.AIEnabled || !settings.OperationEnabled("ProjectEvaluation") ||
            !settings.Policies.TryGetValue(AcceleratorFeature.Review, out var policy) || !policy.Enabled)
            throw new AcceleratorProviderUnavailableException();
        var provider = providers.FirstOrDefault(x => x.IsAvailable && x.Name == policy.Provider)
            ?? throw new AcceleratorProviderUnavailableException();
        var rubric = await projects.ActiveRubricAsync(project.ProjectVersionId, ct)
            ?? throw new ConflictException("Published rubric is missing.");
        AcceleratorPhase4Service.ValidateRubric(rubric);
        var instructions = "Evaluate only the submitted project assignment against these published criteria. " +
            "Return a JSON array of CriterionId and Score. Do not execute candidate code. Criteria: " +
            string.Join("; ", rubric.Criteria.OrderBy(x => x.Sequence).Select(x => $"{x.Id}: {x.Name}, maximum {x.MaximumScore}"));
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{project.Id:N}|{submission.Id:N}|{submission.ContentHash}|{rubric.Id:N}")));
        var bytes = Encoding.UTF8.GetByteCount(submission.Content);
        var tokens = Math.Max(1, (bytes + Encoding.UTF8.GetByteCount(instructions) + 3) / 4);
        var reservation = await quota.ReserveAsync(actor,
            new ReserveAcceleratorUsageRequest(requestKey, AcceleratorFeature.Review, requestHash, tokens, bytes), ct);
        var dispatch = await writes.WriteAsync(async () =>
        {
            var current = await dispatches.DispatchByKeyAsync(actor, requestKey, ct);
            if (current is not null) return current;
            var row = new AcceleratorAiDispatchRecord { UserId = actor, CandidateProjectId = projectId,
                ProjectSubmissionId = submissionId, RequestKey = requestKey, ReservationId = reservation.Id,
                Operation = "ProjectEvaluation", Provider = provider.Name, Model = policy.Model,
                Status = AcceleratorAiDispatchStatus.Reserved, RequestHash = requestHash,
                NextReconciliationAtUtc = Now.AddMinutes(2) };
            dispatches.Add(row);
            await AuditAsync(row.Id, "Reserved", ct);
            return row;
        }, ct);
        if (dispatch.Status != AcceleratorAiDispatchStatus.Reserved || dispatch.ReservationId != reservation.Id ||
            dispatch.RequestHash != requestHash)
            throw new ConflictException("Project evaluation dispatch already exists.");
        try { await quota.MarkDispatchedAsync(actor, reservation.Id, ct); }
        catch
        {
            var released = false;
            try { await quota.ReleaseUnusedAsync(actor, reservation.Id, CancellationToken.None); released = true; }
            catch (ConflictException)
            {
                // MarkDispatched may have committed before the caller lost its response.
                // Preserve the reservation until its actual state is reconciled.
                await MarkUncertainAsync(dispatch.Id, CancellationToken.None);
            }
            if (released)
            {
                await writes.WriteAsync(async () =>
                {
                    var row = await dispatches.DispatchAsync(dispatch.Id, CancellationToken.None);
                    if (row is not null)
                    {
                        row.Status = AcceleratorAiDispatchStatus.Failed;
                        row.FinishedAtUtc = Now;
                        row.Revision = Guid.NewGuid();
                        await AuditAsync(row.Id, "Failed", CancellationToken.None);
                    }
                    return 0;
                }, CancellationToken.None);
                await phase4.ResolveVerifiedProviderFailureAsync(actor, projectId, submissionId, CancellationToken.None);
            }
            throw;
        }
        await writes.WriteAsync(async () =>
        {
            var row = await dispatches.DispatchAsync(dispatch.Id, ct) ?? throw new ConflictException("Dispatch record is missing.");
            row.Status = AcceleratorAiDispatchStatus.Dispatched;
            row.NextReconciliationAtUtc = Now.AddMinutes(2);
            row.Revision = Guid.NewGuid();
            await AuditAsync(row.Id, "Dispatched", ct);
            var owned = await projects.CandidateProjectAsync(actor, projectId, ct)
                ?? throw new NotFoundException("Candidate project not found.");
            if (owned.Status == AcceleratorCandidateProjectStatus.Submitted)
            {
                owned.Status = AcceleratorCandidateProjectStatus.UnderEvaluation;
                owned.Revision = Guid.NewGuid();
            }
            return 0;
        }, ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(policy.TimeoutSeconds));
            var result = await provider.ExecuteAsync(new AcceleratorAIRequest(reservation.Id, dispatch.Id.ToString("N"),
                AcceleratorFeature.Review, policy.Model, instructions, submission.Content,
                policy.MaxOutputTokens, policy.TimeoutSeconds), timeout.Token);
            if (string.IsNullOrWhiteSpace(result.ProviderRequestId))
                throw new ConflictException("Provider response lacks a verifiable request identifier.");
            await PersistResultAsync(dispatch.Id, result, ct);
            var saved = await dispatches.DispatchAsync(dispatch.Id, ct)
                ?? throw new ConflictException("Persisted provider result is missing.");
            return await FinalizeLocalSuccessAsync(saved, ct);
        }
        catch
        {
            await MarkUncertainAsync(dispatch.Id, CancellationToken.None);
            throw;
        }
    }

    public async Task<ReconciliationBatchResult> ProcessBatchAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var candidates = await projects.EligibleProjectDispatchesAsync(Now, batchSize, ct);
        var examined = 0; var resolved = 0; var pending = 0; var manual = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var claimId = Guid.NewGuid();
            var row = await projects.ClaimProjectDispatchAsync(candidate.Id, claimId, Now, Now.AddMinutes(3), ct);
            if (row is null) continue;
            examined++;
            if (row.ReconciliationAttemptCount > MaxReconciliationAttempts)
            {
                row.RequiresManualReview = true;
                row.ManualReviewReason = "Automated verification attempts were exhausted.";
                row.NextReconciliationAtUtc = null;
                if (!await projects.UpdateClaimedProjectDispatchAsync(row, claimId, ct))
                    throw new ConflictException("Project reconciliation claim expired or was superseded.");
                await AuditAsync(row.Id, "ManualReview", ct);
                manual++;
                continue;
            }
            if (row.Status == AcceleratorAiDispatchStatus.Reserved)
            {
                var reservation = await writes.ReservationAsync(row.UserId, row.ReservationId, ct)
                    ?? throw new ConflictException("Project dispatch reservation is missing.");
                if (reservation.State == AcceleratorUsageState.Reserved && reservation.LeaseExpiresAtUtc <= Now)
                    await quota.ReleaseUnusedAsync(row.UserId, row.ReservationId, ct);
                if (reservation.State == AcceleratorUsageState.Reserved && reservation.LeaseExpiresAtUtc > Now)
                {
                    row.NextReconciliationAtUtc = reservation.LeaseExpiresAtUtc;
                    pending++;
                }
                else if (reservation.State is AcceleratorUsageState.Reserved or AcceleratorUsageState.Released)
                {
                    if (row.CandidateProjectId.HasValue && row.ProjectSubmissionId.HasValue)
                        await phase4.ResolveVerifiedProviderFailureAsync(row.UserId,
                            row.CandidateProjectId.Value, row.ProjectSubmissionId.Value, ct);
                    row.Status = AcceleratorAiDispatchStatus.Failed;
                    row.FinishedAtUtc = Now;
                    row.NextReconciliationAtUtc = null;
                    resolved++;
                }
                else
                {
                    row.Status = AcceleratorAiDispatchStatus.Uncertain;
                    row.RequiresManualReview = true;
                    row.ManualReviewReason = "Dispatch commit outcome is unknown; provider request must be verified.";
                    row.NextReconciliationAtUtc = null;
                    manual++;
                }
            }
            else if (row.ResultJson is not null && row.InputTokens.HasValue && row.OutputTokens.HasValue && row.ActualCostInr.HasValue)
            {
                try
                {
                    await FinalizeLocalSuccessAsync(row, ct);
                    row.Status = AcceleratorAiDispatchStatus.Reconciled;
                    row.FinishedAtUtc = Now;
                    row.NextReconciliationAtUtc = null;
                    resolved++;
                }
                catch (ConflictException)
                {
                    row.Status = AcceleratorAiDispatchStatus.Uncertain;
                    row.RequiresManualReview = true;
                    row.ManualReviewReason = "Verified result or accounting could not be finalized safely.";
                    row.NextReconciliationAtUtc = null;
                    manual++;
                }
            }
            else
            {
                var verifier = verifiers.FirstOrDefault(x => x.ProviderName == row.Provider);
                ProjectAiVerification verification;
                if (verifier is null || string.IsNullOrWhiteSpace(row.ProviderRequestId))
                    verification = new(AcceleratorDispatchVerificationStatus.Unsupported, null, null, null, null, null, false, null);
                else
                {
                    try { verification = await verifier.VerifyAsync(row.ProviderRequestId, ct).WaitAsync(TimeSpan.FromSeconds(30), ct); }
                    catch (TimeoutException)
                    { verification = new(AcceleratorDispatchVerificationStatus.Unknown, null, null, null, null, null, false, "verification_timeout"); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    { verification = new(AcceleratorDispatchVerificationStatus.Unknown, null, null, null, null, null, false, "verification_cancelled"); }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    { verification = new(AcceleratorDispatchVerificationStatus.Unknown, null, null, null, null, null, false, exception.GetType().Name); }
                }
                if (verification.ProviderRequestId is not null && verification.ProviderRequestId != row.ProviderRequestId)
                    throw new ConflictException("Provider verification references a different request.");
                if (verification.Status == AcceleratorDispatchVerificationStatus.VerifiedSuccess &&
                    verification.StructuredJson is not null && verification.InputTokens is >= 0 &&
                    verification.OutputTokens is >= 0 && verification.ActualCostInr is >= 0)
                {
                    row.ResultJson = verification.StructuredJson;
                    row.InputTokens = verification.InputTokens;
                    row.OutputTokens = verification.OutputTokens;
                    row.ActualCostInr = verification.ActualCostInr;
                    try
                    {
                        await FinalizeLocalSuccessAsync(row, ct);
                        row.Status = AcceleratorAiDispatchStatus.Reconciled;
                        row.FinishedAtUtc = Now;
                        row.NextReconciliationAtUtc = null;
                        resolved++;
                    }
                    catch (ConflictException)
                    {
                        row.Status = AcceleratorAiDispatchStatus.Uncertain;
                        row.RequiresManualReview = true;
                        row.ManualReviewReason = "Provider success lacked a safely valid project evaluation or usage record.";
                        row.NextReconciliationAtUtc = null;
                        manual++;
                    }
                }
                else if (verification.Status == AcceleratorDispatchVerificationStatus.VerifiedFailure &&
                    verification.ConfirmedNonChargeable && !string.IsNullOrWhiteSpace(row.ProviderRequestId) &&
                    !string.IsNullOrWhiteSpace(verification.EvidenceReference))
                {
                    await quota.ResolveVerifiedNonChargeableAsync(row.UserId, row.ReservationId,
                        row.ProviderRequestId, verification.EvidenceReference, ct);
                    if (row.CandidateProjectId.HasValue && row.ProjectSubmissionId.HasValue)
                        await phase4.ResolveVerifiedProviderFailureAsync(row.UserId,
                            row.CandidateProjectId.Value, row.ProjectSubmissionId.Value, ct);
                    row.Status = AcceleratorAiDispatchStatus.Failed;
                    row.FinishedAtUtc = Now;
                    row.NextReconciliationAtUtc = null;
                    resolved++;
                }
                else
                {
                    row.Status = AcceleratorAiDispatchStatus.Uncertain;
                    row.LastReconciliationError = verification.Status.ToString();
                    row.RequiresManualReview = verification.Status == AcceleratorDispatchVerificationStatus.Unsupported ||
                        row.ReconciliationAttemptCount >= MaxReconciliationAttempts;
                    row.ManualReviewReason = row.RequiresManualReview ? "Provider outcome remains unresolved." : null;
                    row.NextReconciliationAtUtc = row.RequiresManualReview ? null : Now.AddMinutes(Math.Min(60, 1 << Math.Min(6, row.ReconciliationAttemptCount)));
                    if (row.RequiresManualReview) manual++; else pending++;
                }
            }
            if (!await projects.UpdateClaimedProjectDispatchAsync(row, claimId, ct))
                throw new ConflictException("Project reconciliation claim expired or was superseded.");
            await AuditAsync(row.Id, row.Status.ToString(), ct);
        }
        return new ReconciliationBatchResult(examined, resolved, pending, manual);
    }

    public async Task<IReadOnlyList<DispatchAdminResponse>> UnresolvedAsync(Guid adminId, int page, int pageSize, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        AcceleratorService.Page(page, pageSize);
        return (await projects.UnresolvedProjectDispatchesAsync((page - 1) * pageSize, pageSize, ct))
            .Select(x => new DispatchAdminResponse(x.Id, x.UserId, x.ReservationId, x.Status,
                x.ReconciliationAttemptCount, x.RequiresManualReview, x.ManualReviewReason, x.NextReconciliationAtUtc)).ToArray();
    }

    private async Task<AcceleratorProjectEvaluationResponse> FinalizeLocalSuccessAsync(AcceleratorAiDispatchRecord dispatch, CancellationToken ct)
    {
        if (dispatch.CandidateProjectId is null || dispatch.ProjectSubmissionId is null || dispatch.ResultJson is null ||
            dispatch.InputTokens is null || dispatch.OutputTokens is null || dispatch.ActualCostInr is null)
            throw new ConflictException("Verified project dispatch response is incomplete.");
        var evaluation = await phase4.RecordVerifiedAiAsync(dispatch.UserId, dispatch.CandidateProjectId.Value,
            dispatch.ProjectSubmissionId.Value, dispatch.ResultJson, ct);
        await quota.CompleteAsync(dispatch.UserId, dispatch.ReservationId,
            dispatch.InputTokens.Value, dispatch.OutputTokens.Value, dispatch.ActualCostInr.Value, ct);
        await writes.WriteAsync(async () =>
        {
            var row = await dispatches.DispatchAsync(dispatch.Id, ct) ?? throw new ConflictException("Dispatch record is missing.");
            row.Status = AcceleratorAiDispatchStatus.Reconciled;
            row.FinishedAtUtc = Now;
            row.NextReconciliationAtUtc = null;
            row.Revision = Guid.NewGuid();
            return 0;
        }, ct);
        await AuditAsync(dispatch.Id, "Reconciled", ct);
        return evaluation;
    }

    private Task<int> PersistResultAsync(Guid dispatchId, AcceleratorAIResult result, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var row = await dispatches.DispatchAsync(dispatchId, ct) ?? throw new ConflictException("Dispatch record is missing.");
        row.ResultJson = result.StructuredJson;
        row.ProviderRequestId = result.ProviderRequestId;
        row.Model = result.ModelVersion;
        row.InputTokens = result.InputTokens;
        row.OutputTokens = result.OutputTokens;
        row.ActualCostInr = result.ActualCostInr;
        row.Status = AcceleratorAiDispatchStatus.Uncertain;
        row.NextReconciliationAtUtc = Now.AddMinutes(2);
        row.Revision = Guid.NewGuid();
        return 0;
    }, ct);

    private Task<int> MarkUncertainAsync(Guid dispatchId, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var row = await dispatches.DispatchAsync(dispatchId, ct);
        if (row is not null && row.Status is not (AcceleratorAiDispatchStatus.Reconciled or AcceleratorAiDispatchStatus.Failed))
        {
            row.Status = AcceleratorAiDispatchStatus.Uncertain;
            row.NextReconciliationAtUtc = Now.AddMinutes(2);
            row.Revision = Guid.NewGuid();
            await AuditAsync(row.Id, "Uncertain", ct);
        }
        return 0;
    }, ct);

    private Task AuditAsync(Guid id, string status, CancellationToken ct) => audit.AppendAsync(
        new AuditEvent(AuditAction.Update, "AcceleratorAiDispatchRecord", id.ToString(),
            new Dictionary<string, string?> { ["operation"] = "ProjectEvaluation", ["status"] = status }), ct);

    private static Guid Key(Guid submissionId)
    {
        Span<byte> bytes = stackalloc byte[16];
        submissionId.TryWriteBytes(bytes);
        return new Guid(SHA256.HashData(bytes)[..16]);
    }
}
