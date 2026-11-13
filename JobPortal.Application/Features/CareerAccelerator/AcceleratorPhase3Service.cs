using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

// The only tutor dispatch boundary. The default provider is unavailable and never fabricates an assistant reply.
public sealed class AcceleratorPhase3Service(IAcceleratorStore writes, IAcceleratorPhase3Store store,
    IAcceleratorPhase2Store phase2, AcceleratorService access, ITutorContextBuilder contextBuilder, IEnumerable<IAiTutorProvider> providers,
    AcceleratorQuotaService quota, IAuditWriter audit, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<TutorConversationResponse> StartConversationAsync(Guid actor, StartTutorConversationRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); ValidateText(request.Topic, 500); ValidateText(request.Title, 500);
        if (request.RequestKey == Guid.Empty || request.GoalId == Guid.Empty || request.CompetencyId == Guid.Empty) throw Invalid("A request key, goal and competency are required.");
        var existing = await store.ConversationByKeyAsync(actor, request.RequestKey, ct);
        var hash = Hash(request);
        if (existing is not null) { if (existing.RequestHash != hash) throw new ConflictException("Idempotency key was used for a different conversation."); return ConversationDto(existing); }
        var goal = await writes.GoalAsync(actor, request.GoalId, ct) ?? throw new NotFoundException("Career goal not found.");
        var conversation = new AcceleratorTutorConversation { UserId = actor, GoalId = goal.Id, CompetencyId = request.CompetencyId, RoadmapNodeId = request.RoadmapNodeId, RequestKey = request.RequestKey, RequestHash = hash, Topic = request.Topic.Trim(), Title = request.Title.Trim(), LastMessageAtUtc = Now };
        store.Add(conversation); await Audit(actor, AuditAction.Create, "AcceleratorTutorConversation", conversation.Id, ct); return ConversationDto(conversation);
    }, ct);

    public async Task<IReadOnlyList<TutorConversationResponse>> ConversationsAsync(Guid actor, int page, int pageSize, CancellationToken ct)
    { await access.Candidate(actor, ct); AcceleratorService.Page(page, pageSize); return (await store.ConversationsAsync(actor, (page - 1) * pageSize, pageSize, ct)).Select(ConversationDto).ToArray(); }
    public async Task<IReadOnlyList<TutorMessageResponse>> MessagesAsync(Guid actor, Guid conversationId, CancellationToken ct)
    { await access.Candidate(actor, ct); if (await store.ConversationAsync(actor, conversationId, ct) is null) throw new NotFoundException("Tutor conversation not found."); return (await store.MessagesAsync(actor, conversationId, 100, ct)).Select(MessageDto).ToArray(); }
    public Task<TutorConversationResponse> ArchiveConversationAsync(Guid actor, Guid id, Guid expectedRevision, CancellationToken ct) => writes.WriteAsync(async () =>
    { await access.Candidate(actor, ct); var c = await RequiredConversation(actor, id, ct); if (c.Revision != expectedRevision) throw new ConflictException("Conversation changed; reload it."); c.Status = AcceleratorTutorConversationStatus.Archived; c.Revision = Guid.NewGuid(); await Audit(actor, AuditAction.Update, "AcceleratorTutorConversation", c.Id, ct); return ConversationDto(c); }, ct);

    public async Task<TutorMessageResponse> SendMessageAsync(Guid actor, Guid conversationId, SendTutorMessageRequest request, CancellationToken ct)
    {
        await access.Candidate(actor, ct); ValidateText(request.Content, 8000); if (request.RequestKey == Guid.Empty || !Enum.IsDefined(request.Mode)) throw Invalid("A request key and mode are required.");
        var c = await RequiredConversation(actor, conversationId, ct); if (c.Status != AcceleratorTutorConversationStatus.Active) throw new ConflictException("Conversation is archived.");
        var provider = providers.FirstOrDefault(x => x.IsAvailable) ?? throw new TutorProviderUnavailableException();
        var context = await contextBuilder.BuildAsync(actor, c, request.Content, ct);
        var requestHash = Hash(request); var prior = await store.DispatchByKeyAsync(actor, request.RequestKey, ct);
        if (prior is not null)
        {
            if (prior.RequestHash != requestHash) throw new ConflictException("Idempotency key was used for a different tutor request.");
            if (prior.Status is AcceleratorAiDispatchStatus.Uncertain or AcceleratorAiDispatchStatus.Dispatched) throw new ConflictException("Tutor request outcome is pending reconciliation.", "accelerator_dispatch_pending");
            var history = await store.MessagesAsync(actor, c.Id, 100, ct); var delivered = history.LastOrDefault(x => x.DispatchId == prior.Id && x.Role == AcceleratorTutorMessageRole.Assistant);
            if (delivered is not null) return MessageDto(delivered);
            throw new ConflictException("Tutor request previously failed.", "accelerator_dispatch_failed");
        }
        var reservation = await quota.ReserveAsync(actor, new ReserveAcceleratorUsageRequest(request.RequestKey, AcceleratorFeature.Tutor, requestHash, context.TokenEstimate, Encoding.UTF8.GetByteCount(request.Content)), ct);
        var dispatch = await writes.WriteAsync(async () =>
        {
            var row = new AcceleratorAiDispatchRecord { UserId = actor, RequestKey = request.RequestKey, ReservationId = reservation.Id, Operation = "TutorMessage", Provider = provider.Name, Model = "configured", Status = AcceleratorAiDispatchStatus.Reserved, RequestHash = requestHash };
            store.Add(row); return row;
        }, ct);
        await quota.MarkDispatchedAsync(actor, reservation.Id, ct);
        dispatch.Status = AcceleratorAiDispatchStatus.Dispatched; dispatch.Revision = Guid.NewGuid();
        try
        {
            var result = await provider.SendAsync(new TutorProviderRequest(dispatch.Id, reservation.Id, request.RequestKey.ToString("N"), request.Mode, context, request.Content, 1024), ct);
            var response = await writes.WriteAsync(async () =>
            {
                var message = new AcceleratorTutorMessage { ConversationId = c.Id, Role = AcceleratorTutorMessageRole.Assistant, Status = AcceleratorTutorMessageStatus.Delivered, Content = result.Content, MessageType = request.Mode.ToString(), Provider = provider.Name, Model = result.Model, InputTokens = result.InputTokens, OutputTokens = result.OutputTokens, EstimatedCostInr = result.EstimatedCostInr, DispatchId = dispatch.Id };
                store.Add(message); c.LastMessageAtUtc = Now; c.Revision = Guid.NewGuid(); dispatch.Status = AcceleratorAiDispatchStatus.Succeeded; dispatch.ProviderRequestId = result.ProviderRequestId; dispatch.FinishedAtUtc = Now; dispatch.Revision = Guid.NewGuid(); return MessageDto(message);
            }, ct);
            await quota.CompleteAsync(actor, reservation.Id, result.InputTokens, result.OutputTokens, result.EstimatedCostInr, ct); await Audit(actor, AuditAction.Confirm, "AcceleratorAiDispatchRecord", dispatch.Id, ct); return response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { dispatch.Status = AcceleratorAiDispatchStatus.Uncertain; dispatch.FailureCode = "provider_timeout"; dispatch.Revision = Guid.NewGuid(); throw; }
        catch
        { dispatch.Status = AcceleratorAiDispatchStatus.Uncertain; dispatch.FailureCode = "provider_outcome_unknown"; dispatch.Revision = Guid.NewGuid(); throw; }
    }

    public Task<int> EraseTutorDataAsync(Guid actor, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); var conversations = await store.ConversationsAsync(actor, 0, 100, ct); foreach (var c in conversations) { c.Status = AcceleratorTutorConversationStatus.Erased; c.Topic = string.Empty; c.Title = "Erased"; c.Revision = Guid.NewGuid(); } await Audit(actor, AuditAction.Delete, "AcceleratorTutorConversation", actor, ct); return conversations.Count;
    }, ct);

    public Task<PracticeSessionResponse> StartPracticeAsync(Guid actor, StartPracticeSessionRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); if (request.RequestKey == Guid.Empty || request.GoalId == Guid.Empty || request.CompetencyId == Guid.Empty || !Enum.IsDefined(request.Difficulty) || !Enum.IsDefined(request.QuestionType)) throw Invalid("Invalid practice request.");
        var previous = await store.PracticeSessionByKeyAsync(actor, request.RequestKey, ct); var hash = Hash(request);
        if (previous is not null) { if (previous.RequestHash != hash) throw new ConflictException("Idempotency key was used for different practice."); return PracticeDto(previous); }
        var goal = await writes.GoalAsync(actor, request.GoalId, ct) ?? throw new NotFoundException("Career goal not found.");
        var questions = await phase2.QuestionsAsync(goal.TaxonomyId, [request.CompetencyId], ct);
        var selected = questions.Where(x => x.IsActive && ObjectiveType(x.Type) == request.QuestionType).OrderBy(x => x.Difficulty).ThenBy(x => x.Id).Take(5).ToArray();
        if (selected.Length == 0) throw new ConflictException("No validated deterministic practice questions are available.", "accelerator_practice_unavailable");
        var session = new AcceleratorPracticeSession { UserId = actor, GoalId = goal.Id, CompetencyId = request.CompetencyId, RoadmapNodeId = request.RoadmapNodeId, RequestKey = request.RequestKey, RequestHash = hash, Difficulty = request.Difficulty, QuestionType = request.QuestionType, Status = AcceleratorPracticeStatus.Ready };
        foreach (var (question, sequence) in selected.Select((q, i) => (q, i + 1))) session.Questions.Add(new AcceleratorPracticeQuestion { Session = session, SessionId = session.Id, Sequence = sequence, Prompt = question.Prompt, PublicOptionsJson = question.OptionsJson, AnswerKeyJson = question.AnswerKeyJson, RubricJson = question.RubricJson, GenerationVersion = question.Version, IsQualityApproved = true });
        store.Add(session); await Audit(actor, AuditAction.Create, "AcceleratorPracticeSession", session.Id, ct); return PracticeDto(session);
    }, ct);

    public Task<PracticeEvaluationResponse> SubmitPracticeAnswerAsync(Guid actor, Guid sessionId, SubmitPracticeAnswerRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct); ValidateJson(request.AnswerJson); if (request.RequestKey == Guid.Empty) throw Invalid("A request key is required.");
        var prior = await store.PracticeAttemptByKeyAsync(actor, request.RequestKey, ct); if (prior is not null) return EvaluationDto((await store.PracticeEvaluationsAsync(actor, prior.SessionId, ct)).Single(x => x.AttemptId == prior.Id));
        var session = await store.PracticeSessionAsync(actor, sessionId, ct) ?? throw new NotFoundException("Practice session not found."); var question = await store.PracticeQuestionAsync(actor, sessionId, request.QuestionId, ct) ?? throw new NotFoundException("Practice question not found.");
        var attempt = new AcceleratorPracticeAttempt { UserId = actor, SessionId = session.Id, QuestionId = question.Id, RequestKey = request.RequestKey, AnswerJson = request.AnswerJson, SubmittedAtUtc = Now };
        var correct = JsonEquals(question.AnswerKeyJson, request.AnswerJson); var evaluation = new AcceleratorPracticeEvaluation { AttemptId = attempt.Id, Score = correct ? 1 : 0, MaximumScore = 1, Classification = correct ? "Correct" : "Incorrect", StrengthsJson = correct ? "[\"Objective answer matched\"]" : "[]", WeaknessesJson = correct ? "[]" : "[\"Review the concept and explanation\"]", Explanation = correct ? "Your answer matched the validated key." : "Your answer did not match the validated key.", SuggestedImprovement = correct ? "Try a higher-difficulty variation." : "Review prerequisites before another attempt.", Method = AcceleratorPracticeEvaluationMethod.Deterministic, Confidence = 1, RubricVersion = question.GenerationVersion };
        store.Add(attempt); store.Add(evaluation); session.Status = AcceleratorPracticeStatus.Evaluated; session.Revision = Guid.NewGuid();
        if (correct) store.Add(new AcceleratorSkillEvidence { UserId = actor, GoalId = session.GoalId, CompetencyId = session.CompetencyId, Source = AcceleratorEvidenceSource.AssignmentDemonstrated, Proficiency = AcceleratorProficiency.Developing, Confidence = .25m, ScoringVersion = "practice-objective-v1", EvaluatedAtUtc = Now });
        await Audit(actor, AuditAction.Confirm, "AcceleratorPracticeAttempt", attempt.Id, ct); return EvaluationDto(evaluation);
    }, ct);

    private async Task<AcceleratorTutorConversation> RequiredConversation(Guid actor, Guid id, CancellationToken ct) => await store.ConversationAsync(actor, id, ct) ?? throw new NotFoundException("Tutor conversation not found.");
    private static TutorConversationResponse ConversationDto(AcceleratorTutorConversation c) => new(c.Id, c.GoalId, c.CompetencyId, c.RoadmapNodeId, c.Topic, c.Title, c.Status, c.LastMessageAtUtc, c.Revision);
    private static TutorMessageResponse MessageDto(AcceleratorTutorMessage m) => new(m.Id, m.Role, m.Status, m.Content, m.MessageType, m.CreatedAtUtc);
    private static PracticeSessionResponse PracticeDto(AcceleratorPracticeSession s) => new(s.Id, s.Status, s.Difficulty, s.QuestionType, s.Questions.OrderBy(x => x.Sequence).Select(x => new PracticeQuestionResponse(x.Id, x.Sequence, x.Prompt, x.PublicOptionsJson, s.QuestionType, s.Difficulty)).ToArray());
    private static PracticeEvaluationResponse EvaluationDto(AcceleratorPracticeEvaluation e) => new(e.Id, e.Score, e.MaximumScore, e.Classification, e.Explanation, e.SuggestedImprovement, e.Method, e.Confidence);
    private static AcceleratorPracticeQuestionType ObjectiveType(AcceleratorQuestionType type) => type == AcceleratorQuestionType.MultipleSelect ? AcceleratorPracticeQuestionType.MultipleSelect : AcceleratorPracticeQuestionType.MultipleChoice;
    private static void ValidateJson(string text) { try { using var _ = JsonDocument.Parse(text); } catch (JsonException) { throw Invalid("Answer must be valid JSON."); } }
    private static bool JsonEquals(string left, string right) => JsonSerializer.Serialize(JsonDocument.Parse(left).RootElement) == JsonSerializer.Serialize(JsonDocument.Parse(right).RootElement);
    private Task Audit(Guid actor, AuditAction action, string type, Guid id, CancellationToken ct) => audit.AppendAsync(new AuditEvent(action, type, id.ToString(), Actor: new AuditActor(actor, "Candidate")), ct);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static void ValidateText(string? text, int max) { if (string.IsNullOrWhiteSpace(text) || text.Length > max || text.Any(char.IsControl)) throw Invalid("Invalid text input."); }
    private static BadRequestException Invalid(string message) => new(message, "accelerator_invalid_request");
}
