using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public static class AcceleratorAssessmentRecoveryPolicy
{
    public static bool CanResume(AcceleratorAssessmentStatus status, DateTime expiresAtUtc, DateTime now) =>
        status==AcceleratorAssessmentStatus.InProgress && expiresAtUtc>now;
    // The existing assessment model reserves this value for an unanswered question slot.
    public static bool IsAnswered(string submittedAnswerJson) => submittedAnswerJson!="{}";
}

public sealed partial class AcceleratorPhase2Service
{
    public async Task<AssessmentAttemptResponse> AttemptAsync(Guid actor, Guid attemptId, CancellationToken ct)
    {
        await access.Candidate(actor,ct);
        var attempt=await store.ReadAttemptAsync(actor,attemptId,ct) ?? throw new NotFoundException("Assessment attempt not found.");
        await ValidateAttemptOwnership(actor,attempt,ct);
        return await AttemptDto(attempt,ct);
    }
    public async Task<AssessmentAttemptResponse?> CurrentAttemptAsync(Guid actor, Guid contextId, CancellationToken ct)
    {
        var context=await RequireJourneyServices().GetOwnedContextAsync(actor,contextId,ct);
        var attempt=await store.CurrentAttemptAsync(actor,context.Id,context.CareerGoalId,Now,ct);
        if (attempt is null) return null;
        await ValidateAttemptOwnership(actor,attempt,ct);
        return await AttemptDto(attempt,ct);
    }
    public async Task<AssessmentAttemptResponse> StartGoalAssessmentAsync(Guid actor, Guid goalId, Guid requestKey, CancellationToken ct)
    {
        await access.Candidate(actor,ct);
        if (requestKey==Guid.Empty) throw Invalid("A request key is required.");
        var goal=await RequiredGoal(actor,goalId,ct);
        var previous=await store.AttemptByKeyAsync(actor,requestKey,ct);
        if (previous is not null)
        {
            if (previous.GoalId!=goalId) throw new ConflictException("Request key belongs to another assessment.");
            return await AttemptAsync(actor,previous.Id,ct);
        }
        var template=await store.ActiveTemplateAsync(goal.RoleId,goal.TaxonomyId,ct)
            ?? throw new ConflictException("No active compatible assessment template is configured.","accelerator_assessment_not_configured");
        // Reuse the legacy engine and its original request hash, locking and finalization behavior.
        return await StartAttemptAsync(actor,new(requestKey,goalId,template.Id),ct);
    }
    public async Task<AssessmentAttemptResponse> StartJourneyAssessmentAsync(Guid actor, Guid contextId, Guid requestKey, CancellationToken ct)
    {
        var context=await RequireJourneyServices().GetOwnedContextAsync(actor,contextId,ct);
        return context.CareerGoalId.HasValue ? await StartGoalAssessmentAsync(actor,context.CareerGoalId.Value,requestKey,ct)
            : await StartEnrollmentAssessmentAsync(actor,context.LearningPathEnrollmentId!.Value,AcceleratorLearningPathAssessmentPurpose.Baseline,requestKey,ct);
    }
    private async Task ValidateAttemptOwnership(Guid actor, AcceleratorAssessmentAttempt attempt, CancellationToken ct)
    {
        if (attempt.JourneyContextId.HasValue)
        {
            var context=await RequireJourneyServices().GetOwnedContextAsync(actor,attempt.JourneyContextId.Value,ct);
            if (context.CareerGoalId!=attempt.GoalId) throw new ConflictException("Assessment journey attribution is inconsistent.");
        }
        else if (attempt.GoalId.HasValue) await RequiredGoal(actor,attempt.GoalId.Value,ct);
        else throw new ConflictException("Assessment ownership context is missing.");
    }
}
