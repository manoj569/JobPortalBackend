using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record StartTutorConversationRequest(Guid RequestKey, Guid GoalId, Guid CompetencyId, Guid? RoadmapNodeId, string Topic, string Title, Guid? EnrollmentId = null, Guid? ModuleId = null, Guid? LessonId = null);
public sealed record SendTutorMessageRequest(Guid RequestKey, AcceleratorTutorMode Mode, string Content);
public sealed record StartEnrollmentTutorRequest(Guid RequestKey, Guid CompetencyId, Guid? RoadmapNodeId, string Topic, string Title, Guid? ModuleId = null, Guid? LessonId = null);
public sealed record StartEnrollmentPracticeRequest(Guid RequestKey, Guid CompetencyId, Guid? RoadmapNodeId, AcceleratorPracticeDifficulty Difficulty, AcceleratorPracticeQuestionType QuestionType, Guid? ModuleId = null, Guid? LessonId = null);
public sealed record TutorConversationResponse(Guid Id, Guid? GoalId, Guid CompetencyId, Guid? RoadmapNodeId, string Topic, string Title, AcceleratorTutorConversationStatus Status, DateTime LastMessageAtUtc, Guid Revision, Guid? JourneyContextId = null, Guid? ModuleId = null, Guid? LessonId = null);
public sealed record TutorMessageResponse(Guid Id, AcceleratorTutorMessageRole Role, AcceleratorTutorMessageStatus Status, string Content, string MessageType, DateTime CreatedAtUtc);
public sealed record TutorContext(string Version, string Json, int TokenEstimate);
public sealed record TutorProviderRequest(Guid DispatchId, Guid ReservationId, string IdempotencyKey, AcceleratorTutorMode Mode, TutorContext Context, string CandidateContent, int MaxOutputTokens);
public sealed record TutorProviderResult(string Content, string ProviderRequestId, string Model, int InputTokens, int OutputTokens, decimal EstimatedCostInr);
public sealed record TutorDispatchVerification(AcceleratorDispatchVerificationStatus Status, string? ProviderRequestId, string? Content, string? Model, int? InputTokens, int? OutputTokens, decimal? EstimatedCostInr, string? ErrorCode);
public sealed record PracticeResultResponse(Guid SessionId, AcceleratorPracticeStatus Status, int QuestionCount, int AnsweredCount, int CorrectCount, decimal ScorePercent, IReadOnlyList<PracticeEvaluationResponse> Evaluations);
public sealed record PracticeRecommendationResponse(Guid CompetencyId, AcceleratorPracticeDifficulty RecommendedDifficulty, string Reason, int AttemptCount, decimal AccuracyPercent);
public sealed record ReconciliationBatchResult(int Examined, int Resolved, int Pending, int ManualReview);
public sealed record DispatchAdminResponse(Guid Id, Guid UserId, Guid ReservationId, AcceleratorAiDispatchStatus Status, int Attempts, bool RequiresManualReview, string? ManualReviewReason, DateTime? NextReconciliationAtUtc);
public sealed record StartPracticeSessionRequest(Guid RequestKey, Guid GoalId, Guid CompetencyId, Guid? RoadmapNodeId, AcceleratorPracticeDifficulty Difficulty, AcceleratorPracticeQuestionType QuestionType, Guid? EnrollmentId = null, Guid? ModuleId = null, Guid? LessonId = null);
public sealed record PracticeQuestionResponse(Guid Id, int Sequence, string Prompt, string PublicOptionsJson, AcceleratorPracticeQuestionType Type, AcceleratorPracticeDifficulty Difficulty);
public sealed record PracticeSessionResponse(Guid Id, AcceleratorPracticeStatus Status, AcceleratorPracticeDifficulty Difficulty, AcceleratorPracticeQuestionType QuestionType, IReadOnlyList<PracticeQuestionResponse> Questions, Guid? JourneyContextId = null, Guid? GoalId = null, Guid? ModuleId = null, Guid? LessonId = null);
public sealed record SubmitPracticeAnswerRequest(Guid RequestKey, Guid QuestionId, string AnswerJson);
public sealed record PracticeEvaluationResponse(Guid Id, decimal Score, decimal MaximumScore, string Classification, string Explanation, string SuggestedImprovement, AcceleratorPracticeEvaluationMethod Method, decimal Confidence);

public interface IAiTutorProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<TutorProviderResult> SendAsync(TutorProviderRequest request, CancellationToken ct);
}
public interface IAiTutorDispatchVerifier
{
    string ProviderName { get; }
    Task<TutorDispatchVerification> VerifyAsync(string providerRequestId, CancellationToken ct);
}
public interface ITutorContextBuilder
{
    Task<TutorContext> BuildAsync(Guid userId, AcceleratorTutorConversation conversation, string candidateContent, CancellationToken ct);
}
public interface IAcceleratorPhase3Store
{
    Task<AcceleratorTutorConversation?> ConversationAsync(Guid userId, Guid id, CancellationToken ct);
    Task<AcceleratorTutorConversation?> ConversationByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorTutorConversation>> ConversationsAsync(Guid userId, int skip, int take, CancellationToken ct, Guid? contextId = null);
    Task<IReadOnlyList<AcceleratorTutorMessage>> MessagesAsync(Guid userId, Guid conversationId, int take, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorPracticeSession>> PracticeSessionsAsync(Guid userId, int skip, int take, CancellationToken ct, Guid? contextId = null);
    Task<AcceleratorAiDispatchRecord?> DispatchAsync(Guid id, CancellationToken ct);
    Task<bool> UpdateClaimedDispatchAsync(AcceleratorAiDispatchRecord record, Guid claimId, CancellationToken ct);
    Task<AcceleratorPracticeSession?> PracticeSessionAsync(Guid userId, Guid id, CancellationToken ct);
    Task<AcceleratorPracticeSession?> PracticeSessionByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct);
    Task<AcceleratorPracticeQuestion?> PracticeQuestionAsync(Guid userId, Guid sessionId, Guid questionId, CancellationToken ct);
    Task<AcceleratorPracticeAttempt?> PracticeAttemptByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorPracticeEvaluation>> PracticeEvaluationsAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<AcceleratorAiDispatchRecord?> DispatchByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorAiDispatchRecord>> FailedDispatchesAsync(int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorAiDispatchRecord>> EligibleReconciliationDispatchesAsync(DateTime now, int take, CancellationToken ct);
    Task<AcceleratorAiDispatchRecord?> ClaimReconciliationAsync(Guid dispatchId, Guid claimId, DateTime now, DateTime expiresAt, CancellationToken ct);
    void Add(object entity);
}
public sealed class TutorProviderUnavailableException() : JobPortal.Application.Common.Exceptions.AppException("AI Tutor provider is unavailable.", 503, "accelerator_tutor_provider_unavailable");
public sealed class UnavailableAiTutorProvider : IAiTutorProvider, IAiTutorDispatchVerifier
{
    public string Name => "Unavailable";
    public string ProviderName => Name;
    public bool IsAvailable => false;
    public Task<TutorProviderResult> SendAsync(TutorProviderRequest request, CancellationToken ct) => throw new TutorProviderUnavailableException();
    public Task<TutorDispatchVerification> VerifyAsync(string providerRequestId, CancellationToken ct) => Task.FromResult(new TutorDispatchVerification(AcceleratorDispatchVerificationStatus.Unsupported, null, null, null, null, null, null, "provider_unavailable"));
}
