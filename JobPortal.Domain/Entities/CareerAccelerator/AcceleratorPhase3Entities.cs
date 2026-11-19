using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum AcceleratorTutorConversationStatus { Active = 1, Archived, Erased }
public enum AcceleratorTutorMessageRole { System = 1, User, Assistant, Tool }
public enum AcceleratorTutorMessageStatus { Pending = 1, Delivered, Failed, Uncertain, Rejected }
public enum AcceleratorTutorMode { Explain = 1, Simplify, DeepDive, RealWorldExample, Socratic, Revision, InterviewPreparation, CompareConcepts }
public enum AcceleratorPracticeStatus { Draft = 1, Ready, Answered, Evaluated, Rejected, Failed }
public enum AcceleratorPracticeDifficulty { Beginner = 1, Intermediate, Advanced }
public enum AcceleratorPracticeQuestionType { MultipleChoice = 1, MultipleSelect, ShortAnswer, Scenario, SqlReasoning, CSharpExplanation, Debugging, ProductCase, ProductMetrics, SystemDesign }
public enum AcceleratorPracticeEvaluationMethod { Deterministic = 1, ProviderAssisted, Unavailable }
public enum AcceleratorAiDispatchStatus { Reserved = 1, Dispatched, Succeeded, Failed, Uncertain, Reconciled }
public enum AcceleratorDispatchVerificationStatus { VerifiedSuccess = 1, VerifiedFailure, Unknown, Unsupported }

public sealed class AcceleratorTutorConversation : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid? GoalId { get; set; }
    public Guid? JourneyContextId { get; set; }
    public Guid? ModuleId { get; set; }
    public Guid? LessonId { get; set; }
    public Guid? RoadmapNodeId { get; set; }
    public Guid CompetencyId { get; set; }
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public AcceleratorTutorConversationStatus Status { get; set; } = AcceleratorTutorConversationStatus.Active;
    public DateTime LastMessageAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<AcceleratorTutorMessage> Messages { get; set; } = [];
}
public sealed class AcceleratorTutorMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public AcceleratorTutorConversation Conversation { get; set; } = null!;
    public AcceleratorTutorMessageRole Role { get; set; }
    public AcceleratorTutorMessageStatus Status { get; set; }
    public string Content { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal? EstimatedCostInr { get; set; }
    public string? FailureCode { get; set; }
    public Guid? DispatchId { get; set; }
}
public sealed class AcceleratorTutorContextSnapshot : BaseEntity
{
    public Guid ConversationId { get; set; }
    public string ContextVersion { get; set; } = string.Empty;
    public string ContentJson { get; set; } = "{}";
    public int TokenEstimate { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
public sealed class AcceleratorTutorMemorySummary : BaseEntity
{
    public Guid ConversationId { get; set; }
    public int Version { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool IsInvalidated { get; set; }
    public string SourceRange { get; set; } = string.Empty;
}
public sealed class AcceleratorPracticeSession : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid? GoalId { get; set; }
    public Guid? JourneyContextId { get; set; }
    public Guid? ModuleId { get; set; }
    public Guid? LessonId { get; set; }
    public Guid CompetencyId { get; set; }
    public Guid? RoadmapNodeId { get; set; }
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public AcceleratorPracticeDifficulty Difficulty { get; set; }
    public AcceleratorPracticeQuestionType QuestionType { get; set; }
    public AcceleratorPracticeStatus Status { get; set; }
    public string GenerationStrategy { get; set; } = "deterministic-v1";
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<AcceleratorPracticeQuestion> Questions { get; set; } = [];
}
public sealed class AcceleratorPracticeQuestion : BaseEntity
{
    public Guid SessionId { get; set; }
    public AcceleratorPracticeSession Session { get; set; } = null!;
    public int Sequence { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string PublicOptionsJson { get; set; } = "[]";
    public string AnswerKeyJson { get; set; } = "{}";
    public string RubricJson { get; set; } = "{}";
    public string GenerationVersion { get; set; } = string.Empty;
    public bool IsQualityApproved { get; set; }
}
public sealed class AcceleratorPracticeAttempt : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid RequestKey { get; set; }
    public string AnswerJson { get; set; } = "{}";
    public DateTime SubmittedAtUtc { get; set; }
}
public sealed class AcceleratorPracticeEvaluation : BaseEntity
{
    public Guid AttemptId { get; set; }
    public decimal Score { get; set; }
    public decimal MaximumScore { get; set; }
    public string Classification { get; set; } = string.Empty;
    public string StrengthsJson { get; set; } = "[]";
    public string WeaknessesJson { get; set; } = "[]";
    public string Explanation { get; set; } = string.Empty;
    public string SuggestedImprovement { get; set; } = string.Empty;
    public AcceleratorPracticeEvaluationMethod Method { get; set; }
    public decimal Confidence { get; set; }
    public string RubricVersion { get; set; } = string.Empty;
}
public sealed class AcceleratorAiDispatchRecord : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? CandidateProjectId { get; set; }
    public Guid? ProjectSubmissionId { get; set; }
    public Guid? InterviewSessionId { get; set; }
    public Guid? InterviewAnswerId { get; set; }
    public Guid? InterviewSectionId { get; set; }
    public int? GenerationSlot { get; set; }
    public Guid RequestKey { get; set; }
    public Guid ReservationId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public AcceleratorAiDispatchStatus Status { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string? ProviderRequestId { get; set; }
    public string? ResultJson { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? ActualCostInr { get; set; }
    public string? FailureCode { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime? NextReconciliationAtUtc { get; set; }
    public int ReconciliationAttemptCount { get; set; }
    public DateTime? LastReconciliationAtUtc { get; set; }
    public string? LastReconciliationError { get; set; }
    public bool RequiresManualReview { get; set; }
    public string? ManualReviewReason { get; set; }
    public Guid? ReconciliationClaimId { get; set; }
    public DateTime? ReconciliationClaimExpiresAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
