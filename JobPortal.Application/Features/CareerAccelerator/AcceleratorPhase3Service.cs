using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerAccelerator;

// The only tutor dispatch boundary. The default provider is unavailable and never fabricates an assistant reply.
public sealed class AcceleratorPhase3Service(IAcceleratorStore writes, IAcceleratorPhase3Store store,
    IAcceleratorPhase2Store phase2, AcceleratorService access, ITutorContextBuilder contextBuilder, IEnumerable<IAiTutorProvider> providers,
    AcceleratorQuotaService quota, IAuditWriter audit, TimeProvider clock, IOptions<AcceleratorOptions> options,
    AcceleratorJourneyContextService contexts, IAcceleratorLearningPathStore learning)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<TutorConversationResponse> StartConversationAsync(Guid actor, StartTutorConversationRequest request, CancellationToken ct)
    {
        var context = await ResolveScope(actor, request.GoalId, request.EnrollmentId, ct);
        return await writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); ValidateText(request.Topic, 500); ValidateText(request.Title, 500);
        if (request.RequestKey == Guid.Empty || request.CompetencyId == Guid.Empty) throw Invalid("A request key and competency are required.");
        await ValidateAttribution(actor, context, request.CompetencyId, request.RoadmapNodeId, request.ModuleId, request.LessonId, ct);
        var existing = await store.ConversationByKeyAsync(actor, request.RequestKey, ct);
        var hash = Hash(request);
        if (existing is not null) { if (existing.RequestHash != hash) throw new ConflictException("Idempotency key was used for a different conversation."); return ConversationDto(existing); }
        var conversation = new AcceleratorTutorConversation { UserId = actor, GoalId = context.CareerGoalId, JourneyContextId = context.Id, ModuleId = request.ModuleId, LessonId = request.LessonId, CompetencyId = request.CompetencyId, RoadmapNodeId = request.RoadmapNodeId, RequestKey = request.RequestKey, RequestHash = hash, Topic = request.Topic.Trim(), Title = request.Title.Trim(), LastMessageAtUtc = Now };
        store.Add(conversation); await Audit(actor, AuditAction.Create, "AcceleratorTutorConversation", conversation.Id, ct); return ConversationDto(conversation);
    }, ct);
    }

    public async Task<IReadOnlyList<TutorConversationResponse>> ConversationsAsync(Guid actor, int page, int pageSize, CancellationToken ct, Guid? contextId = null)
    {
        await access.Candidate(actor, ct); AcceleratorService.Page(page, pageSize);
        if (contextId.HasValue) await contexts.GetOwnedContextAsync(actor, contextId.Value, ct);
        return (await store.ConversationsAsync(actor, (page - 1) * pageSize, pageSize, ct, contextId)).Select(ConversationDto).ToArray();
    }
    public async Task<TutorConversationResponse> ConversationAsync(Guid actor, Guid id, CancellationToken ct)
    { await access.Candidate(actor, ct); return ConversationDto(await RequiredConversation(actor, id, ct)); }
    public async Task<IReadOnlyList<TutorMessageResponse>> MessagesAsync(Guid actor, Guid conversationId, CancellationToken ct)
    { await access.Candidate(actor, ct); await RequiredConversation(actor, conversationId, ct); return (await store.MessagesAsync(actor, conversationId, 100, ct)).Select(MessageDto).ToArray(); }
    public Task<TutorConversationResponse> ArchiveConversationAsync(Guid actor, Guid id, Guid expectedRevision, CancellationToken ct) => writes.WriteAsync(async () =>
    { await access.Candidate(actor, ct); var c = await RequiredConversation(actor, id, ct); if (c.Revision != expectedRevision) throw new ConflictException("Conversation changed; reload it."); c.Status = AcceleratorTutorConversationStatus.Archived; c.Revision = Guid.NewGuid(); await Audit(actor, AuditAction.Update, "AcceleratorTutorConversation", c.Id, ct); return ConversationDto(c); }, ct);

    public async Task<TutorMessageResponse> SendMessageAsync(Guid actor, Guid conversationId, SendTutorMessageRequest request, CancellationToken ct)
    {
        await access.Candidate(actor, ct); ValidateText(request.Content, 8000); if (request.RequestKey == Guid.Empty || !Enum.IsDefined(request.Mode)) throw Invalid("A request key and mode are required.");
        var c = await RequiredConversation(actor, conversationId, ct); if (c.Status != AcceleratorTutorConversationStatus.Active) throw new ConflictException("Conversation is archived.");
        var context = await contextBuilder.BuildAsync(actor, c, request.Content, ct);
        var requestHash = Hash(request); var prior = await store.DispatchByKeyAsync(actor, request.RequestKey, ct);
        if (prior is not null)
        {
            if (prior.ConversationId != c.Id || prior.RequestHash != requestHash) throw new ConflictException("Idempotency key was used for a different tutor request.");
            if (prior.Status is AcceleratorAiDispatchStatus.Uncertain or AcceleratorAiDispatchStatus.Dispatched) throw new ConflictException("Tutor request outcome is pending reconciliation.", "accelerator_dispatch_pending");
            var history = await store.MessagesAsync(actor, c.Id, 100, ct); var delivered = history.LastOrDefault(x => x.DispatchId == prior.Id && x.Role == AcceleratorTutorMessageRole.Assistant);
            if (delivered is not null)
            {
                var cost = delivered.EstimatedCostInr ?? throw new ConflictException("Verified tutor usage metadata is incomplete; reconciliation is required.");
                await quota.CompleteAsync(actor, prior.ReservationId, delivered.InputTokens, delivered.OutputTokens, cost, ct);
                return MessageDto(delivered);
            }
            throw new ConflictException("Tutor request previously failed.", "accelerator_dispatch_failed");
        }
        var settings = options.Value;
        if (!settings.AIEnabled || !settings.OperationEnabled("TutorMessage") ||
            !settings.Policies.TryGetValue(AcceleratorFeature.Tutor, out var policy) || !policy.Enabled)
            throw new TutorProviderUnavailableException();
        var provider = providers.FirstOrDefault(x => x.IsAvailable && x.Name == policy.Provider)
            ?? throw new TutorProviderUnavailableException();
        var reservation = await quota.ReserveAsync(actor, new ReserveAcceleratorUsageRequest(request.RequestKey, AcceleratorFeature.Tutor, requestHash, context.TokenEstimate, Encoding.UTF8.GetByteCount(request.Content)), ct);
        var dispatch = await writes.WriteAsync(() => {
            var row = new AcceleratorAiDispatchRecord { UserId = actor, ConversationId = c.Id, RequestKey = request.RequestKey, ReservationId = reservation.Id, Operation = "TutorMessage", Provider = provider.Name, Model = policy.Model, Status = AcceleratorAiDispatchStatus.Reserved, RequestHash = requestHash };
            store.Add(row); return Task.FromResult(row);
        }, ct);
        await quota.MarkDispatchedAsync(actor, reservation.Id, ct);
        await writes.WriteAsync(async () =>
        {
            var row = await store.DispatchAsync(dispatch.Id, ct) ?? throw new NotFoundException("Tutor dispatch not found.");
            if (row.Status != AcceleratorAiDispatchStatus.Reserved) throw new ConflictException("Tutor dispatch already started.");
            row.Status = AcceleratorAiDispatchStatus.Dispatched; row.Revision = Guid.NewGuid(); row.NextReconciliationAtUtc = Now.AddMinutes(2);
            return 0;
        }, ct); try
        {
            var result = await provider.SendAsync(new TutorProviderRequest(dispatch.Id, reservation.Id, request.RequestKey.ToString("N"), request.Mode, context, request.Content, policy.MaxOutputTokens), ct);
            var response = await writes.WriteAsync(async () =>
            {
                var current = await RequiredConversation(actor, c.Id, ct);
                var row = await store.DispatchAsync(dispatch.Id, ct) ?? throw new NotFoundException("Tutor dispatch not found.");
                var message = new AcceleratorTutorMessage { ConversationId = c.Id, Role = AcceleratorTutorMessageRole.Assistant, Status = AcceleratorTutorMessageStatus.Delivered, Content = result.Content, MessageType = request.Mode.ToString(), Provider = provider.Name, Model = result.Model, InputTokens = result.InputTokens, OutputTokens = result.OutputTokens, EstimatedCostInr = result.EstimatedCostInr, DispatchId = dispatch.Id };
                store.Add(message); current.LastMessageAtUtc = Now; current.Revision = Guid.NewGuid();
                row.Status = AcceleratorAiDispatchStatus.Succeeded; row.ProviderRequestId = result.ProviderRequestId; row.FinishedAtUtc = Now; row.Revision = Guid.NewGuid();
                row.ResultJson = JsonSerializer.Serialize(result); row.InputTokens = result.InputTokens; row.OutputTokens = result.OutputTokens; row.ActualCostInr = result.EstimatedCostInr;
                return MessageDto(message);
            }, ct);
            await quota.CompleteAsync(actor, reservation.Id, result.InputTokens, result.OutputTokens, result.EstimatedCostInr, ct); await Audit(actor, AuditAction.Confirm, "AcceleratorAiDispatchRecord", dispatch.Id, ct); return response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { await MarkUncertainAsync(dispatch.Id, "provider_timeout"); throw; }
        catch
        { await MarkUncertainAsync(dispatch.Id, "provider_outcome_unknown"); throw; }
    }

    private Task<int> MarkUncertainAsync(Guid dispatchId, string reason) => writes.WriteAsync(async () =>
    {
        var row = await store.DispatchAsync(dispatchId, CancellationToken.None);
        if (row is not null && row.Status is not (AcceleratorAiDispatchStatus.Succeeded or AcceleratorAiDispatchStatus.Reconciled))
        {
            // Do not release the reservation: the external outcome may have been chargeable.
            row.Status = AcceleratorAiDispatchStatus.Uncertain;
            row.FailureCode = reason;
            row.NextReconciliationAtUtc = Now.AddMinutes(2);
            row.Revision = Guid.NewGuid();
        }
        return 0;
    }, CancellationToken.None);

    public async Task<PracticeSessionResponse> PracticeSessionAsync(Guid actor, Guid sessionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        return PracticeDto(await RequiredPractice(actor, sessionId, ct));
    }

    public async Task<IReadOnlyList<PracticeSessionResponse>> PracticeHistoryAsync(Guid actor, int page, int pageSize, CancellationToken ct, Guid? contextId = null)
    {
        await access.Candidate(actor, ct); AcceleratorService.Page(page, pageSize);
        if (contextId.HasValue) await contexts.GetOwnedContextAsync(actor, contextId.Value, ct);
        var sessions = await store.PracticeSessionsAsync(actor, (page - 1) * pageSize, pageSize, ct, contextId);
        // History intentionally does not return question answers or rubrics.
        return sessions.Select(x => new PracticeSessionResponse(x.Id, x.Status, x.Difficulty, x.QuestionType, [], x.JourneyContextId, x.GoalId, x.ModuleId, x.LessonId)).ToArray();
    }

    public async Task<PracticeResultResponse> PracticeResultsAsync(Guid actor, Guid sessionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var session = await RequiredPractice(actor, sessionId, ct);
        var evaluations = await store.PracticeEvaluationsAsync(actor, sessionId, ct);
        var correct = evaluations.Count(x => x.Score == x.MaximumScore);
        return new PracticeResultResponse(session.Id, session.Status, session.Questions.Count, evaluations.Count, correct,
            evaluations.Count == 0 ? 0 : decimal.Round(evaluations.Sum(x => x.Score) * 100 / evaluations.Sum(x => x.MaximumScore), 2), evaluations.Select(EvaluationDto).ToArray());
    }

    public async Task<IReadOnlyList<PracticeRecommendationResponse>> PracticeRecommendationsAsync(Guid actor, CancellationToken ct, Guid? contextId = null)
    {
        await access.Candidate(actor, ct);
        if (contextId.HasValue) await contexts.GetOwnedContextAsync(actor, contextId.Value, ct);
        var sessions = await store.PracticeSessionsAsync(actor, 0, 100, ct, contextId);
        var recommendations = new List<PracticeRecommendationResponse>();
        foreach (var group in sessions.GroupBy(x => x.CompetencyId))
        {
            var recent = group.OrderByDescending(x => x.CreatedAtUtc).Take(5).ToArray();
            var evaluations = new List<AcceleratorPracticeEvaluation>();
            foreach (var session in recent) evaluations.AddRange(await store.PracticeEvaluationsAsync(actor, session.Id, ct));
            var accuracy = evaluations.Count == 0 ? 0 : decimal.Round(evaluations.Count(x => x.Score == x.MaximumScore) * 100m / evaluations.Count, 2);
            var difficulty = evaluations.Count >= 3 && accuracy >= 80 ? AcceleratorPracticeDifficulty.Advanced :
                evaluations.Count >= 3 && accuracy >= 60 ? AcceleratorPracticeDifficulty.Intermediate : AcceleratorPracticeDifficulty.Beginner;
            recommendations.Add(new PracticeRecommendationResponse(group.Key, difficulty,
                evaluations.Count == 0 ? "Start with foundational practice." : accuracy < 60 ? "Review prerequisites and retry foundational questions." : accuracy < 80 ? "Continue practice to improve consistency." : "Try harder questions after sustained correct answers.", evaluations.Count, accuracy));
        }
        return recommendations;
    }

    public Task<int> EraseTutorDataAsync(Guid actor, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); var conversations = await store.ConversationsAsync(actor, 0, 100, ct); foreach (var c in conversations) { c.Status = AcceleratorTutorConversationStatus.Erased; c.Topic = string.Empty; c.Title = "Erased"; c.Revision = Guid.NewGuid(); } await Audit(actor, AuditAction.Delete, "AcceleratorTutorConversation", actor, ct); return conversations.Count;
    }, ct);

    public async Task<PracticeSessionResponse> StartPracticeAsync(Guid actor, StartPracticeSessionRequest request, CancellationToken ct)
    {
        var context = await ResolveScope(actor, request.GoalId, request.EnrollmentId, ct);
        return await writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); if (request.RequestKey == Guid.Empty || request.CompetencyId == Guid.Empty || !Enum.IsDefined(request.Difficulty) || !Enum.IsDefined(request.QuestionType)) throw Invalid("Invalid practice request.");
        await ValidateAttribution(actor, context, request.CompetencyId, request.RoadmapNodeId, request.ModuleId, request.LessonId, ct);
        var previous = await store.PracticeSessionByKeyAsync(actor, request.RequestKey, ct); var hash = Hash(request);
        if (previous is not null) { if (previous.RequestHash != hash) throw new ConflictException("Idempotency key was used for different practice."); return PracticeDto(previous); }
        var questions = await phase2.QuestionsAsync(context.TaxonomyId, [request.CompetencyId], ct);
        var selected = questions.Where(x => x.IsActive && x.Type is AcceleratorQuestionType.MultipleChoice or AcceleratorQuestionType.MultipleSelect or AcceleratorQuestionType.TrueFalse)
            .Where(x => ObjectiveType(x.Type) == request.QuestionType).OrderBy(x => x.Difficulty).ThenBy(x => x.Id).Take(5).ToArray();
        if (selected.Length == 0) throw new ConflictException("No validated deterministic practice questions are available.", "accelerator_practice_unavailable");
        var session = new AcceleratorPracticeSession { UserId = actor, GoalId = context.CareerGoalId, JourneyContextId = context.Id, ModuleId = request.ModuleId, LessonId = request.LessonId, CompetencyId = request.CompetencyId, RoadmapNodeId = request.RoadmapNodeId, RequestKey = request.RequestKey, RequestHash = hash, Difficulty = request.Difficulty, QuestionType = request.QuestionType, Status = AcceleratorPracticeStatus.Ready };
        foreach (var (question, sequence) in selected.Select((q, i) => (q, i + 1))) session.Questions.Add(new AcceleratorPracticeQuestion { Session = session, SessionId = session.Id, Sequence = sequence, Prompt = question.Prompt, PublicOptionsJson = question.OptionsJson, AnswerKeyJson = question.AnswerKeyJson, RubricJson = question.RubricJson, GenerationVersion = question.Version, IsQualityApproved = true });
        store.Add(session); await Audit(actor, AuditAction.Create, "AcceleratorPracticeSession", session.Id, ct); return PracticeDto(session);
    }, ct);
    }

    public Task<PracticeEvaluationResponse> SubmitPracticeAnswerAsync(Guid actor, Guid sessionId, SubmitPracticeAnswerRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); ValidateJson(request.AnswerJson); if (request.RequestKey == Guid.Empty) throw Invalid("A request key is required.");
        var session = await RequiredPractice(actor, sessionId, ct);
        var prior = await store.PracticeAttemptByKeyAsync(actor, request.RequestKey, ct);
        if (prior is not null)
        {
            if (prior.SessionId != sessionId || prior.QuestionId != request.QuestionId || !JsonEquals(prior.AnswerJson, request.AnswerJson))
                throw new ConflictException("Idempotency key was used for a different practice answer.");
            return EvaluationDto((await store.PracticeEvaluationsAsync(actor, prior.SessionId, ct)).Single(x => x.AttemptId == prior.Id));
        }
        var question = await store.PracticeQuestionAsync(actor, sessionId, request.QuestionId, ct) ?? throw new NotFoundException("Practice question not found.");
        var attempt = new AcceleratorPracticeAttempt { UserId = actor, SessionId = session.Id, QuestionId = question.Id, RequestKey = request.RequestKey, AnswerJson = request.AnswerJson, SubmittedAtUtc = Now };
        var correct = JsonEquals(question.AnswerKeyJson, request.AnswerJson); var evaluation = new AcceleratorPracticeEvaluation { AttemptId = attempt.Id, Score = correct ? 1 : 0, MaximumScore = 1, Classification = correct ? "Correct" : "Incorrect", StrengthsJson = correct ? "[\"Objective answer matched\"]" : "[]", WeaknessesJson = correct ? "[]" : "[\"Review the concept and explanation\"]", Explanation = correct ? "Your answer matched the validated key." : "Your answer did not match the validated key.", SuggestedImprovement = correct ? "Try a higher-difficulty variation." : "Review prerequisites before another attempt.", Method = AcceleratorPracticeEvaluationMethod.Deterministic, Confidence = 1, RubricVersion = question.GenerationVersion };
        store.Add(attempt); store.Add(evaluation); session.Status = AcceleratorPracticeStatus.Evaluated; session.Revision = Guid.NewGuid();
        if (correct) store.Add(new AcceleratorSkillEvidence { UserId = actor, GoalId = session.GoalId, JourneyContextId = session.JourneyContextId, CompetencyId = session.CompetencyId, Source = AcceleratorEvidenceSource.AssignmentDemonstrated, Proficiency = AcceleratorProficiency.Developing, Confidence = .25m, ScoringVersion = "practice-objective-v1", EvaluatedAtUtc = Now });
        await Audit(actor, AuditAction.Confirm, "AcceleratorPracticeAttempt", attempt.Id, ct); return EvaluationDto(evaluation);
    }, ct);

    private async Task<AcceleratorTutorConversation> RequiredConversation(Guid actor, Guid id, CancellationToken ct)
    {
        var conversation = await store.ConversationAsync(actor, id, ct) ?? throw new NotFoundException("Tutor conversation not found.");
        await ValidateOwnedJourney(actor, conversation.JourneyContextId, conversation.GoalId, ct);
        return conversation;
    }
    private async Task<AcceleratorPracticeSession> RequiredPractice(Guid actor, Guid id, CancellationToken ct)
    {
        var session = await store.PracticeSessionAsync(actor, id, ct) ?? throw new NotFoundException("Practice session not found.");
        await ValidateOwnedJourney(actor, session.JourneyContextId, session.GoalId, ct);
        return session;
    }
    private async Task ValidateOwnedJourney(Guid actor, Guid? contextId, Guid? goalId, CancellationToken ct)
    {
        if (!contextId.HasValue) throw new ConflictException("Journey context backfill is required.");
        var context = await contexts.GetOwnedContextAsync(actor, contextId.Value, ct);
        if (context.CareerGoalId != goalId) throw new ConflictException("Journey attribution is inconsistent.");
    }
    private static TutorConversationResponse ConversationDto(AcceleratorTutorConversation c) => new(c.Id, c.GoalId, c.CompetencyId, c.RoadmapNodeId, c.Topic, c.Title, c.Status, c.LastMessageAtUtc, c.Revision, c.JourneyContextId, c.ModuleId, c.LessonId);
    private static TutorMessageResponse MessageDto(AcceleratorTutorMessage m) => new(m.Id, m.Role, m.Status, m.Content, m.MessageType, m.CreatedAtUtc);
    private static PracticeSessionResponse PracticeDto(AcceleratorPracticeSession s) => new(s.Id, s.Status, s.Difficulty, s.QuestionType, s.Questions.OrderBy(x => x.Sequence).Select(x => new PracticeQuestionResponse(x.Id, x.Sequence, x.Prompt, x.PublicOptionsJson, s.QuestionType, s.Difficulty)).ToArray(), s.JourneyContextId, s.GoalId, s.ModuleId, s.LessonId);

    private Task<AcceleratorJourneyContext> ResolveScope(Guid actor, Guid goalId, Guid? enrollmentId, CancellationToken ct)
    {
        if ((goalId != Guid.Empty) == enrollmentId.HasValue) throw Invalid("Specify exactly one career goal or learning enrollment.");
        return enrollmentId.HasValue ? contexts.GetOrCreateForEnrollmentAsync(actor, enrollmentId.Value, ct) : contexts.GetOrCreateForCareerGoalAsync(actor, goalId, ct);
    }

    private async Task ValidateAttribution(Guid actor, AcceleratorJourneyContext context, Guid competencyId, Guid? nodeId, Guid? moduleId, Guid? lessonId, CancellationToken ct)
    {
        if (!(await contexts.ResolveAsync(actor, context.Id, ct)).Any(x => x.CompetencyId == competencyId))
            throw Invalid("Competency is not a requirement of this journey.");
        if (nodeId.HasValue)
        {
            var node = await phase2.NodeAsync(nodeId.Value, ct) ?? throw new NotFoundException("Roadmap node not found.");
            var roadmap = await phase2.RoadmapAsync(actor, node.RoadmapId, ct);
            if (roadmap is null || roadmap.JourneyContextId != context.Id || node.CompetencyId != competencyId)
                throw new NotFoundException("Roadmap node not found in this journey.");
        }
        if (!moduleId.HasValue && !lessonId.HasValue) return;
        if (!context.LearningPathEnrollmentId.HasValue || !moduleId.HasValue) throw Invalid("Module attribution requires an enrollment; lessons require a module.");
        var enrollment = await learning.EnrollmentAsync(actor, context.LearningPathEnrollmentId.Value, ct) ?? throw new NotFoundException("Learning enrollment not found.");
        var module = enrollment.LearningPath.Modules.SingleOrDefault(x => x.Id == moduleId && !x.IsDeleted && x.Status == AcceleratorLearningCatalogStatus.Published)
            ?? throw new NotFoundException("Published module not found in this journey.");
        if (lessonId.HasValue && !module.Lessons.Any(x => x.Id == lessonId && !x.IsDeleted && x.Status == AcceleratorLearningCatalogStatus.Published))
            throw new NotFoundException("Published lesson not found in this module.");
    }
    private static PracticeEvaluationResponse EvaluationDto(AcceleratorPracticeEvaluation e) => new(e.Id, e.Score, e.MaximumScore, e.Classification, e.Explanation, e.SuggestedImprovement, e.Method, e.Confidence);
    private static AcceleratorPracticeQuestionType ObjectiveType(AcceleratorQuestionType type) => type == AcceleratorQuestionType.MultipleSelect ? AcceleratorPracticeQuestionType.MultipleSelect : AcceleratorPracticeQuestionType.MultipleChoice;
    private static void ValidateJson(string text) { try { using var _ = JsonDocument.Parse(text); } catch (JsonException) { throw Invalid("Answer must be valid JSON."); } }
    private static bool JsonEquals(string left, string right) => JsonSerializer.Serialize(JsonDocument.Parse(left).RootElement) == JsonSerializer.Serialize(JsonDocument.Parse(right).RootElement);
    private Task Audit(Guid actor, AuditAction action, string type, Guid id, CancellationToken ct) => audit.AppendAsync(new AuditEvent(action, type, id.ToString(), Actor: new AuditActor(actor, "Candidate")), ct);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    // Preserve persisted pre-JourneyContext hashes for unchanged legacy request payloads.
    private static string Hash(StartTutorConversationRequest value) => value.EnrollmentId is null && value.ModuleId is null && value.LessonId is null
        ? Hash(new { value.RequestKey, value.GoalId, value.CompetencyId, value.RoadmapNodeId, value.Topic, value.Title })
        : Hash<StartTutorConversationRequest>(value);
    private static string Hash(StartPracticeSessionRequest value) => value.EnrollmentId is null && value.ModuleId is null && value.LessonId is null
        ? Hash(new { value.RequestKey, value.GoalId, value.CompetencyId, value.RoadmapNodeId, value.Difficulty, value.QuestionType })
        : Hash<StartPracticeSessionRequest>(value);
    private static void ValidateText(string? text, int max) { if (string.IsNullOrWhiteSpace(text) || text.Length > max || text.Any(char.IsControl)) throw Invalid("Invalid text input."); }
    private static BadRequestException Invalid(string message) => new(message, "accelerator_invalid_request");
}
