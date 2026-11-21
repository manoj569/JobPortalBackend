using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed partial class AcceleratorPhase2Service
{
    public async Task<IReadOnlyList<SkillGapResponse>> EnrollmentGapsAsync(Guid actor, Guid enrollmentId, CancellationToken ct)
    {
        if (journeyContexts is null || journeys is null) throw new InvalidOperationException("Journey services are required.");
        var context = await journeyContexts.GetOrCreateForEnrollmentAsync(actor, enrollmentId, ct);
        return await ContextGapsAsync(actor, context, ct);
    }

    private async Task<IReadOnlyList<SkillGapResponse>> ContextGapsAsync(Guid actor, AcceleratorJourneyContext context, CancellationToken ct)
    {
        RequireJourneyServices();
        var requirements = await journeyContexts!.ResolveAsync(actor, context.Id, ct);
        var evidence = await journeys!.EvidenceAsync(actor, context.Id, ct);
        if (context.CareerGoalId.HasValue) evidence = evidence.Concat(await store.EvidenceAsync(actor, context.CareerGoalId.Value, ct)).DistinctBy(x => x.Id).ToArray();
        var competencies = await journeys.CompetenciesAsync(requirements.Select(x => x.CompetencyId).ToArray(), ct);
        var names = competencies.ToDictionary(x => x.Id, x => x.SkillNameSnapshot);
        return requirements.Select(r =>
        {
            var best = evidence.Where(e => e.CompetencyId == r.CompetencyId).OrderByDescending(e => e.Confidence)
                .ThenByDescending(e => e.EvaluatedAtUtc).FirstOrDefault();
            var current = best?.Proficiency ?? AcceleratorProficiency.NotAssessed;
            return new SkillGapResponse(r.CompetencyId, names[r.CompetencyId], current, r.TargetProficiency,
                Math.Max(0, r.TargetProficiency - (int)current), best?.Confidence ?? 0);
        }).OrderByDescending(x => x.Gap).ThenBy(x => x.SkillName).ToArray();
    }

    private AcceleratorJourneyContextService RequireJourneyServices()
    {
        if (journeyContexts is null || journeys is null || learning is null) throw new InvalidOperationException("Journey services are required.");
        return journeyContexts;
    }

    private async Task<(DateTime TargetAtUtc, int WeeklyStudyMinutes)> JourneyPlanAsync(Guid actor, AcceleratorJourneyContext context, CancellationToken ct)
    {
        await RequireJourneyServices().GetOwnedContextAsync(actor, context.Id, ct);
        if (context.CareerGoalId.HasValue)
        {
            var goal = await RequiredGoal(actor, context.CareerGoalId.Value, ct);
            return (goal.TargetAtUtc, goal.WeeklyStudyMinutes);
        }
        var enrollment = await learning!.EnrollmentAsync(actor, context.LearningPathEnrollmentId ?? throw Invalid("Journey source is missing."), ct)
            ?? throw new NotFoundException("Enrollment not found.");
        if (!enrollment.TargetCompletionAtUtc.HasValue) throw new ConflictException("Set a target completion date before generating a roadmap.");
        return (enrollment.TargetCompletionAtUtc.Value, enrollment.WeeklyCommitmentMinutes);
    }

    public async Task<RoadmapResponse> GenerateEnrollmentRoadmapAsync(Guid actor, Guid enrollmentId, Guid requestKey, CancellationToken ct)
    {
        var context = await RequireJourneyServices().GetOrCreateForEnrollmentAsync(actor, enrollmentId, ct);
        return await GenerateContextRoadmapAsync(actor, context, requestKey, ct);
    }

    public async Task<IReadOnlyList<RecommendationResponse>> EnrollmentRecommendationsAsync(Guid actor, Guid enrollmentId, CancellationToken ct)
    {
        var context = await RequireJourneyServices().GetOrCreateForEnrollmentAsync(actor, enrollmentId, ct);
        return (await journeys!.RecommendationsAsync(actor, context.Id, ct)).Select(x => new RecommendationResponse(x.Id, x.Type, x.Reason, x.Priority, x.EvidenceJson, x.GeneratedAtUtc)).ToArray();
    }
    public async Task<AssessmentAttemptResponse> StartEnrollmentAssessmentAsync(Guid actor, Guid enrollmentId,
        AcceleratorLearningPathAssessmentPurpose purpose, Guid requestKey, CancellationToken ct)
    {
        if (journeyContexts is null || journeys is null || learning is null) throw new InvalidOperationException("Journey services are required.");
        if (requestKey == Guid.Empty || !Enum.IsDefined(purpose)) throw Invalid("Assessment purpose and request key are required.");
        var context = await journeyContexts.GetOrCreateForEnrollmentAsync(actor, enrollmentId, ct);
        return await writes.WriteAsync(async () =>
        {
            var enrollment = await learning.EnrollmentAsync(actor, enrollmentId, ct) ?? throw new NotFoundException("Enrollment not found.");
            var mapping = await journeys.AssessmentMappingAsync(enrollment.LearningPathId, purpose, ct) ?? throw new NotFoundException("Assessment mapping not found.");
            var existing = await store.AttemptByKeyAsync(actor, requestKey, ct);
            if (existing is not null)
            {
                if (existing.JourneyContextId != context.Id || existing.TemplateId != mapping.AssessmentTemplateId) throw new ConflictException("Request key belongs to another assessment.");
                return await AttemptDto(existing, ct);
            }
            var template = await store.TemplateAsync(mapping.AssessmentTemplateId, ct) ?? throw new NotFoundException("Assessment template not found.");
            var taxonomyId = template.TaxonomyId ?? template.Role?.TaxonomyId;
            if (!template.IsActive || taxonomyId != context.TaxonomyId || template.RoleId.HasValue && template.RoleId != enrollment.LearningPath.CanonicalCareerRoleId)
                throw Invalid("Assessment template is incompatible with this learning path.");
            var requirements = await journeyContexts.ResolveAsync(actor, context.Id, ct);
            var eligible = await store.QuestionsAsync(context.TaxonomyId, requirements.Select(x => x.CompetencyId).ToArray(), ct);
            var selected = SelectQuestions(eligible, template.MaximumQuestions,template.SelectionStrategy);
            if (selected.Length == 0 || template.SelectionStrategy==AcceleratorJourneyProvisioningPolicy.ObjectiveSelection && !AcceleratorJourneyProvisioningPolicy.SufficientCoverage(requirements.Count,selected.Length))
                throw new ConflictException("Insufficient active deterministic questions are configured.", "accelerator_questions_unavailable");
            var attempt = new AcceleratorAssessmentAttempt { UserId = actor, JourneyContextId = context.Id, GoalId = null,
                TemplateId = template.Id, TemplateVersion = template.Version, TaxonomyId = context.TaxonomyId, RequestKey = requestKey,
                RequestHash = Hash(new { enrollmentId, purpose, requestKey }), Status = AcceleratorAssessmentStatus.InProgress,
                AttemptNumber = await journeys.AttemptCountAsync(actor, context.Id, ct) + 1, StartedAtUtc = Now, ExpiresAtUtc = Now.AddMinutes(template.TimeLimitMinutes), SelectionStrategy = template.SelectionStrategy };
            foreach (var (question, sequence) in selected.Select((q, i) => (q, i + 1)))
                attempt.Answers.Add(new AcceleratorAssessmentAnswer { Attempt = attempt, AttemptId = attempt.Id, QuestionId = question.Id, QuestionVersion = question.Version, Sequence = sequence });
            store.Add(attempt);
            await Audit(actor, AuditAction.Create, "AcceleratorAssessmentAttempt", attempt.Id, ct);
            return await AttemptDto(attempt, ct);
        }, ct);
    }

    private static AcceleratorQuestion[] SelectQuestions(IReadOnlyList<AcceleratorQuestion> eligible, int maximum,string? strategy=null) =>
        AcceleratorAssessmentReadinessPolicy.SelectQuestions(eligible,strategy,maximum);
}
