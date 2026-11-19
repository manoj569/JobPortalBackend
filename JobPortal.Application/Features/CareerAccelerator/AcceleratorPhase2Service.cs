using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

// Deterministic only: no model/provider invocation is used for assessment, scoring, or scheduling.
public sealed partial class AcceleratorPhase2Service(IAcceleratorStore writes, IAcceleratorPhase2Store store,
    AcceleratorService access, IAuditWriter audit, TimeProvider clock,
    AcceleratorJourneyContextService? journeyContexts = null, IAcceleratorJourneyStore? journeys = null,
    IAcceleratorLearningPathStore? learning = null)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AssessmentAttemptResponse> StartAttemptAsync(Guid actor, CreateAssessmentAttemptRequest request, CancellationToken ct)
    {
        var journey = await RequireJourneyServices().GetOrCreateForCareerGoalAsync(actor, request.GoalId, ct);
        return await writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        if (request.RequestKey == Guid.Empty || request.GoalId == Guid.Empty || request.TemplateId == Guid.Empty) throw Invalid("A request key, goal and template are required.");
        var existing = await store.AttemptByKeyAsync(actor, request.RequestKey, ct);
        if (existing is not null)
        {
            if (existing.GoalId != request.GoalId || existing.TemplateId != request.TemplateId) throw new ConflictException("Idempotency key was used for a different assessment.");
            return await AttemptDto(existing, ct);
        }
        var goal = await RequiredGoal(actor, request.GoalId, ct);
        var template = await store.TemplateAsync(request.TemplateId, ct) ?? throw new NotFoundException("Assessment template not found.");
        if (!template.IsActive || template.RoleId != goal.RoleId || template.Role?.TaxonomyId != goal.TaxonomyId || template.TaxonomyId.HasValue && template.TaxonomyId != goal.TaxonomyId) throw Invalid("Assessment template is not available for this goal.");
        var requirements = await RequireJourneyServices().ResolveAsync(actor, journey.Id, ct);
        var eligible = await store.QuestionsAsync(goal.TaxonomyId, requirements.Select(x => x.CompetencyId).ToArray(), ct);
        var selected = SelectQuestions(eligible, template.MaximumQuestions);
        if (selected.Length == 0) throw new ConflictException("No active deterministic questions are configured for this role.", "accelerator_questions_unavailable");
        var attempt = new AcceleratorAssessmentAttempt { UserId = actor, GoalId = goal.Id, Goal = goal, TemplateId = template.Id, TemplateVersion = template.Version, TaxonomyId = goal.TaxonomyId, RequestKey = request.RequestKey, RequestHash = Hash(request), Status = AcceleratorAssessmentStatus.InProgress, AttemptNumber = await store.AttemptCountAsync(actor, goal.Id, ct) + 1, StartedAtUtc = Now, ExpiresAtUtc = Now.AddMinutes(template.TimeLimitMinutes), SelectionStrategy = template.SelectionStrategy };
        foreach (var (question, index) in selected.Select((q, i) => (q, i + 1))) attempt.Answers.Add(new AcceleratorAssessmentAnswer { Attempt = attempt, AttemptId = attempt.Id, QuestionId = question.Id, QuestionVersion = question.Version, Sequence = index });
        attempt.JourneyContextId = journey.Id;
        store.Add(attempt); await Audit(actor, AuditAction.Create, "AcceleratorAssessmentAttempt", attempt.Id, ct); return await AttemptDto(attempt, ct);
    }, ct);
    }

    public Task<AssessmentAttemptResponse> SubmitAnswerAsync(Guid actor, Guid attemptId, SubmitAssessmentAnswerRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); ValidateJson(request.AnswerJson);
        var attempt = await RequiredAttempt(actor, attemptId, ct);
        if (attempt.Status != AcceleratorAssessmentStatus.InProgress || attempt.ExpiresAtUtc <= Now) throw new ConflictException("Assessment attempt is no longer accepting answers.");
        var answer = attempt.Answers.SingleOrDefault(x => x.QuestionId == request.QuestionId) ?? throw new NotFoundException("Question is not part of this attempt.");
        if (answer.SubmittedAnswerJson != "{}" && answer.SubmittedAnswerJson != request.AnswerJson) throw new ConflictException("An answer for this question has already been submitted.");
        answer.SubmittedAnswerJson = request.AnswerJson; attempt.Revision = Guid.NewGuid(); return await AttemptDto(attempt, ct);
    }, ct);

    public Task<AssessmentAttemptResponse> FinalizeAttemptAsync(Guid actor, Guid attemptId, FinalizeAssessmentRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); var attempt = await RequiredAttempt(actor, attemptId, ct);
        if (attempt.Status == AcceleratorAssessmentStatus.Completed) return await AttemptDto(attempt, ct);
        if (attempt.Revision != request.ExpectedRevision) throw new ConflictException("Assessment changed; reload it.");
        if (attempt.Status != AcceleratorAssessmentStatus.InProgress) throw new ConflictException("Assessment cannot be finalized.");
        var questions = await store.QuestionsByIdsAsync(attempt.TaxonomyId, attempt.Answers.Select(x => x.QuestionId).ToArray(), ct);
        var questionsById = questions.ToDictionary(x => x.Id);
        if (attempt.Answers.Any(answer => !questionsById.ContainsKey(answer.QuestionId)))
            throw new ConflictException("Assessment question is no longer available.", "accelerator_question_unavailable");
        decimal scored = 0, total = 0;
        foreach (var answer in attempt.Answers)
        {
            var q = questionsById[answer.QuestionId]; total += q.Weight;
            var correct = IsObjectivelyCorrect(q, answer.SubmittedAnswerJson); answer.IsCorrect = correct; answer.Score = correct ? q.Weight : 0; answer.EvaluationEvidenceJson = JsonSerializer.Serialize(new { method = "deterministic-v1", correct, questionVersion = q.Version }); scored += answer.Score.Value;
        }
        attempt.Score = total == 0 ? 0 : decimal.Round(scored * 100 / total, 2); attempt.Status = AcceleratorAssessmentStatus.Completed; attempt.SubmittedAtUtc = Now; attempt.Revision = Guid.NewGuid();
        foreach (var group in attempt.Answers.GroupBy(x => questionsById[x.QuestionId].CompetencyId))
        {
            var gTotal = group.Sum(x => questionsById[x.QuestionId].Weight); var rate = gTotal == 0 ? 0 : group.Sum(x => x.Score ?? 0) / gTotal;
            store.Add(new AcceleratorSkillEvidence { UserId = actor, GoalId = attempt.GoalId, JourneyContextId = attempt.JourneyContextId, CompetencyId = group.Key, AttemptId = attempt.Id, Source = AcceleratorEvidenceSource.AssessmentDemonstrated, Proficiency = rate >= .85m ? AcceleratorProficiency.Advanced : rate >= .65m ? AcceleratorProficiency.Intermediate : rate >= .35m ? AcceleratorProficiency.Developing : AcceleratorProficiency.Beginner, Confidence = decimal.Round(rate, 4), ScoringVersion = "deterministic-v1", EvaluatedAtUtc = Now });
        }
        await Audit(actor, AuditAction.Confirm, "AcceleratorAssessmentAttempt", attempt.Id, ct); return await AttemptDto(attempt, ct);
    }, ct);

    public async Task<IReadOnlyList<SkillGapResponse>> GapsAsync(Guid actor, Guid goalId, CancellationToken ct)
    {
        var context = await RequireJourneyServices().GetOrCreateForCareerGoalAsync(actor, goalId, ct);
        return await ContextGapsAsync(actor, context, ct);
    }

    public async Task<RoadmapResponse> GenerateRoadmapAsync(Guid actor, GenerateRoadmapRequest request, CancellationToken ct)
    {
        var context = await RequireJourneyServices().GetOrCreateForCareerGoalAsync(actor, request.GoalId, ct);
        return await GenerateContextRoadmapAsync(actor, context, request.RequestKey, ct);
    }

    private Task<RoadmapResponse> GenerateContextRoadmapAsync(Guid actor, AcceleratorJourneyContext context, Guid requestKey, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        if (requestKey == Guid.Empty) throw Invalid("A request key is required.");
        var plan = await JourneyPlanAsync(actor, context, ct);
        var requirements = await RequireJourneyServices().ResolveAsync(actor, context.Id, ct);
        var gaps = await ContextGapsAsync(actor, context, ct);
        var needed = gaps.Where(x => x.Gap > 0).Select(x => x.CompetencyId).ToHashSet();
        var prerequisites = await store.PrerequisitesAsync(context.TaxonomyId, ct); ExpandPrerequisites(needed, prerequisites); var order = TopologicalOrder(needed, prerequisites);
        var prior = await journeys!.ActiveRoadmapAsync(actor, context.Id, ct);
        if (prior is null && context.CareerGoalId.HasValue) prior = await store.ActiveRoadmapAsync(actor, context.CareerGoalId.Value, ct);
        if (prior is not null) prior.Status = AcceleratorRoadmapStatus.Superseded;
        var roadmap = new AcceleratorRoadmap { UserId = actor, GoalId = context.CareerGoalId, JourneyContextId = context.Id, TaxonomyId = context.TaxonomyId, Version = (prior?.Version ?? 0) + 1, Status = AcceleratorRoadmapStatus.Active, PlannedStartAtUtc = Now.Date, TargetAtUtc = plan.TargetAtUtc, GenerationStrategy = "deterministic-prerequisite-v1" };
        var priorNodes = prior?.Nodes.ToDictionary(x => x.CompetencyId) ?? [];
        foreach (var (competencyId, i) in order.Select((x, i) => (x, i + 1)))
        {
            var preserved = priorNodes.GetValueOrDefault(competencyId);
            roadmap.Nodes.Add(new AcceleratorRoadmapNode { Roadmap = roadmap, RoadmapId = roadmap.Id, CompetencyId = competencyId, StableKey = competencyId.ToString("N"), Sequence = i,
                Status = preserved?.Status ?? (i == 1 ? AcceleratorNodeStatus.Available : AcceleratorNodeStatus.Locked), EstimatedMinutes = EstimateMinutes(competencyId, requirements),
                ProgressPercent = preserved?.ProgressPercent ?? 0, CompletionEvidenceJson = preserved?.CompletionEvidenceJson ?? "[]" });
        }
        roadmap.EstimatedMinutes = roadmap.Nodes.Sum(x => x.EstimatedMinutes); store.Add(roadmap);
        var edges = prerequisites.Where(p => needed.Contains(p.CompetencyId) && needed.Contains(p.RequiredCompetencyId)).Select(p => new AcceleratorRoadmapEdge { RoadmapId = roadmap.Id, FromNodeId = roadmap.Nodes.Single(n => n.CompetencyId == p.RequiredCompetencyId).Id, ToNodeId = roadmap.Nodes.Single(n => n.CompetencyId == p.CompetencyId).Id }).ToArray(); foreach (var edge in edges) store.Add(edge);
        var activities = Schedule(roadmap, plan.WeeklyStudyMinutes); foreach (var activity in activities) store.Add(activity); var capacity = Math.Max(0, (int)((plan.TargetAtUtc.Date - Now.Date).TotalDays / 7) * plan.WeeklyStudyMinutes); var feasible = roadmap.EstimatedMinutes <= capacity;
        roadmap.Explanation = feasible ? "Prerequisites are ordered before dependent skills using deterministic capacity scheduling." : "Timeline is infeasible at the selected study hours; increase weekly study time or extend the target date.";
        foreach (var gap in gaps.Where(x => x.Gap > 0).Take(5)) store.Add(new AcceleratorRecommendation { UserId = actor, GoalId = context.CareerGoalId, JourneyContextId = context.Id, Type = "SkillGap", Priority = Math.Min(100, gap.Gap * 20), Reason = $"Improve {gap.SkillName} to meet the target role requirement.", EvidenceJson = JsonSerializer.Serialize(gap), GeneratedAtUtc = Now });
        await Audit(actor, AuditAction.Create, "AcceleratorRoadmap", roadmap.Id, ct); return RoadmapDto(roadmap, activities, edges, feasible);
    }, ct);

    public async Task<RoadmapResponse> RoadmapAsync(Guid actor, Guid roadmapId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var roadmap = await store.RoadmapAsync(actor, roadmapId, ct) ?? throw new NotFoundException("Roadmap not found.");
        var activities = await store.ActivitiesAsync(actor, roadmap.Id, ct);
        var edges = await store.EdgesAsync(roadmap.Id, ct);
        var context = roadmap.JourneyContextId.HasValue
            ? await RequireJourneyServices().GetOwnedContextAsync(actor, roadmap.JourneyContextId.Value, ct)
            : await RequireJourneyServices().GetOrCreateForCareerGoalAsync(actor, roadmap.GoalId ?? throw Invalid("Journey source missing."), ct);
        var plan = await JourneyPlanAsync(actor, context, ct);
        var capacity = Math.Max(0, (int)((roadmap.TargetAtUtc.Date - roadmap.PlannedStartAtUtc.Date).TotalDays / 7) * plan.WeeklyStudyMinutes);
        return RoadmapDto(roadmap, activities, edges, roadmap.EstimatedMinutes <= capacity);
    }

    public Task<LearningActivityResponse> RecordProgressAsync(Guid actor, Guid activityId, RecordLearningProgressRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); ValidateJson(request.EvidenceJson); if (request.RequestKey == Guid.Empty || !Enum.IsDefined(request.Kind)) throw Invalid("A request key and progress kind are required.");
        var prior = await store.ProgressEventByKeyAsync(actor, request.RequestKey, ct);
        if (prior is not null)
        {
            if (prior.ActivityId != activityId || prior.Kind != request.Kind || !JsonEquals(prior.EvidenceJson, request.EvidenceJson)) throw new ConflictException("Idempotency key was used for different learning progress.");
            return ActivityDto(await store.ActivityAsync(actor, activityId, ct) ?? throw new NotFoundException("Learning activity not found."));
        }
        var activity = await store.ActivityAsync(actor, activityId, ct) ?? throw new NotFoundException("Learning activity not found."); if (activity.Revision != request.ExpectedRevision) throw new ConflictException("Learning activity changed; reload it.");
        store.Add(new AcceleratorProgressEvent { UserId = actor, ActivityId = activity.Id, RequestKey = request.RequestKey, Kind = request.Kind, OccurredAtUtc = Now, EvidenceJson = request.EvidenceJson });
        activity.Status = request.Kind is AcceleratorProgressKind.Completed or AcceleratorProgressKind.MilestoneCompleted ? AcceleratorNodeStatus.Completed : AcceleratorNodeStatus.InProgress; activity.Revision = Guid.NewGuid();
        await PropagateActivityAsync(actor, activity, ct); await Audit(actor, AuditAction.Update, "AcceleratorLearningActivity", activity.Id, ct); return ActivityDto(activity);
    }, ct);

    public Task<IReadOnlyList<LearningActivityResponse>> RecoverMissedScheduleAsync(Guid actor, Guid roadmapId, RecoverMissedScheduleRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); if (request.RequestKey == Guid.Empty) throw Invalid("A request key is required.");
        var roadmap = await store.RoadmapAsync(actor, roadmapId, ct) ?? throw new NotFoundException("Roadmap not found.");
        if (roadmap.Status != AcceleratorRoadmapStatus.Active) throw new ConflictException("Only an active roadmap can be rescheduled.");
        var activities = (await store.ActivitiesAsync(actor, roadmapId, ct)).ToList(); var missed = activities.Where(x => x.ScheduledAtUtc.Date < Now.Date && x.Status is not AcceleratorNodeStatus.Completed).OrderBy(x => x.ScheduledAtUtc).ToArray();
        if (missed.Length == 0) return (IReadOnlyList<LearningActivityResponse>)activities.Select(ActivityDto).ToArray();
        var next = activities.Where(x => x.ScheduledAtUtc.Date >= Now.Date && x.Status is not AcceleratorNodeStatus.Completed).Select(x => x.ScheduledAtUtc.Date).DefaultIfEmpty(Now.Date).Max();
        foreach (var activity in missed)
        {
            var key = DeterministicKey(request.RequestKey, activity.Id);
            if (await store.ProgressEventByKeyAsync(actor, key, ct) is null) store.Add(new AcceleratorProgressEvent { UserId = actor, ActivityId = activity.Id, RequestKey = key, Kind = AcceleratorProgressKind.Rescheduled, OccurredAtUtc = Now, EvidenceJson = JsonSerializer.Serialize(new { recoveredFromUtc = activity.ScheduledAtUtc }) });
            activity.ScheduledAtUtc = next = next.AddDays(1); activity.Revision = Guid.NewGuid();
        }
        await Audit(actor, AuditAction.Update, "AcceleratorRoadmap", roadmap.Id, ct); return (IReadOnlyList<LearningActivityResponse>)activities.OrderBy(x => x.ScheduledAtUtc).Select(ActivityDto).ToArray();
    }, ct);

    public async Task<IReadOnlyList<RecommendationResponse>> RecommendationsAsync(Guid actor, Guid goalId, CancellationToken ct)
    {
        var context = await RequireJourneyServices().GetOrCreateForCareerGoalAsync(actor, goalId, ct);
        var records = (await journeys!.RecommendationsAsync(actor, context.Id, ct)).Concat(await store.RecommendationsAsync(actor, goalId, ct)).DistinctBy(x => x.Id);
        return records.Select(x => new RecommendationResponse(x.Id, x.Type, x.Reason, x.Priority, x.EvidenceJson, x.GeneratedAtUtc)).ToArray();
    }

    // Called inside the project evaluation write transaction. The evaluation, evidence, and progress event commit together.
    public async Task RecordProjectEvaluationAsync(AcceleratorCandidateProject project,
        AcceleratorProjectSubmission submission, AcceleratorProjectEvaluation evaluation,
        AcceleratorProjectRubric rubric, CancellationToken ct)
    {
        var evidence = ProjectEvidencePolicy.Create(project, submission, evaluation, rubric);
        var previousEvidence = await store.EvidenceAsync(project.UserId, project.GoalId, ct);
        ProjectEvidencePolicy.PreserveStrongerVerifiedProficiency(evidence, previousEvidence);
        foreach (var item in evidence) store.Add(item);
        if (evidence.Count == 0) return;

        var roadmap = await store.ActiveRoadmapAsync(project.UserId, project.GoalId, ct);
        if (roadmap is null) return;
        var activities = await store.ActivitiesAsync(project.UserId, roadmap.Id, ct);
        foreach (var item in evidence)
        {
            var node = roadmap.Nodes.SingleOrDefault(x => x.CompetencyId == item.CompetencyId);
            if (node is null) continue;
            var activity = activities.FirstOrDefault(x => x.RoadmapNodeId == node.Id);
            if (activity is null) continue;
            var key = DeterministicKey(evaluation.Id, activity.Id);
            if (await store.ProgressEventByKeyAsync(project.UserId, key, ct) is not null) continue;
            store.Add(new AcceleratorProgressEvent
            {
                UserId = project.UserId,
                ActivityId = activity.Id,
                RequestKey = key,
                Kind = AcceleratorProgressKind.EvidenceSubmitted,
                OccurredAtUtc = Now,
                EvidenceJson = JsonSerializer.Serialize(new { source = "VirtualProject", evaluationId = evaluation.Id,
                    submissionId = submission.Id, competencyId = item.CompetencyId, score = item.Score })
            });
            if (node.Status == AcceleratorNodeStatus.Completed) continue;
            node.ProgressPercent = Math.Max(node.ProgressPercent, Math.Min(80, (int)(item.Confidence * 100)));
            if (node.Status is AcceleratorNodeStatus.Available or AcceleratorNodeStatus.NeedsPractice)
                node.Status = AcceleratorNodeStatus.InProgress;
            if (activity.Status == AcceleratorNodeStatus.Available)
            {
                activity.Status = AcceleratorNodeStatus.InProgress;
                activity.Revision = Guid.NewGuid();
            }
        }
    }

    public Task<Guid> UpsertTemplateAsync(Guid actor, UpsertAssessmentTemplateRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(actor, ct); if (request.RoleId == Guid.Empty || string.IsNullOrWhiteSpace(request.Version) || request.Version.Length > 80 || request.TimeLimitMinutes is < 1 or > 240 || request.MaximumQuestions is < 1 or > 100) throw Invalid("Invalid assessment template.");
        var template = await store.TemplateAsync(request.RoleId, request.Version.Trim(), ct);
        if (template is null) { template = new AcceleratorAssessmentTemplate { RoleId = request.RoleId, Version = request.Version.Trim(), TimeLimitMinutes = request.TimeLimitMinutes, MaximumQuestions = request.MaximumQuestions, IsActive = request.IsActive }; store.Add(template); }
        else { template.TimeLimitMinutes = request.TimeLimitMinutes; template.MaximumQuestions = request.MaximumQuestions; template.IsActive = request.IsActive; }
        await audit.AppendAsync(new AuditEvent(AuditAction.Update, "AcceleratorAssessmentTemplate", template.Id.ToString(), Actor: new AuditActor(actor, "Administrator")), ct); return template.Id;
    }, ct);

    public Task<Guid> UpsertQuestionAsync(Guid actor, UpsertAssessmentQuestionRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(actor, ct); ValidateJson(request.OptionsJson); ValidateJson(request.AnswerKeyJson); ValidateJson(request.RubricJson);
        if (request.TaxonomyId == Guid.Empty || request.CompetencyId == Guid.Empty || string.IsNullOrWhiteSpace(request.Version) || request.Version.Length > 80 || request.Difficulty is < 1 or > 5 || request.Weight <= 0 || request.Weight > 100 || string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 8000 || request.Explanation?.Length > 8000 || !Enum.IsDefined(request.Type)) throw Invalid("Invalid assessment question.");
        var question = await store.QuestionAsync(request.CompetencyId, request.Version.Trim(), ct);
        if (question is null) { question = new AcceleratorQuestion { TaxonomyId = request.TaxonomyId, CompetencyId = request.CompetencyId, Version = request.Version.Trim() }; store.Add(question); }
        else if (question.TaxonomyId != request.TaxonomyId) throw new ConflictException("Question version belongs to a different taxonomy.");
        question.Type = request.Type; question.Difficulty = request.Difficulty; question.Weight = request.Weight; question.Prompt = request.Prompt.Trim(); question.OptionsJson = request.OptionsJson; question.AnswerKeyJson = request.AnswerKeyJson; question.RubricJson = request.RubricJson; question.Explanation = request.Explanation ?? string.Empty; question.IsActive = request.IsActive;
        await audit.AppendAsync(new AuditEvent(AuditAction.Update, "AcceleratorQuestion", question.Id.ToString(), Actor: new AuditActor(actor, "Administrator")), ct); return question.Id;
    }, ct);

    private async Task<AcceleratorCareerGoal> RequiredGoal(Guid actor, Guid id, CancellationToken ct) => await writes.GoalAsync(actor, id, ct) ?? throw new NotFoundException("Career goal not found.");
    private async Task<AcceleratorAssessmentAttempt> RequiredAttempt(Guid actor, Guid id, CancellationToken ct)
    {
        var attempt = await store.AttemptAsync(actor, id, ct) ?? throw new NotFoundException("Assessment attempt not found.");
        if (attempt.JourneyContextId.HasValue && journeyContexts is not null)
            await journeyContexts.GetOwnedContextAsync(actor, attempt.JourneyContextId.Value, ct);
        else if (!attempt.GoalId.HasValue) throw new ConflictException("Assessment ownership context is missing.");
        return attempt;
    }
    private async Task<AssessmentAttemptResponse> AttemptDto(AcceleratorAssessmentAttempt a, CancellationToken ct) { var questions = await store.QuestionsByIdsAsync(a.TaxonomyId, a.Answers.Select(x => x.QuestionId).ToArray(), ct); return new(a.Id, a.GoalId, a.Status, a.Revision, a.ExpiresAtUtc, a.Score, a.Answers.OrderBy(x => x.Sequence).Select(x => { var q = questions.SingleOrDefault(q => q.Id == x.QuestionId) ?? throw new ConflictException("Assessment question is no longer available.", "accelerator_question_unavailable"); return new AssessmentQuestionResponse(q.Id, x.Sequence, q.Prompt, q.OptionsJson, q.Type, q.Difficulty); }).ToArray()); }
    private static bool IsObjectivelyCorrect(AcceleratorQuestion q, string answer) => q.Type is AcceleratorQuestionType.MultipleChoice or AcceleratorQuestionType.MultipleSelect or AcceleratorQuestionType.TrueFalse && JsonEquals(q.AnswerKeyJson, answer);
    private static string CanonicalJson(string json) { using var doc = JsonDocument.Parse(json); return JsonSerializer.Serialize(doc.RootElement); }
    private static bool JsonEquals(string left, string right) => CanonicalJson(left) == CanonicalJson(right);
    private static void ValidateJson(string json) { try { using var _ = JsonDocument.Parse(json); } catch (JsonException) { throw Invalid("Answer and evidence must be valid JSON."); } }
    private static int EstimateMinutes(Guid competencyId, IReadOnlyList<AcceleratorJourneyRequirement> r) { var requirement = r.SingleOrDefault(x => x.CompetencyId == competencyId); return requirement is null ? 120 : 120 + 60 * requirement.TargetProficiency; }
    private static void ExpandPrerequisites(HashSet<Guid> needed, IReadOnlyList<AcceleratorPrerequisite> edges) { for (var changed = true; changed;) { changed = false; foreach (var e in edges.Where(x => needed.Contains(x.CompetencyId))) changed |= needed.Add(e.RequiredCompetencyId); } }
    private static Guid[] TopologicalOrder(HashSet<Guid> nodes, IReadOnlyList<AcceleratorPrerequisite> edges) { var incoming = nodes.ToDictionary(x => x, _ => 0); var children = nodes.ToDictionary(x => x, _ => new List<Guid>()); foreach (var e in edges.Where(x => nodes.Contains(x.CompetencyId) && nodes.Contains(x.RequiredCompetencyId))) { incoming[e.CompetencyId]++; children[e.RequiredCompetencyId].Add(e.CompetencyId); } var queue = new SortedSet<Guid>(incoming.Where(x => x.Value == 0).Select(x => x.Key)); var result = new List<Guid>(); while (queue.Count > 0) { var id = queue.Min; queue.Remove(id); result.Add(id); foreach (var child in children[id]) if (--incoming[child] == 0) queue.Add(child); } if (result.Count != nodes.Count) throw new ConflictException("Taxonomy prerequisite graph contains a cycle.", "accelerator_graph_cycle"); return result.ToArray(); }
    private List<AcceleratorLearningActivity> Schedule(AcceleratorRoadmap roadmap, int weeklyMinutes)
    {
        var date = Now.Date;
        var result = new List<AcceleratorLearningActivity>();

        foreach (var node in roadmap.Nodes)
        {
            result.Add(new AcceleratorLearningActivity
            {
                RoadmapNodeId = node.Id,
                UserId = roadmap.UserId,
                Title = $"Learn competency {node.Sequence}",
                EstimatedMinutes = node.EstimatedMinutes,
                ScheduledAtUtc = date,
                Status = node.Status == AcceleratorNodeStatus.Completed
                    ? AcceleratorNodeStatus.Completed
                    : AcceleratorNodeStatus.Locked
            });

            var durationDays = Math.Max(1, (int)Math.Ceiling((double)node.EstimatedMinutes / weeklyMinutes * 7));
            date = date.AddDays(durationDays);
        }

        return result;
    }
    private static RoadmapResponse RoadmapDto(AcceleratorRoadmap r, IReadOnlyList<AcceleratorLearningActivity> a, IReadOnlyList<AcceleratorRoadmapEdge> edges, bool feasible) => new(r.Id, r.GoalId, r.Version, r.Status, r.TargetAtUtc, r.EstimatedMinutes, r.Explanation, feasible, r.Nodes.OrderBy(x => x.Sequence).Select(x => new RoadmapNodeResponse(x.Id, x.CompetencyId, x.StableKey, x.Sequence, x.Status, x.EstimatedMinutes, x.ProgressPercent)).ToArray(), edges.Select(x => new RoadmapEdgeResponse(x.FromNodeId, x.ToNodeId)).ToArray(), a.Select(ActivityDto).ToArray());
    private static LearningActivityResponse ActivityDto(AcceleratorLearningActivity a) => new(a.Id, a.RoadmapNodeId, a.Title, a.EstimatedMinutes, a.ScheduledAtUtc, a.Status, a.Revision);
    private Task Audit(Guid actor, AuditAction action, string type, Guid id, CancellationToken ct) => audit.AppendAsync(new AuditEvent(action, type, id.ToString(), Actor: new AuditActor(actor, "Candidate")), ct);
    private static string Hash<T>(T request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    private static BadRequestException Invalid(string message) => new(message, "accelerator_invalid_request");
    private static Guid DeterministicKey(Guid scope, Guid item) { Span<byte> bytes = stackalloc byte[32]; scope.TryWriteBytes(bytes); item.TryWriteBytes(bytes[16..]); return new Guid(SHA256.HashData(bytes)[..16]); }
    private async Task PropagateActivityAsync(Guid actor, AcceleratorLearningActivity activity, CancellationToken ct)
    {
        var node = await store.NodeAsync(activity.RoadmapNodeId, ct) ?? throw new NotFoundException("Roadmap node not found."); var roadmap = await store.RoadmapAsync(actor, node.RoadmapId, ct) ?? throw new NotFoundException("Roadmap not found."); var activities = await store.ActivitiesAsync(actor, roadmap.Id, ct); var nodeActivities = activities.Where(x => x.RoadmapNodeId == node.Id).ToArray();
        node.ProgressPercent = nodeActivities.Length == 0 ? 0 : nodeActivities.Count(x => x.Status == AcceleratorNodeStatus.Completed) * 100 / nodeActivities.Length;
        if (node.ProgressPercent != 100) { node.Status = AcceleratorNodeStatus.InProgress; return; }
        node.Status = AcceleratorNodeStatus.Completed; node.CompletionEvidenceJson = JsonSerializer.Serialize(new { completedAtUtc = Now });
        var edges = await store.EdgesAsync(roadmap.Id, ct);
        foreach (var targetId in edges.Where(x => x.FromNodeId == node.Id).Select(x => x.ToNodeId).Distinct())
        {
            var target = roadmap.Nodes.Single(x => x.Id == targetId); var dependencies = edges.Where(x => x.ToNodeId == targetId).Select(x => x.FromNodeId).ToArray();
            if (dependencies.All(id => roadmap.Nodes.Single(x => x.Id == id).Status == AcceleratorNodeStatus.Completed) && target.Status == AcceleratorNodeStatus.Locked) target.Status = AcceleratorNodeStatus.Available;
        }
    }
}
