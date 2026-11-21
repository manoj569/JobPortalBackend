using System.Text.Json;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorPhase5ReconciliationTests
{
    [Fact]
    public async Task VerifiedSuccessCreatesOneEvaluationAndOneCompletedReservation()
    {
        using var fixture = new AcceleratorFixture();
        var (ai, dispatch, claimStore, verifier) = await Arrange(fixture);
        verifier.Result = new ProjectAiVerification(AcceleratorDispatchVerificationStatus.VerifiedSuccess,
            dispatch.ProviderRequestId, JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(verifier.CriterionId, 8) }),
            80, 20, .1m, false, "provider-result-1");
        var result = await ai.ProcessBatchAsync(5, default);
        Assert.Equal(1, result.Resolved);
        Assert.Equal(1, claimStore.ClaimCount);
        Assert.Equal(AcceleratorAiDispatchStatus.Reconciled, dispatch.Status);
        Assert.Equal(AcceleratorUsageState.Completed, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        Assert.Equal(80, Assert.Single(fixture.Db.AcceleratorInterviewEvaluations).Score);
        await ai.ProcessBatchAsync(5, default);
        Assert.Equal(1, claimStore.ClaimCount);
        Assert.Single(fixture.Db.AcceleratorInterviewEvaluations);
        Assert.Equal(1, fixture.Db.AcceleratorUsageEvents.Count(x => x.State == AcceleratorUsageState.Completed));
    }

    [Fact]
    public async Task VerifiedNonChargeableFailureReleasesOnlyWithEvidence()
    {
        using var fixture = new AcceleratorFixture();
        var (ai, dispatch, _, verifier) = await Arrange(fixture);
        verifier.Result = new ProjectAiVerification(AcceleratorDispatchVerificationStatus.VerifiedFailure,
            dispatch.ProviderRequestId, null, null, null, null, false, null);
        await ai.ProcessBatchAsync(5, default);
        Assert.Equal(AcceleratorAiDispatchStatus.Uncertain, dispatch.Status);
        Assert.Equal(AcceleratorUsageState.Dispatched, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        dispatch.NextReconciliationAtUtc = fixture.Clock.Utc.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        verifier.Result = verifier.Result with { ConfirmedNonChargeable = true, EvidenceReference = "provider-noncharge-1" };
        await ai.ProcessBatchAsync(5, default);
        Assert.Equal(AcceleratorAiDispatchStatus.Failed, dispatch.Status);
        Assert.Equal(AcceleratorUsageState.Released, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        Assert.Empty(fixture.Db.AcceleratorInterviewEvaluations);
    }

    [Fact]
    public async Task UnknownAndUnsupportedOutcomesPreserveReservationAndEscalateSafely()
    {
        using var fixture = new AcceleratorFixture();
        var (ai, dispatch, _, verifier) = await Arrange(fixture);
        verifier.Result = new ProjectAiVerification(AcceleratorDispatchVerificationStatus.Unknown,
            dispatch.ProviderRequestId, null, null, null, null, false, "still_processing");
        var first = await ai.ProcessBatchAsync(5, default);
        Assert.Equal(1, first.Pending);
        Assert.Equal(AcceleratorAiDispatchStatus.Uncertain, dispatch.Status);
        Assert.False(dispatch.RequiresManualReview);
        Assert.True(dispatch.NextReconciliationAtUtc > fixture.Clock.Utc);
        Assert.Equal(AcceleratorUsageState.Dispatched, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        dispatch.NextReconciliationAtUtc = fixture.Clock.Utc.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        verifier.Result = verifier.Result with { Status = AcceleratorDispatchVerificationStatus.Unsupported };
        var second = await ai.ProcessBatchAsync(5, default);
        Assert.Equal(1, second.ManualReview);
        Assert.True(dispatch.RequiresManualReview);
        Assert.Null(dispatch.NextReconciliationAtUtc);
        Assert.Equal(AcceleratorUsageState.Dispatched, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
    }

    [Fact]
    public async Task ManualResolutionRequiresAdminAndMatchingProviderEvidence()
    {
        using var fixture = new AcceleratorFixture();
        var (ai, dispatch, _, verifier) = await Arrange(fixture);
        var request = new ManualInterviewDispatchResolutionRequest("Provider invoice checked",
            dispatch.ProviderRequestId!, "provider-evidence-1");
        await Assert.ThrowsAsync<AppException>(() => ai.ResolveAsync(fixture.Candidate.Id,
            dispatch.Id, request, default));
        verifier.Result = new ProjectAiVerification(AcceleratorDispatchVerificationStatus.Unknown,
            dispatch.ProviderRequestId, null, null, null, null, false, request.EvidenceReference);
        await Assert.ThrowsAsync<ConflictException>(() => ai.ResolveAsync(fixture.Admin.Id,
            dispatch.Id, request, default));
        Assert.Equal(AcceleratorUsageState.Dispatched, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        verifier.Result = new ProjectAiVerification(AcceleratorDispatchVerificationStatus.VerifiedFailure,
            dispatch.ProviderRequestId, null, null, null, null, true, request.EvidenceReference);
        var resolved = await ai.ResolveAsync(fixture.Admin.Id, dispatch.Id, request, default);
        Assert.Equal(AcceleratorAiDispatchStatus.Failed, resolved.Status);
        Assert.Equal(AcceleratorUsageState.Released, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
    }

    private static async Task<(AcceleratorInterviewAiService Ai, AcceleratorAiDispatchRecord Dispatch,
        InterviewClaimTestStore Claims, InterviewVerificationTestDouble Verifier)> Arrange(AcceleratorFixture fixture)
    {
        await fixture.Activate();
        var definition = new AcceleratorInterviewDefinition { StableKey = "reconcile-test", Name = "Interview" };
        var version = new AcceleratorInterviewVersion { Definition = definition, DefinitionId = definition.Id,
            Version = 1, Status = AcceleratorInterviewCatalogStatus.Draft };
        var section = new AcceleratorInterviewSection { Version = version, InterviewVersionId = version.Id,
            Sequence = 1, Title = "Questions", QuestionCount = 1 };
        var criterion = new AcceleratorInterviewRubricCriterion { InterviewVersionId = version.Id,
            Sequence = 1, Name = "Reasoning", Weight = 100, MaximumScore = 10 };
        var question = new AcceleratorInterviewQuestion { Section = section, SectionId = section.Id,
            Sequence = 1, PublicPrompt = "Explain reliability", EvaluationMode = AcceleratorInterviewEvaluationMode.Ai };
        version.Sections.Add(section); version.Criteria.Add(criterion); section.Questions.Add(question);
        fixture.Db.Add(version); await fixture.Db.SaveChangesAsync();
        version.Status = AcceleratorInterviewCatalogStatus.Published; await fixture.Db.SaveChangesAsync();
        var session = new AcceleratorInterviewSession { UserId = fixture.Candidate.Id, GoalId = Guid.NewGuid(),
            InterviewVersionId = version.Id, RequestKey = Guid.NewGuid(), Status = AcceleratorInterviewSessionStatus.InProgress };
        var assigned = new AcceleratorSessionQuestion { SessionId = session.Id, SourceQuestionId = question.Id,
            SectionId = section.Id, Sequence = 1, PublicPrompt = question.PublicPrompt };
        session.Questions.Add(assigned);
        var answer = new AcceleratorInterviewAnswer { SessionId = session.Id, SessionQuestionId = assigned.Id,
            RequestKey = Guid.NewGuid(), Content = "Use durable idempotency keys." };
        fixture.Db.AddRange(session, answer); await fixture.Db.SaveChangesAsync();
        var reservation = await fixture.Quota.ReserveAsync(fixture.Candidate.Id,
            new ReserveAcceleratorUsageRequest(Guid.NewGuid(), AcceleratorFeature.Interview,
                new string('A', 64), 50, 50), default);
        await fixture.Quota.MarkDispatchedAsync(fixture.Candidate.Id, reservation.Id, default);
        var dispatch = new AcceleratorAiDispatchRecord { UserId = fixture.Candidate.Id,
            InterviewSessionId = session.Id, InterviewAnswerId = answer.Id, RequestKey = Guid.NewGuid(),
            ReservationId = reservation.Id, Operation = "InterviewEvaluation", Provider = "VerifierFake",
            Model = "fake-v1", Status = AcceleratorAiDispatchStatus.Uncertain, RequestHash = new string('A', 64),
            ProviderRequestId = "provider-request-1", NextReconciliationAtUtc = fixture.Clock.Utc.AddMinutes(-1) };
        fixture.Db.Add(dispatch); await fixture.Db.SaveChangesAsync();
        var phase5Store = new AcceleratorPhase5Store(fixture.Db);
        var claims = new InterviewClaimTestStore(fixture.Db, dispatch);
        var verifier = new InterviewVerificationTestDouble(criterion.Id);
        var phase3Store = new AcceleratorPhase3Store(fixture.Db);
        var phase5 = new AcceleratorPhase5Service(fixture.Store, fixture.Phase2Store, phase5Store,
            phase3Store, new AcceleratorPhase4Store(fixture.Db), fixture.Service, fixture.Audit, fixture.Clock, fixture.Work, new AcceleratorJourneyStore(fixture.Db));
        var ai = new AcceleratorInterviewAiService(fixture.Store, phase3Store, phase5Store,
            claims, phase5, fixture.Quota, fixture.Service, Options.Create(fixture.Settings),
            [fixture.Provider], [verifier], fixture.Audit, fixture.Clock);
        return (ai, dispatch, claims, verifier);
    }
}

internal sealed class InterviewVerificationTestDouble(Guid criterionId, string providerName = "VerifierFake") : IInterviewAiDispatchVerifier
{
    public Guid CriterionId => criterionId;
    public string ProviderName => providerName;
    public ProjectAiVerification Result { get; set; } = new(AcceleratorDispatchVerificationStatus.Unknown,
        null, null, null, null, null, false, null);
    public Task<ProjectAiVerification> VerifyAsync(string providerRequestId, CancellationToken ct) => Task.FromResult(Result);
}

internal sealed class InterviewClaimTestStore(JobPortalDbContext context, AcceleratorAiDispatchRecord dispatch) : IInterviewDispatchStore
{
    public int ClaimCount { get; private set; }
    public Task<IReadOnlyList<AcceleratorAiDispatchRecord>> EligibleInterviewDispatchesAsync(DateTime now, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AcceleratorAiDispatchRecord>>(dispatch.Status is
            AcceleratorAiDispatchStatus.Uncertain or AcceleratorAiDispatchStatus.Succeeded &&
            !dispatch.RequiresManualReview && dispatch.NextReconciliationAtUtc <= now &&
            (dispatch.ReconciliationClaimExpiresAtUtc is null || dispatch.ReconciliationClaimExpiresAtUtc <= now)
            ? [dispatch] : []);
    public async Task<AcceleratorAiDispatchRecord?> ClaimInterviewDispatchAsync(Guid dispatchId, Guid claimId,
        DateTime now, DateTime expiresAt, CancellationToken ct)
    {
        if (dispatch.Id != dispatchId || dispatch.ReconciliationClaimExpiresAtUtc > now) return null;
        ClaimCount++; dispatch.ReconciliationClaimId = claimId; dispatch.ReconciliationClaimExpiresAtUtc = expiresAt;
        dispatch.ReconciliationAttemptCount++; dispatch.LastReconciliationAtUtc = now;
        await context.SaveChangesAsync(ct); return dispatch;
    }
    public Task<AcceleratorAiDispatchRecord?> ClaimManualInterviewDispatchAsync(Guid dispatchId, Guid claimId,
        DateTime now, DateTime expiresAt, CancellationToken ct) =>
        ClaimInterviewDispatchAsync(dispatchId, claimId, now, expiresAt, ct);
    public async Task<bool> UpdateClaimedInterviewDispatchAsync(AcceleratorAiDispatchRecord record, Guid claimId,
        CancellationToken ct)
    {
        if (dispatch.ReconciliationClaimId != claimId) return false;
        if (dispatch.Status == AcceleratorAiDispatchStatus.Reconciled) return true;
        dispatch.ReconciliationClaimId = null; dispatch.ReconciliationClaimExpiresAtUtc = null;
        await context.SaveChangesAsync(ct); return true;
    }
    public async Task<bool> PersistClaimedInterviewResultAsync(AcceleratorAiDispatchRecord record, Guid claimId,
        CancellationToken ct)
    {
        if (dispatch.ReconciliationClaimId != claimId || dispatch.ResultJson is null) return false;
        await context.SaveChangesAsync(ct); return true;
    }
    public Task<IReadOnlyList<AcceleratorAiDispatchRecord>> UnresolvedInterviewDispatchesAsync(int skip, int take,
        CancellationToken ct) => Task.FromResult<IReadOnlyList<AcceleratorAiDispatchRecord>>(
            dispatch.Status is AcceleratorAiDispatchStatus.Uncertain or AcceleratorAiDispatchStatus.Dispatched
                ? [dispatch] : []);
}
