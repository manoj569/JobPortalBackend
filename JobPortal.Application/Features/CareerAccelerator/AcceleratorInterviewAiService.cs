using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerAccelerator;

// Uses the Phase 1 immutable quota ledger. No provider is activated by this registration.
public sealed class AcceleratorInterviewAiService(IAcceleratorStore writes, IAcceleratorPhase3Store dispatches,
    IAcceleratorPhase5Store interviews, IInterviewDispatchStore claims, AcceleratorPhase5Service phase5, AcceleratorQuotaService quota,
    AcceleratorService access, IOptions<AcceleratorOptions> options, IEnumerable<IAcceleratorAIProvider> providers,
    IEnumerable<IInterviewAiDispatchVerifier> verifiers, IAuditWriter audit, TimeProvider clock)
{
    private const int MaxReconciliationAttempts = 6;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<InterviewQuestionResponse> GenerateQuestionAsync(Guid actor, Guid sessionId, Guid sectionId,
        GenerateInterviewQuestionRequest request, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        if (request.RequestKey == Guid.Empty) throw new BadRequestException("Generation request key is required.");
        var session = await interviews.SessionAsync(actor, sessionId, ct)
            ?? throw new NotFoundException("Interview session not found.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{sessionId:N}|{sectionId:N}|{session.InterviewVersionId:N}|{request.RequestKey:N}")));
        var priorQuestion = session.Questions.SingleOrDefault(x => x.GenerationRequestKey == request.RequestKey);
        if (priorQuestion is not null)
        {
            if (priorQuestion.SectionId != sectionId || priorQuestion.GenerationRequestHash != hash)
                throw new ConflictException("Generation request key was reused with different input.");
            return QuestionDto(priorQuestion);
        }
        var previousDispatch = await dispatches.DispatchByKeyAsync(actor, request.RequestKey, ct);
        if (previousDispatch is not null)
        {
            if (previousDispatch.Operation != "InterviewQuestionGeneration" || previousDispatch.RequestHash != hash ||
                previousDispatch.InterviewSessionId != sessionId || previousDispatch.InterviewSectionId != sectionId)
                throw new ConflictException("Generation request key was reused with different input.");
            if (previousDispatch.ResultJson is not null) return await FinalizeGeneratedQuestionAsync(previousDispatch, ct);
            throw new ConflictException("Question generation outcome requires reconciliation.", "accelerator_dispatch_pending");
        }
        if (session.Status != AcceleratorInterviewSessionStatus.InProgress || session.Revision != request.ExpectedRevision ||
            session.Questions.Count >= 100) throw new ConflictException("Interview changed or cannot accept another question.");
        var version = await interviews.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        var section = version.Sections.SingleOrDefault(x => x.Id == sectionId)
            ?? throw new NotFoundException("Interview section not found.");
        var templates = section.Questions.Where(x => x.IsActive && x.Kind == AcceleratorInterviewQuestionKind.AiGenerated)
            .OrderBy(x => x.Sequence).ThenBy(x => x.Id).ToArray();
        if (templates.Length == 0) throw new ConflictException("Section does not permit AI question generation.");
        var assignedCount = session.Questions.Count(x => x.SectionId == sectionId && !x.ParentQuestionId.HasValue);
        if (assignedCount >= section.QuestionCount) throw new ConflictException("Section already has enough questions.");
        var slot = assignedCount + 1;
        var source = templates[(slot - 1) % templates.Length];
        var settings = options.Value;
        if (!settings.AIEnabled || !settings.OperationEnabled("InterviewQuestionGeneration") ||
            !settings.Policies.TryGetValue(AcceleratorFeature.Interview, out var policy) || !policy.Enabled)
        {
            if (section.AllowStaticFallback)
                return await phase5.AssignStaticFallbackAsync(actor, sessionId, sectionId, request.RequestKey, hash, ct);
            throw new AcceleratorProviderUnavailableException();
        }
        var provider = providers.FirstOrDefault(x => x.IsAvailable && x.Name == policy.Provider);
        if (provider is null)
        {
            if (section.AllowStaticFallback)
                return await phase5.AssignStaticFallbackAsync(actor, sessionId, sectionId, request.RequestKey, hash, ct);
            throw new AcceleratorProviderUnavailableException();
        }
        var competencyIds = version.Competencies.Select(x => x.CompetencyId).OrderBy(x => x).ToArray();
        var instructions = "Generate one role-specific mock interview question. Return only a JSON object with " +
            "QuestionText, QuestionType, Difficulty, CompetencyIds, FollowUpEligible and GenerationMetadata. " +
            "Do not include answers, scoring rules, identity fields or credentials. " +
            $"Role: {version.Definition.Name}; section: {section.Title}; scenario: {source.PublicPrompt}; " +
            $"question type: {(int)source.Type}; difficulty: {(int)version.Difficulty}; " +
            $"allowed competency IDs: {string.Join(", ", competencyIds)}.";
        var context = $"Interview section {section.Sequence}, question slot {slot}.";
        var bytes = Encoding.UTF8.GetByteCount(context);
        var tokens = Math.Max(1, (bytes + Encoding.UTF8.GetByteCount(instructions) + 3) / 4);
        var reservation = await quota.ReserveAsync(actor, new ReserveAcceleratorUsageRequest(request.RequestKey,
            AcceleratorFeature.Interview, hash, tokens, bytes), ct);
        var (dispatch, created) = await writes.WriteAsync(async () =>
        {
            var current = await dispatches.DispatchByKeyAsync(actor, request.RequestKey, ct);
            if (current is not null) return (current, false);
            var row = new AcceleratorAiDispatchRecord { UserId = actor, InterviewSessionId = sessionId,
                InterviewSectionId = sectionId, GenerationSlot = slot, RequestKey = request.RequestKey,
                ReservationId = reservation.Id, Operation = "InterviewQuestionGeneration", Provider = provider.Name,
                Model = policy.Model, Status = AcceleratorAiDispatchStatus.Reserved, RequestHash = hash,
                NextReconciliationAtUtc = Now.AddMinutes(2) };
            dispatches.Add(row); await Audit(row.Id, "Reserved", ct, row.Operation);
            return (row, true);
        }, ct);
        if (!created || dispatch.Status != AcceleratorAiDispatchStatus.Reserved || dispatch.RequestHash != hash ||
            dispatch.ReservationId != reservation.Id || dispatch.GenerationSlot != slot)
            throw new ConflictException("Question generation dispatch already exists or requires reconciliation.",
                "accelerator_dispatch_pending");
        try { await quota.MarkDispatchedAsync(actor, reservation.Id, ct); }
        catch
        {
            try { await quota.ReleaseUnusedAsync(actor, reservation.Id, CancellationToken.None);
                await SetStatus(dispatch.Id, AcceleratorAiDispatchStatus.Failed, CancellationToken.None); }
            catch (ConflictException) { await SetStatus(dispatch.Id, AcceleratorAiDispatchStatus.Uncertain, CancellationToken.None); }
            throw;
        }
        await SetStatus(dispatch.Id, AcceleratorAiDispatchStatus.Dispatched, ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(policy.TimeoutSeconds));
            var result = await provider.ExecuteAsync(new AcceleratorAIRequest(reservation.Id, dispatch.Id.ToString("N"),
                AcceleratorFeature.Interview, policy.Model, instructions, context,
                policy.MaxOutputTokens, policy.TimeoutSeconds), timeout.Token);
            if (string.IsNullOrWhiteSpace(result.ProviderRequestId))
                throw new ConflictException("Provider response lacks a verifiable request identifier.");
            await PersistResult(dispatch.Id, result, ct);
            _ = InterviewQuestionGenerationPolicy.Parse(result.StructuredJson, version, section, source);
            var saved = await dispatches.DispatchAsync(dispatch.Id, ct)
                ?? throw new ConflictException("Persisted question dispatch is missing.");
            return await FinalizeGeneratedQuestionAsync(saved, ct);
        }
        catch
        {
            await SetStatus(dispatch.Id, AcceleratorAiDispatchStatus.Uncertain, CancellationToken.None);
            throw;
        }
    }

    public async Task<InterviewEvaluationResponse> EvaluateAsync(Guid actor, Guid sessionId, Guid answerId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var session = await interviews.SessionAsync(actor, sessionId, ct)
            ?? throw new NotFoundException("Interview session not found.");
        var answer = (await interviews.AnswersAsync(actor, sessionId, ct)).SingleOrDefault(x => x.Id == answerId)
            ?? throw new NotFoundException("Interview answer not found.");
        var assigned = session.Questions.SingleOrDefault(x => x.Id == answer.SessionQuestionId)
            ?? throw new ConflictException("Assigned question is missing.");
        var version = await interviews.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        var source = version.Sections.SelectMany(x => x.Questions).SingleOrDefault(x => x.Id == assigned.SourceQuestionId)
            ?? throw new ConflictException("Historical interview question is missing.");
        if (source.EvaluationMode != AcceleratorInterviewEvaluationMode.Ai)
            throw new ConflictException("Question does not permit AI evaluation.");
        var key = Key(answerId);
        var prior = await interviews.EvaluationAsync(actor, answerId, ct);
        var previousDispatch = await dispatches.DispatchByKeyAsync(actor, key, ct);
        if (prior is not null)
        {
            if (prior.Evaluator == AcceleratorInterviewEvaluationMode.Ai && previousDispatch?.ResultJson is not null &&
                previousDispatch.Status != AcceleratorAiDispatchStatus.Reconciled)
                await FinalizeSuccessAsync(previousDispatch, ct);
            return await phase5.EvaluationAsync(actor, sessionId, answerId, ct)
                ?? throw new ConflictException("Interview evaluation is missing.");
        }
        if (previousDispatch is not null)
        {
            if (previousDispatch.ResultJson is not null) return await FinalizeSuccessAsync(previousDispatch, ct);
            throw new ConflictException("Interview AI outcome requires reconciliation.", "accelerator_dispatch_pending");
        }
        var settings = options.Value;
        if (!settings.AIEnabled || !settings.OperationEnabled("InterviewEvaluation") ||
            !settings.Policies.TryGetValue(AcceleratorFeature.Interview, out var policy) || !policy.Enabled)
            throw new AcceleratorProviderUnavailableException();
        var provider = providers.FirstOrDefault(x => x.IsAvailable && x.Name == policy.Provider)
            ?? throw new AcceleratorProviderUnavailableException();
        var instructions = "Evaluate this mock interview answer against the published rubric. " +
            "Return only a JSON array of CriterionId and Score. Do not execute candidate code. " +
            $"Question: {assigned.PublicPrompt}. Evaluator guidance: {source.HiddenEvaluatorGuidance}. Criteria: " +
            string.Join("; ", version.Criteria.OrderBy(x => x.Sequence).Select(x =>
                $"{x.Id}: {x.Name}, maximum {x.MaximumScore}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{session.Id:N}|{answer.Id:N}|{answer.RequestHash}|{version.Id:N}")));
        var bytes = Encoding.UTF8.GetByteCount(answer.Content);
        var tokens = Math.Max(1, (bytes + Encoding.UTF8.GetByteCount(instructions) + 3) / 4);
        var reservation = await quota.ReserveAsync(actor,
            new ReserveAcceleratorUsageRequest(key, AcceleratorFeature.Interview, hash, tokens, bytes), ct);
        var dispatch = await writes.WriteAsync(async () =>
        {
            var current = await dispatches.DispatchByKeyAsync(actor, key, ct);
            if (current is not null) return current;
            var row = new AcceleratorAiDispatchRecord { UserId = actor, InterviewSessionId = sessionId,
                InterviewAnswerId = answerId, RequestKey = key, ReservationId = reservation.Id,
                Operation = "InterviewEvaluation", Provider = provider.Name, Model = policy.Model,
                Status = AcceleratorAiDispatchStatus.Reserved, RequestHash = hash,
                NextReconciliationAtUtc = Now.AddMinutes(2) };
            dispatches.Add(row); await Audit(row.Id, "Reserved", ct);
            return row;
        }, ct);
        if (dispatch.Status != AcceleratorAiDispatchStatus.Reserved || dispatch.ReservationId != reservation.Id ||
            dispatch.RequestHash != hash) throw new ConflictException("Interview dispatch already exists.");
        try { await quota.MarkDispatchedAsync(actor, reservation.Id, ct); }
        catch
        {
            try
            {
                await quota.ReleaseUnusedAsync(actor, reservation.Id, CancellationToken.None);
                await SetStatus(dispatch.Id, AcceleratorAiDispatchStatus.Failed, CancellationToken.None);
            }
            catch (ConflictException) { await SetStatus(dispatch.Id, AcceleratorAiDispatchStatus.Uncertain, CancellationToken.None); }
            throw;
        }
        await SetStatus(dispatch.Id, AcceleratorAiDispatchStatus.Dispatched, ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(policy.TimeoutSeconds));
            var result = await provider.ExecuteAsync(new AcceleratorAIRequest(reservation.Id, dispatch.Id.ToString("N"),
                AcceleratorFeature.Interview, policy.Model, instructions, answer.Content,
                policy.MaxOutputTokens, policy.TimeoutSeconds), timeout.Token);
            if (string.IsNullOrWhiteSpace(result.ProviderRequestId))
                throw new ConflictException("Provider response lacks a verifiable request identifier.");
            await PersistResult(dispatch.Id, result, ct);
            _ = InterviewAiResultParser.Parse(result.StructuredJson, version.Criteria);
            var saved = await dispatches.DispatchAsync(dispatch.Id, ct)
                ?? throw new ConflictException("Persisted interview dispatch is missing.");
            return await FinalizeSuccessAsync(saved, ct);
        }
        catch
        {
            await SetStatus(dispatch.Id, AcceleratorAiDispatchStatus.Uncertain, CancellationToken.None);
            throw;
        }
    }

    public async Task<ReconciliationBatchResult> ProcessBatchAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var candidates = await claims.EligibleInterviewDispatchesAsync(Now, batchSize, ct);
        var examined = 0; var resolved = 0; var pending = 0; var manual = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var claimId = Guid.NewGuid();
            var row = await claims.ClaimInterviewDispatchAsync(candidate.Id, claimId, Now, Now.AddMinutes(3), ct);
            if (row is null) continue;
            examined++;
            if (row.ReconciliationAttemptCount > MaxReconciliationAttempts)
            {
                Escalate(row, "Automated verification attempts were exhausted."); manual++;
            }
            else if (row.Status == AcceleratorAiDispatchStatus.Reserved)
            {
                var reservation = await writes.ReservationAsync(row.UserId, row.ReservationId, ct)
                    ?? throw new ConflictException("Interview reservation is missing.");
                if (reservation.State == AcceleratorUsageState.Reserved && reservation.LeaseExpiresAtUtc <= Now)
                    await quota.ReleaseUnusedAsync(row.UserId, row.ReservationId, ct);
                if (reservation.State == AcceleratorUsageState.Reserved && reservation.LeaseExpiresAtUtc > Now)
                { row.NextReconciliationAtUtc = reservation.LeaseExpiresAtUtc; pending++; }
                else if (reservation.State is AcceleratorUsageState.Reserved or AcceleratorUsageState.Released)
                { row.Status = AcceleratorAiDispatchStatus.Failed; row.FinishedAtUtc = Now; row.NextReconciliationAtUtc = null; resolved++; }
                else { Escalate(row, "Dispatch commit outcome is unknown."); manual++; }
            }
            else if (row.ResultJson is not null && row.InputTokens.HasValue && row.OutputTokens.HasValue && row.ActualCostInr.HasValue)
            {
                try { await FinalizeAnyAsync(row, ct); row.Status = AcceleratorAiDispatchStatus.Reconciled;
                    row.FinishedAtUtc = Now; row.NextReconciliationAtUtc = null; resolved++; }
                catch (ConflictException) { Escalate(row, "Verified response could not be finalized safely."); manual++; }
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
                    catch (TimeoutException) { verification = Unknown("verification_timeout"); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { verification = Unknown("verification_cancelled"); }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    { verification = Unknown(exception.GetType().Name); }
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
                    if (!await claims.PersistClaimedInterviewResultAsync(row, claimId, ct))
                        throw new ConflictException("Interview reconciliation claim expired before verified output was saved.");
                    try { await FinalizeAnyAsync(row, ct); row.Status = AcceleratorAiDispatchStatus.Reconciled;
                        row.FinishedAtUtc = Now; row.NextReconciliationAtUtc = null; resolved++; }
                    catch (ConflictException) { Escalate(row, "Provider success could not be recorded safely."); manual++; }
                }
                else if (verification.Status == AcceleratorDispatchVerificationStatus.VerifiedFailure &&
                    verification.ConfirmedNonChargeable && !string.IsNullOrWhiteSpace(row.ProviderRequestId) &&
                    !string.IsNullOrWhiteSpace(verification.EvidenceReference))
                {
                    await quota.ResolveVerifiedNonChargeableAsync(row.UserId, row.ReservationId,
                        row.ProviderRequestId, verification.EvidenceReference, ct);
                    row.Status = AcceleratorAiDispatchStatus.Failed;
                    row.FinishedAtUtc = Now; row.NextReconciliationAtUtc = null; resolved++;
                }
                else
                {
                    row.Status = AcceleratorAiDispatchStatus.Uncertain;
                    row.LastReconciliationError = verification.Status.ToString();
                    if (verification.Status == AcceleratorDispatchVerificationStatus.Unsupported ||
                        row.ReconciliationAttemptCount >= MaxReconciliationAttempts)
                    { Escalate(row, "Provider outcome remains unresolved."); manual++; }
                    else { row.NextReconciliationAtUtc = Now.AddMinutes(Math.Min(60, 1 << Math.Min(6,
                        row.ReconciliationAttemptCount))); pending++; }
                }
            }
            if (!await claims.UpdateClaimedInterviewDispatchAsync(row, claimId, ct))
                throw new ConflictException("Interview reconciliation claim expired or was superseded.");
            await Audit(row.Id, row.Status.ToString(), ct, row.Operation);
        }
        return new ReconciliationBatchResult(examined, resolved, pending, manual);
    }

    public async Task<IReadOnlyList<DispatchAdminResponse>> UnresolvedAsync(Guid adminId, int page, int pageSize, CancellationToken ct)
    {
        await access.Administrator(adminId, ct); AcceleratorService.Page(page, pageSize);
        return (await claims.UnresolvedInterviewDispatchesAsync((page - 1) * pageSize, pageSize, ct))
            .Select(x => new DispatchAdminResponse(x.Id, x.UserId, x.ReservationId, x.Status,
                x.ReconciliationAttemptCount, x.RequiresManualReview, x.ManualReviewReason, x.NextReconciliationAtUtc)).ToArray();
    }

    public async Task<DispatchAdminResponse> ResolveAsync(Guid adminId, Guid dispatchId,
        ManualInterviewDispatchResolutionRequest request, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        if (dispatchId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000 ||
            string.IsNullOrWhiteSpace(request.ProviderRequestId) || request.ProviderRequestId.Length > 200 ||
            string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Length > 500)
            throw new BadRequestException("Resolution requires a reason, provider request ID and evidence reference.");
        var claimId = Guid.NewGuid();
        var row = await claims.ClaimManualInterviewDispatchAsync(dispatchId, claimId, Now, Now.AddMinutes(3), ct)
            ?? throw new ConflictException("Interview dispatch is terminal or currently claimed.");
        try
        {
            if (row.Status is not (AcceleratorAiDispatchStatus.Uncertain or AcceleratorAiDispatchStatus.Dispatched or
                AcceleratorAiDispatchStatus.Succeeded) || row.ProviderRequestId != request.ProviderRequestId)
                throw new ConflictException("Dispatch has no matching provider request to verify.");
            var verifier = verifiers.FirstOrDefault(x => x.ProviderName == row.Provider)
                ?? throw new ConflictException("Provider-side verification is unavailable; dispatch remains unresolved.");
            var verification = await verifier.VerifyAsync(row.ProviderRequestId, ct)
                .WaitAsync(TimeSpan.FromSeconds(30), ct);
            if (verification.ProviderRequestId != row.ProviderRequestId ||
                verification.EvidenceReference != request.EvidenceReference)
                throw new ConflictException("Provider evidence does not match this dispatch and reference.");
            if (verification.Status == AcceleratorDispatchVerificationStatus.VerifiedSuccess &&
                verification.StructuredJson is not null && verification.InputTokens is >= 0 &&
                verification.OutputTokens is >= 0 && verification.ActualCostInr is >= 0)
            {
                if (row.ResultJson is not null && row.ResultJson != verification.StructuredJson)
                    throw new ConflictException("Verified output conflicts with persisted output.");
                var hadResult = row.ResultJson is not null;
                row.ResultJson = verification.StructuredJson;
                row.InputTokens = verification.InputTokens;
                row.OutputTokens = verification.OutputTokens;
                row.ActualCostInr = verification.ActualCostInr;
                if (!hadResult && !await claims.PersistClaimedInterviewResultAsync(row, claimId, ct))
                    throw new ConflictException("Manual reconciliation claim expired before output persisted.");
                await FinalizeAnyAsync(row, ct);
                row.Status = AcceleratorAiDispatchStatus.Reconciled;
                row.FinishedAtUtc = Now; row.NextReconciliationAtUtc = null;
            }
            else if (verification.Status == AcceleratorDispatchVerificationStatus.VerifiedFailure &&
                verification.ConfirmedNonChargeable)
            {
                await quota.ResolveVerifiedNonChargeableAsync(row.UserId, row.ReservationId,
                    row.ProviderRequestId, request.EvidenceReference, ct);
                row.Status = AcceleratorAiDispatchStatus.Failed;
                row.FinishedAtUtc = Now; row.NextReconciliationAtUtc = null;
            }
            else throw new ConflictException("Provider outcome is not verified as successful or non-chargeable.");
            row.RequiresManualReview = false; row.ManualReviewReason = null;
            await audit.AppendAsync(new AuditEvent(AuditAction.Confirm, "AcceleratorInterviewManualResolution",
                row.Id.ToString(), new Dictionary<string, string?> { ["reason"] = request.Reason.Trim(),
                    ["evidenceReference"] = request.EvidenceReference, ["status"] = row.Status.ToString() },
                Actor: new AuditActor(adminId, "Administrator")), ct);
        }
        catch
        {
            if (row.Status is not (AcceleratorAiDispatchStatus.Reconciled or AcceleratorAiDispatchStatus.Failed))
            {
                row.Status = AcceleratorAiDispatchStatus.Uncertain;
                row.RequiresManualReview = true;
                row.ManualReviewReason = "Manual verification did not establish a safe outcome.";
                row.NextReconciliationAtUtc = null;
            }
            await audit.AppendAsync(new AuditEvent(AuditAction.Update, "AcceleratorInterviewManualResolution",
                row.Id.ToString(), new Dictionary<string, string?> { ["reason"] = request.Reason.Trim(),
                    ["evidenceReference"] = request.EvidenceReference, ["status"] = "Unresolved" },
                Actor: new AuditActor(adminId, "Administrator")), CancellationToken.None);
            _ = await claims.UpdateClaimedInterviewDispatchAsync(row, claimId, CancellationToken.None);
            throw;
        }
        if (!await claims.UpdateClaimedInterviewDispatchAsync(row, claimId, ct))
            throw new ConflictException("Manual reconciliation claim expired or was superseded.");
        return new DispatchAdminResponse(row.Id, row.UserId, row.ReservationId, row.Status,
            row.ReconciliationAttemptCount, row.RequiresManualReview, row.ManualReviewReason, row.NextReconciliationAtUtc);
    }

    private async Task<InterviewEvaluationResponse> FinalizeSuccessAsync(AcceleratorAiDispatchRecord row, CancellationToken ct)
    {
        if (!row.InterviewSessionId.HasValue || !row.InterviewAnswerId.HasValue || row.ResultJson is null ||
            !row.InputTokens.HasValue || !row.OutputTokens.HasValue || !row.ActualCostInr.HasValue)
            throw new ConflictException("Verified interview dispatch is incomplete.");
        var evaluation = await phase5.RecordVerifiedAiAsync(row.UserId, row.InterviewSessionId.Value,
            row.InterviewAnswerId.Value, row.ResultJson, ct);
        await quota.CompleteAsync(row.UserId, row.ReservationId, row.InputTokens.Value,
            row.OutputTokens.Value, row.ActualCostInr.Value, ct);
        await SetStatus(row.Id, AcceleratorAiDispatchStatus.Reconciled, ct);
        return evaluation;
    }

    private async Task<InterviewQuestionResponse> FinalizeGeneratedQuestionAsync(AcceleratorAiDispatchRecord row,
        CancellationToken ct)
    {
        if (row.Operation != "InterviewQuestionGeneration" || !row.InterviewSessionId.HasValue ||
            !row.InterviewSectionId.HasValue || !row.GenerationSlot.HasValue || row.ResultJson is null ||
            !row.InputTokens.HasValue || !row.OutputTokens.HasValue || !row.ActualCostInr.HasValue)
            throw new ConflictException("Verified question dispatch is incomplete.");
        var session = await interviews.SessionAsync(row.UserId, row.InterviewSessionId.Value, ct)
            ?? throw new ConflictException("Interview session is missing.");
        var version = await interviews.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        var section = version.Sections.SingleOrDefault(x => x.Id == row.InterviewSectionId.Value)
            ?? throw new ConflictException("Historical interview section is missing.");
        var templates = section.Questions.Where(x => x.IsActive && x.Kind == AcceleratorInterviewQuestionKind.AiGenerated)
            .OrderBy(x => x.Sequence).ThenBy(x => x.Id).ToArray();
        if (templates.Length == 0) throw new ConflictException("Historical AI question policy is missing.");
        var source = templates[(row.GenerationSlot.Value - 1) % templates.Length];
        var question = await phase5.RecordGeneratedQuestionAsync(row.UserId, session.Id, section.Id, source.Id,
            row.Id, row.RequestKey, row.RequestHash, row.Provider, row.Model, row.ResultJson, ct);
        await quota.CompleteAsync(row.UserId, row.ReservationId, row.InputTokens.Value,
            row.OutputTokens.Value, row.ActualCostInr.Value, ct);
        await SetStatus(row.Id, AcceleratorAiDispatchStatus.Reconciled, ct);
        return question;
    }

    private async Task FinalizeAnyAsync(AcceleratorAiDispatchRecord row, CancellationToken ct)
    {
        if (row.Operation == "InterviewQuestionGeneration")
            _ = await FinalizeGeneratedQuestionAsync(row, ct);
        else if (row.Operation == "InterviewEvaluation")
            _ = await FinalizeSuccessAsync(row, ct);
        else throw new ConflictException("Unknown interview dispatch operation.");
    }

    private Task<int> PersistResult(Guid dispatchId, AcceleratorAIResult result, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var row = await dispatches.DispatchAsync(dispatchId, ct) ?? throw new ConflictException("Dispatch is missing.");
        row.ResultJson = result.StructuredJson; row.ProviderRequestId = result.ProviderRequestId;
        row.Model = result.ModelVersion; row.InputTokens = result.InputTokens;
        row.OutputTokens = result.OutputTokens; row.ActualCostInr = result.ActualCostInr;
        row.Status = AcceleratorAiDispatchStatus.Uncertain; row.NextReconciliationAtUtc = Now.AddMinutes(2);
        row.Revision = Guid.NewGuid();
        return 0;
    }, ct);
    private Task<int> SetStatus(Guid dispatchId, AcceleratorAiDispatchStatus status, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var row = await dispatches.DispatchAsync(dispatchId, ct) ?? throw new ConflictException("Dispatch is missing.");
        if (row.Status is AcceleratorAiDispatchStatus.Reconciled or AcceleratorAiDispatchStatus.Failed) return 0;
        row.Status = status;
        row.NextReconciliationAtUtc = status is AcceleratorAiDispatchStatus.Reconciled or AcceleratorAiDispatchStatus.Failed ? null : Now.AddMinutes(2);
        row.FinishedAtUtc = status is AcceleratorAiDispatchStatus.Reconciled or AcceleratorAiDispatchStatus.Failed ? Now : null;
        row.Revision = Guid.NewGuid(); await Audit(row.Id, status.ToString(), ct, row.Operation);
        return 0;
    }, ct);
    private static void Escalate(AcceleratorAiDispatchRecord row, string reason)
    {
        row.Status = AcceleratorAiDispatchStatus.Uncertain; row.RequiresManualReview = true;
        row.ManualReviewReason = reason; row.NextReconciliationAtUtc = null;
    }
    private static ProjectAiVerification Unknown(string reason) =>
        new(AcceleratorDispatchVerificationStatus.Unknown, null, null, null, null, null, false, reason);
    private Task Audit(Guid id, string status, CancellationToken ct, string operation = "InterviewEvaluation") => audit.AppendAsync(
        new AuditEvent(AuditAction.Update, "AcceleratorAiDispatchRecord", id.ToString(),
            new Dictionary<string, string?> { ["operation"] = operation, ["status"] = status }), ct);
    private static InterviewQuestionResponse QuestionDto(AcceleratorSessionQuestion row) =>
        new(row.Id, row.SectionId, row.ParentQuestionId, row.Sequence, row.Type, row.PublicPrompt, false, false);
    private static Guid Key(Guid answerId)
    {
        Span<byte> bytes = stackalloc byte[16]; answerId.TryWriteBytes(bytes);
        return new Guid(SHA256.HashData(bytes)[..16]);
    }
}
