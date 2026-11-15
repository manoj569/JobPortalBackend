using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record InterviewRecommendationResponse(Guid VersionId, string Title, AcceleratorInterviewType Type,
    AcceleratorPracticeDifficulty Difficulty, string Reason);
public sealed record InterviewQuestionResponse(Guid Id, Guid SectionId, Guid? ParentQuestionId, int Sequence,
    AcceleratorInterviewQuestionType Type, string Prompt, bool Answered, bool Evaluated);
public sealed record InterviewSectionResponse(Guid Id, int Sequence, string Title, AcceleratorInterviewType Type,
    bool IsRequired, IReadOnlyList<InterviewQuestionResponse> Questions,
    bool AiQuestionAvailable = false, bool StaticFallbackAvailable = false);
public sealed record InterviewVersionResponse(Guid Id, string Name, string Description, AcceleratorInterviewType Type,
    AcceleratorPracticeDifficulty Difficulty, int EstimatedMinutes, IReadOnlyList<InterviewSectionResponse> Sections);
public sealed record StartInterviewRequest(Guid RequestKey, Guid GoalId, Guid VersionId, bool UseAiQuestions = false);
public sealed record GenerateInterviewQuestionRequest(Guid RequestKey, Guid ExpectedRevision);
public sealed record GeneratedInterviewQuestionPayload(string QuestionText, AcceleratorInterviewQuestionType QuestionType,
    AcceleratorPracticeDifficulty Difficulty, IReadOnlyList<Guid> CompetencyIds, bool FollowUpEligible,
    string GenerationMetadata);
public sealed record InterviewSessionResponse(Guid Id, Guid VersionId, AcceleratorInterviewSessionStatus Status,
    Guid Revision, decimal? OverallScore, DateTime StartedAtUtc, DateTime? CompletedAtUtc,
    IReadOnlyList<InterviewQuestionResponse> Questions);
public sealed record SubmitInterviewAnswerRequest(Guid RequestKey, Guid QuestionId, string Content, Guid ExpectedRevision);
public sealed record InterviewAnswerResponse(Guid Id, Guid QuestionId, DateTime SubmittedAtUtc);
public sealed record InterviewAdminAnswerResponse(Guid Id, Guid SessionId, Guid QuestionId,
    string Content, DateTime SubmittedAtUtc, InterviewEvaluationResponse? Evaluation);
public sealed record InterviewCriterionObservation(Guid CriterionId, decimal Score);
public sealed record ManualInterviewEvaluationRequest(Guid CandidateId, IReadOnlyList<InterviewCriterionObservation> Observations);
public sealed record ManualInterviewDispatchResolutionRequest(string Reason, string ProviderRequestId, string EvidenceReference);
public sealed record InterviewEvaluationResponse(Guid Id, Guid AnswerId, AcceleratorInterviewEvaluationMode Evaluator,
    decimal Score, string Feedback, DateTime EvaluatedAtUtc);
public sealed record CompleteInterviewRequest(Guid ExpectedRevision);
public sealed record InterviewSectionScoreResponse(Guid SectionId, decimal Score);
public sealed record InterviewCompetencyScoreResponse(Guid CompetencyId, decimal Score);
public sealed record InterviewReportResponse(Guid SessionId, decimal OverallScore,
    IReadOnlyList<InterviewSectionScoreResponse> Sections, IReadOnlyList<InterviewCompetencyScoreResponse> Competencies,
    IReadOnlyList<string> Strengths, IReadOnlyList<string> ImprovementAreas,
    IReadOnlyList<string> NextActions, decimal ReadinessChange);
public sealed record ReadinessResponse(Guid GoalId, string Version, decimal Total, decimal SkillCoverage,
    decimal PracticePerformance, decimal ProjectPerformance, decimal InterviewPerformance, string MainBlocker);
public sealed record ReadinessHistoryResponse(Guid Id, DateTime CalculatedAtUtc, decimal Total, string Explanation);
public sealed record PassportEvidenceResponse(AcceleratorEvidenceSource Source, DateTime DateUtc, decimal? Score,
    decimal Confidence, Guid? ProjectId, Guid? InterviewSessionId);
public sealed record PassportSkillResponse(Guid CompetencyId, string SkillName, AcceleratorProficiency Proficiency,
    decimal Confidence, int EvidenceCount, DateTime LastDemonstratedAtUtc, string TrustLevel,
    IReadOnlyList<PassportEvidenceResponse> Evidence);
public sealed record SkillPassportResponse(Guid GoalId, string TargetRole, ReadinessResponse Readiness,
    IReadOnlyList<PassportSkillResponse> Skills, bool IsPublic, string? PublicToken);
public sealed record PublicSkillPassportResponse(string TargetRole, decimal Readiness,
    IReadOnlyList<PassportSkillResponse> Skills, DateTime LastUpdatedAtUtc);
public sealed record PassportShareRequest(Guid GoalId, Guid? ExpectedRevision);
public sealed record PassportShareResponse(bool IsEnabled, string? PublicToken, Guid Revision);

public sealed record CreateInterviewDefinitionRequest(string StableKey, string Name, string Description, Guid TargetRoleId);
public sealed record CreateInterviewDraftRequest(Guid DefinitionId, int Version, AcceleratorInterviewType Type,
    AcceleratorPracticeDifficulty Difficulty, int EstimatedMinutes, string TargetExperienceLevel, string Instructions);
public sealed record EditInterviewDraftRequest(Guid ExpectedRevision, AcceleratorInterviewType Type,
    AcceleratorPracticeDifficulty Difficulty, int EstimatedMinutes, string TargetExperienceLevel, string Instructions);
public sealed record UpsertInterviewCompetencyRequest(Guid ExpectedRevision, Guid CompetencyId, int ExpectedProficiency,
    decimal Weight, bool IsRequired, int QuestionCoverageTarget);
public sealed record UpsertInterviewSectionRequest(Guid ExpectedRevision, Guid? Id, int Sequence, string Title,
    AcceleratorInterviewType Type, int EstimatedMinutes, bool IsRequired, int QuestionCount, int MaxFollowUpsPerQuestion,
    bool AllowStaticFallback = true);
public sealed record UpsertInterviewQuestionRequest(Guid ExpectedRevision, Guid? Id, Guid SectionId, int Sequence,
    Guid? CompetencyId, AcceleratorInterviewQuestionKind Kind, AcceleratorInterviewQuestionType Type,
    AcceleratorInterviewEvaluationMode EvaluationMode, AcceleratorPracticeDifficulty Difficulty, string PublicPrompt,
    string ExpectedConceptsJson, string HiddenEvaluatorGuidance, string DeterministicRulesJson,
    string FollowUpPrompt, bool IsActive);
public sealed record UpsertInterviewCriterionRequest(Guid ExpectedRevision, Guid? Id, int Sequence, Guid? CompetencyId,
    string Name, string Description, decimal Weight, decimal MaximumScore);
public sealed record InterviewDraftResponse(Guid Id, Guid Revision);
public sealed record InterviewAdminCompetencyResponse(Guid Id, Guid CompetencyId, int ExpectedProficiency,
    decimal Weight, bool IsRequired, int QuestionCoverageTarget);
public sealed record InterviewAdminQuestionResponse(Guid Id, Guid SectionId, int Sequence, Guid? CompetencyId,
    AcceleratorInterviewQuestionKind Kind, AcceleratorInterviewQuestionType Type,
    AcceleratorInterviewEvaluationMode EvaluationMode, AcceleratorPracticeDifficulty Difficulty,
    string PublicPrompt, string ExpectedConceptsJson, string HiddenEvaluatorGuidance,
    string DeterministicRulesJson, string FollowUpPrompt, bool IsActive);
public sealed record InterviewAdminSectionResponse(Guid Id, int Sequence, string Title, AcceleratorInterviewType Type,
    int EstimatedMinutes, bool IsRequired, int QuestionCount, int MaxFollowUpsPerQuestion,
    IReadOnlyList<InterviewAdminQuestionResponse> Questions, bool AllowStaticFallback = true);
public sealed record InterviewAdminCriterionResponse(Guid Id, int Sequence, Guid? CompetencyId,
    string Name, string Description, decimal Weight, decimal MaximumScore);
public sealed record InterviewAdminVersionResponse(Guid Id, Guid DefinitionId, int Version, AcceleratorInterviewCatalogStatus Status,
    Guid Revision, IReadOnlyList<InterviewAdminCompetencyResponse> Competencies,
    IReadOnlyList<InterviewAdminSectionResponse> Sections, IReadOnlyList<InterviewAdminCriterionResponse> Criteria);

public interface IAcceleratorPhase5Store
{
    Task<AcceleratorInterviewDefinition?> DefinitionAsync(Guid id, CancellationToken ct);
    Task<AcceleratorInterviewDefinition?> DefinitionByKeyAsync(string stableKey, CancellationToken ct);
    Task<AcceleratorInterviewVersion?> VersionAsync(Guid id, CancellationToken ct);
    Task<AcceleratorInterviewVersion?> VersionByNumberAsync(Guid definitionId, int version, CancellationToken ct);
    Task<bool> VersionUsedAsync(Guid versionId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorInterviewVersion>> PublishedVersionsAsync(Guid roleId, int take, CancellationToken ct);
    Task<AcceleratorInterviewSession?> SessionAsync(Guid userId, Guid id, CancellationToken ct);
    Task<AcceleratorInterviewSession?> SessionByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorInterviewSession>> SessionsAsync(Guid userId, Guid goalId, int skip, int take, CancellationToken ct);
    Task<AcceleratorInterviewAnswer?> AnswerByKeyAsync(Guid userId, Guid sessionId, Guid requestKey, CancellationToken ct);
    Task<AcceleratorInterviewAnswer?> AnswerAsync(Guid userId, Guid sessionId, Guid questionId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorInterviewAnswer>> AnswersAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<AcceleratorInterviewEvaluation?> EvaluationAsync(Guid userId, Guid answerId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorInterviewEvaluation>> EvaluationsAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorReadinessSnapshot>> ReadinessHistoryAsync(Guid userId, Guid goalId, int skip, int take, CancellationToken ct);
    Task<AcceleratorReadinessSnapshot?> ReadinessForSessionAsync(Guid sessionId, CancellationToken ct);
    Task<AcceleratorPassportShare?> ShareAsync(Guid userId, Guid goalId, CancellationToken ct);
    Task<AcceleratorPassportShare?> PublicShareAsync(string token, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorCompetency>> CompetenciesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    void Add(object entity);
}

public interface IInterviewDispatchStore
{
    Task<IReadOnlyList<AcceleratorAiDispatchRecord>> EligibleInterviewDispatchesAsync(DateTime now, int take, CancellationToken ct);
    Task<AcceleratorAiDispatchRecord?> ClaimInterviewDispatchAsync(Guid dispatchId, Guid claimId, DateTime now,
        DateTime expiresAt, CancellationToken ct);
    Task<AcceleratorAiDispatchRecord?> ClaimManualInterviewDispatchAsync(Guid dispatchId, Guid claimId, DateTime now,
        DateTime expiresAt, CancellationToken ct);
    Task<bool> UpdateClaimedInterviewDispatchAsync(AcceleratorAiDispatchRecord record, Guid claimId, CancellationToken ct);
    Task<bool> PersistClaimedInterviewResultAsync(AcceleratorAiDispatchRecord record, Guid claimId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorAiDispatchRecord>> UnresolvedInterviewDispatchesAsync(int skip, int take, CancellationToken ct);
}
