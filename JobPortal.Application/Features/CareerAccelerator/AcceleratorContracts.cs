using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record PublishTaxonomyRequest(string Version, CompetencyDefinition[] Skills, CareerRoleDefinition[] Roles);
public sealed record CompetencyDefinition(Guid SkillId, string Description, string Category, int Difficulty,
    Guid? ParentSkillId, Guid[] PrerequisiteSkillIds, string[] AssessmentMethods, string[] LearningResources);
public sealed record RoleRequirementDefinition(Guid SkillId, int ExpectedProficiency);
public sealed record CareerRoleDefinition(string Code, string Name, string Description, RoleRequirementDefinition[] Requirements);
public sealed record TaxonomyResponse(Guid Id, string Version, DateTime PublishedAtUtc,
    IReadOnlyList<CompetencyResponse> Skills, IReadOnlyList<CareerRoleResponse> Roles);
public sealed record CompetencyResponse(Guid Id, Guid SkillId, string Name, string Description, string Category,
    int Difficulty, Guid? ParentSkillId, Guid[] PrerequisiteSkillIds, string[] AssessmentMethods, string[] LearningResources);
public sealed record CareerRoleResponse(Guid Id, string Code, string Name, string Description, RoleRequirementDefinition[] Requirements);
public sealed record SelfReportedSkill(Guid SkillId, int Proficiency);
public sealed record CanonicalAcceleratorSkill(Guid Id, string Name);
public sealed record CreateCanonicalAcceleratorSkillRequest(string Name);
public sealed record SaveAcceleratorProfileRequest(Guid? ExpectedRevision, decimal ExperienceYears, string EducationSummary,
    int WeeklyStudyMinutes, bool LinkCurrentResume, SelfReportedSkill[] Skills);
public sealed record AcceleratorProfileResponse(Guid Id, Guid Revision, decimal ExperienceYears, string EducationSummary,
    int WeeklyStudyMinutes, DateTime? LinkedResumeUploadedAtUtc, IReadOnlyList<SelfReportedSkill> Skills,
    string EvidenceMethod = "SelfReported");
public sealed record CreateCareerGoalRequest(Guid RequestKey, Guid RoleId, int WeeklyStudyMinutes, DateTime TargetAtUtc);
public sealed record ChangeCareerGoalRequest(Guid ExpectedRevision, AcceleratorGoalStatus Status);
public sealed record CareerGoalResponse(Guid Id, Guid Revision, Guid RoleId, string RoleName, string TaxonomyVersion,
    AcceleratorGoalStatus Status, int WeeklyStudyMinutes, DateTime TargetAtUtc);
public sealed record AcceleratorPlanResponse(string Code, string Name, string Version, decimal BaseAmountInr,
    decimal TaxRate, decimal TaxAmountInr, decimal TotalAmountInr, string BillingPeriod,
    IReadOnlyDictionary<AcceleratorFeature, int> Allowances, bool CheckoutAvailable, bool AIAvailable);
public sealed record AcceleratorEntitlementResponse(string Status, DateTime? StartsAtUtc, DateTime? EndsAtUtc,
    IReadOnlyList<AcceleratorUsageSummary> Usage);
public sealed record AcceleratorUsageSummary(AcceleratorFeature Feature, int Allocated, int Consumed, int Reserved, int Remaining);
public sealed record ReserveAcceleratorUsageRequest(Guid RequestKey, AcceleratorFeature Feature, string RequestHash,
    int InputTokens, int RequestBytes);
public sealed record AcceleratorReservationResponse(Guid Id, Guid RequestKey, AcceleratorFeature Feature,
    AcceleratorUsageState State, DateTime LeaseExpiresAtUtc, decimal ReservedCostInr, decimal? ActualCostInr, bool PolicyViolation);
public sealed record AcceleratorUsageHistoryItem(Guid Id, AcceleratorFeature Feature, AcceleratorUsageState State,
    DateTime ReservedAtUtc, DateTime? FinishedAtUtc);
public sealed record AcceleratorBudgetSnapshot(int FeatureUsed, int RecentRequests, int UserInFlight, int GlobalInFlight,
    decimal CycleCost, decimal UserDailyCost, decimal GlobalDailyCost, decimal GlobalMonthlyCost);

public sealed record CreateAssessmentAttemptRequest(Guid RequestKey, Guid GoalId, Guid TemplateId);
public sealed record SubmitAssessmentAnswerRequest(Guid QuestionId, string AnswerJson);
public sealed record FinalizeAssessmentRequest(Guid ExpectedRevision);
public sealed record AssessmentQuestionResponse(Guid Id, int Sequence, string Prompt, string OptionsJson, AcceleratorQuestionType Type, int Difficulty,
    bool IsAnswered = false, string? SubmittedAnswerJson = null, bool AnswerIsImmutable = false);
public sealed record AssessmentAttemptResponse(Guid Id, Guid? GoalId, AcceleratorAssessmentStatus Status, Guid Revision, DateTime ExpiresAtUtc, decimal? Score, IReadOnlyList<AssessmentQuestionResponse> Questions,
    DateTime? StartedAtUtc = null, Guid? JourneyContextId = null, bool CanResume = false);
public sealed record StartResolvedAssessmentRequest(Guid RequestKey);
public sealed record SkillGapResponse(Guid CompetencyId, string SkillName, AcceleratorProficiency Current, int Required, int Gap, decimal Confidence);
public sealed record RoadmapNodeResponse(Guid Id, Guid CompetencyId, string StableKey, int Sequence, AcceleratorNodeStatus Status, int EstimatedMinutes, int ProgressPercent);
public sealed record RoadmapEdgeResponse(Guid FromNodeId, Guid ToNodeId);
public sealed record LearningActivityResponse(Guid Id, Guid RoadmapNodeId, string Title, int EstimatedMinutes, DateTime ScheduledAtUtc, AcceleratorNodeStatus Status, Guid Revision);
public sealed record RoadmapResponse(Guid Id, Guid? GoalId, int Version, AcceleratorRoadmapStatus Status, DateTime TargetAtUtc, int EstimatedMinutes, string Explanation, bool TimelineFeasible, IReadOnlyList<RoadmapNodeResponse> Nodes, IReadOnlyList<RoadmapEdgeResponse> Edges, IReadOnlyList<LearningActivityResponse> Schedule);
public sealed record GenerateRoadmapRequest(Guid RequestKey, Guid GoalId);
public sealed record RecordLearningProgressRequest(Guid RequestKey, AcceleratorProgressKind Kind, string EvidenceJson, Guid ExpectedRevision);
public sealed record RecoverMissedScheduleRequest(Guid RequestKey);
public sealed record RecommendationResponse(Guid Id, string Type, string Reason, int Priority, string EvidenceJson, DateTime GeneratedAtUtc);
public sealed record UpsertAssessmentTemplateRequest(Guid RoleId, string Version, int TimeLimitMinutes, int MaximumQuestions, bool IsActive);
public sealed record UpsertAssessmentQuestionRequest(Guid TaxonomyId, Guid CompetencyId, string Version, AcceleratorQuestionType Type, int Difficulty, decimal Weight, string Prompt, string OptionsJson, string AnswerKeyJson, string RubricJson, string Explanation, bool IsActive);

public interface IAcceleratorStore
{
    Task<T> WriteAsync<T>(Func<Task<T>> action, CancellationToken ct);
    Task<User?> UserAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Skill>> SkillsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<Skill?> SkillByNameAsync(string normalizedName, CancellationToken ct);
    Task<IReadOnlyList<Skill>> SearchSkillsAsync(string normalizedQuery, int skip, int take, CancellationToken ct);
    Task<AcceleratorTaxonomy?> TaxonomyAsync(Guid? id, string? version, CancellationToken ct);
    Task<AcceleratorCareerRole?> RoleAsync(Guid id, CancellationToken ct);
    Task<AcceleratorProfile?> ProfileAsync(Guid userId, CancellationToken ct);
    Task<AcceleratorCareerGoal?> GoalByKeyAsync(Guid userId, Guid key, CancellationToken ct);
    Task<AcceleratorCareerGoal?> GoalAsync(Guid userId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorCareerGoal>> GoalsAsync(Guid userId, int skip, int take, CancellationToken ct);
    Task<Payment?> PaymentAsync(Guid id, CancellationToken ct);
    Task<AcceleratorBillingCycle?> CycleForPaymentAsync(Guid paymentId, CancellationToken ct);
    Task<AcceleratorBillingCycle?> ActiveCycleAsync(Guid userId, DateTime now, CancellationToken ct);
    Task<AcceleratorBillingCycle?> LatestCycleAsync(Guid userId, CancellationToken ct);
    Task<bool> CycleOverlapsAsync(Guid userId, DateTime start, DateTime end, CancellationToken ct);
    Task<AcceleratorUsageReservation?> ReservationByKeyAsync(Guid userId, Guid key, CancellationToken ct);
    Task<AcceleratorUsageReservation?> ReservationAsync(Guid userId, Guid id, CancellationToken ct);
    Task<AcceleratorBudgetSnapshot> BudgetAsync(Guid userId, Guid cycleId, AcceleratorFeature feature, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorUsageReservation>> CycleUsageAsync(Guid cycleId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorUsageReservation>> UsageHistoryAsync(Guid userId, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorUsageReservation>> ExpiredReservationsAsync(DateTime now, int take, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorProfile>> DeletedUserProfilesAsync(int take, CancellationToken ct);
    void Add(object entity);
}

public interface IAcceleratorPhase2Store
{
    Task<AcceleratorAssessmentTemplate?> TemplateAsync(Guid id, CancellationToken ct);
    Task<AcceleratorAssessmentTemplate?> TemplateAsync(Guid roleId, string version, CancellationToken ct);
    Task<AcceleratorQuestion?> QuestionAsync(Guid competencyId, string version, CancellationToken ct);
    Task<AcceleratorAssessmentAttempt?> AttemptAsync(Guid userId, Guid id, CancellationToken ct);
    Task<AcceleratorAssessmentAttempt?> ReadAttemptAsync(Guid userId, Guid id, CancellationToken ct);
    Task<AcceleratorAssessmentAttempt?> CurrentAttemptAsync(Guid userId, Guid contextId, Guid? goalId, DateTime now, CancellationToken ct);
    Task<AcceleratorAssessmentTemplate?> ActiveTemplateAsync(Guid roleId, Guid taxonomyId, CancellationToken ct);
    Task<AcceleratorAssessmentAttempt?> AttemptByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorQuestion>> QuestionsAsync(Guid taxonomyId, IReadOnlyCollection<Guid> competencyIds, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorQuestion>> QuestionsByIdsAsync(Guid taxonomyId, IReadOnlyCollection<Guid> questionIds, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorRoleRequirement>> RequirementsAsync(Guid roleId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorPrerequisite>> PrerequisitesAsync(Guid taxonomyId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorSkillEvidence>> EvidenceAsync(Guid userId, Guid goalId, CancellationToken ct);
    Task<AcceleratorRoadmap?> RoadmapAsync(Guid userId, Guid id, CancellationToken ct);
    Task<AcceleratorRoadmap?> ActiveRoadmapAsync(Guid userId, Guid goalId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorLearningActivity>> ActivitiesAsync(Guid userId, Guid roadmapId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorRoadmapEdge>> EdgesAsync(Guid roadmapId, CancellationToken ct);
    Task<AcceleratorLearningActivity?> ActivityAsync(Guid userId, Guid id, CancellationToken ct);
    Task<AcceleratorRoadmapNode?> NodeAsync(Guid id, CancellationToken ct);
    Task<AcceleratorProgressEvent?> ProgressEventByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorRecommendation>> RecommendationsAsync(Guid userId, Guid goalId, CancellationToken ct);
    Task<int> AttemptCountAsync(Guid userId, Guid goalId, CancellationToken ct);
    void Add(object entity);
}

// Foundation contracts only. No production endpoint dispatches AI requests in Phase 1.
public interface IAcceleratorAIProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<AcceleratorAIResult> ExecuteAsync(AcceleratorAIRequest request, CancellationToken ct);
}
public sealed record AcceleratorAIRequest(Guid ReservationId, string CorrelationId, AcceleratorFeature Task,
    string Model, string Instructions, string UntrustedCandidateContent, int MaxOutputTokens, int TimeoutSeconds);
public sealed record AcceleratorAIResult(string StructuredJson, string ProviderRequestId, string ModelVersion,
    int InputTokens, int OutputTokens, decimal ActualCostInr);
public interface IAcceleratorAIResultValidator
{
    bool Validate(AcceleratorFeature task, string structuredJson);
}
public sealed class AcceleratorProviderUnavailableException() :
    JobPortal.Application.Common.Exceptions.AppException("Accelerator AI is unavailable.", 503, "accelerator_ai_unavailable");
