using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record StartAcceleratorProjectRequest(Guid RequestKey, Guid GoalId, Guid ProjectVersionId);
public sealed record SubmitAcceleratorAssignmentRequest(Guid RequestKey, Guid AssignmentId, string Content, Guid ExpectedRevision);
public sealed record ChangeAcceleratorProjectStatusRequest(Guid ExpectedRevision, AcceleratorCandidateProjectStatus Status);
public sealed record AcceleratorProjectRecommendation(Guid ProjectVersionId, string Title, AcceleratorPracticeDifficulty Difficulty, string Reason);
public sealed record CandidateAcceleratorProjectResponse(Guid Id, Guid ProjectVersionId, AcceleratorCandidateProjectStatus Status, int ProgressPercent, Guid Revision);
public sealed record AcceleratorProjectProgressResponse(Guid ProjectId, AcceleratorCandidateProjectStatus Status,
    int ProgressPercent, Guid? CurrentMilestoneId, decimal? FinalScore, DateTime? CompletedAtUtc, Guid Revision);
public sealed record AcceleratorProjectSubmissionResponse(Guid Id, Guid AssignmentId, int AttemptNumber, AcceleratorSubmissionStatus Status, DateTime SubmittedAtUtc);
public sealed record AcceleratorProjectHistoryEntry(AcceleratorProjectSubmissionResponse Submission, AcceleratorProjectEvaluationResponse? Evaluation);
public sealed record AcceleratorCriterionObservation(Guid CriterionId, decimal Score);
public sealed record ManualAcceleratorEvaluationRequest(Guid CandidateId, IReadOnlyList<AcceleratorCriterionObservation> Observations);
public sealed record AcceleratorProjectEvaluationResponse(Guid Id, Guid SubmissionId, decimal Score, decimal MaximumScore, string Feedback, DateTime EvaluatedAtUtc);
public sealed record AcceleratorProjectDetailResponse(Guid Id, string Title, string ShortDescription, string BusinessContext,
    string Instructions, IReadOnlyList<AcceleratorProjectMilestoneResponse> Milestones);
public sealed record AcceleratorProjectMilestoneResponse(Guid Id, int Sequence, string Title, string Description,
    IReadOnlyList<AcceleratorProjectAssignmentResponse> Assignments);
public sealed record AcceleratorProjectAssignmentResponse(Guid Id, string Prompt, AcceleratorAssignmentType Type, int MaximumAttempts);
public sealed record CreateAcceleratorProjectDefinitionRequest(string StableKey);
public sealed record CreateAcceleratorDraftRequest(Guid DefinitionId, int Version, Guid TargetRoleId, AcceleratorProjectType Type,
    AcceleratorPracticeDifficulty Difficulty, int EstimatedMinutes, string Title, string ShortDescription, string BusinessContext, string Instructions);
public sealed record EditAcceleratorDraftRequest(Guid ExpectedRevision, Guid TargetRoleId, AcceleratorProjectType Type,
    AcceleratorPracticeDifficulty Difficulty, int EstimatedMinutes, string Title, string ShortDescription, string BusinessContext, string Instructions);
public sealed record AcceleratorDraftResponse(Guid Id, Guid Revision);
public sealed record AcceleratorAdminProjectVersionResponse(Guid Id, Guid DefinitionId, int Version,
    AcceleratorProjectCatalogStatus Status, Guid Revision, Guid TargetRoleId, AcceleratorProjectType Type,
    AcceleratorPracticeDifficulty Difficulty, int EstimatedMinutes, string Title, string ShortDescription,
    string BusinessContext, string Instructions, IReadOnlyList<AcceleratorAdminProjectCompetencyResponse> Competencies,
    IReadOnlyList<AcceleratorAdminProjectMilestoneResponse> Milestones, AcceleratorAdminProjectRubricResponse? Rubric);
public sealed record AcceleratorAdminProjectCompetencyResponse(Guid Id, Guid CompetencyId, bool IsPrerequisite, int RequiredProficiency);
public sealed record AcceleratorAdminProjectMilestoneResponse(Guid Id, int Sequence, string Title, string Description,
    int EstimatedMinutes, bool IsRequired, IReadOnlyList<AcceleratorAdminProjectAssignmentResponse> Assignments);
public sealed record AcceleratorAdminProjectAssignmentResponse(Guid Id, int Sequence, AcceleratorAssignmentType Type,
    AcceleratorProjectEvaluationMode EvaluationMode, string Prompt, string CompletionRequirements, string DeterministicRulesJson,
    int MaximumAttempts, bool RetryAllowed, int CooldownMinutes);
public sealed record AcceleratorAdminProjectRubricResponse(Guid Id, int Version, string Name,
    IReadOnlyList<AcceleratorAdminProjectCriterionResponse> Criteria);
public sealed record AcceleratorAdminProjectCriterionResponse(Guid Id, int Sequence, Guid? CompetencyId,
    string Name, string Description, decimal Weight, decimal MaximumScore);
public sealed record UpsertAcceleratorProjectCompetencyRequest(Guid ExpectedRevision, Guid CompetencyId, bool IsPrerequisite, int RequiredProficiency);
public sealed record UpsertAcceleratorProjectMilestoneRequest(Guid ExpectedRevision, Guid? Id, int Sequence, string Title, string Description, int EstimatedMinutes, bool IsRequired);
public sealed record UpsertAcceleratorProjectAssignmentRequest(Guid ExpectedRevision, Guid? Id, Guid MilestoneId, int Sequence, AcceleratorAssignmentType Type,
    AcceleratorProjectEvaluationMode EvaluationMode, string Prompt, string CompletionRequirements, string DeterministicRulesJson,
    int MaximumAttempts, bool RetryAllowed, int CooldownMinutes);
public sealed record UpsertAcceleratorProjectRubricRequest(Guid ExpectedRevision, string Name, int Version);
public sealed record UpsertAcceleratorProjectCriterionRequest(Guid ExpectedRevision, Guid? Id, int Sequence, Guid? CompetencyId, string Name,
    string Description, decimal Weight, decimal MaximumScore);
public sealed record AcceleratorEvaluatedAssignment(Guid AssignmentId, int AttemptNumber, decimal Score);

public interface IAcceleratorPhase4Store
{
    Task<AcceleratorProjectDefinition?> DefinitionByStableKeyAsync(string stableKey, CancellationToken ct);
    Task<AcceleratorProjectDefinition?> DefinitionAsync(Guid id, CancellationToken ct);
    Task<AcceleratorCompetency?> CompetencyAsync(Guid id, CancellationToken ct);
    Task<AcceleratorProjectVersion?> VersionByNumberAsync(Guid definitionId, int version, CancellationToken ct);
    Task<bool> VersionUsedAsync(Guid versionId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorAiDispatchRecord>> EligibleProjectDispatchesAsync(DateTime now, int take, CancellationToken ct);
    Task<AcceleratorAiDispatchRecord?> ClaimProjectDispatchAsync(Guid dispatchId, Guid claimId, DateTime now, DateTime expiresAt, CancellationToken ct);
    Task<bool> UpdateClaimedProjectDispatchAsync(AcceleratorAiDispatchRecord record, Guid claimId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorAiDispatchRecord>> UnresolvedProjectDispatchesAsync(int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorProjectVersion>> ActiveVersionsAsync(Guid roleId, int take, CancellationToken ct);
    Task<AcceleratorProjectVersion?> VersionAsync(Guid id, CancellationToken ct);
    Task<AcceleratorCandidateProject?> CandidateProjectAsync(Guid userId, Guid id, CancellationToken ct);
    Task<AcceleratorCandidateProject?> CandidateProjectByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorCandidateProject>> CandidateProjectsAsync(Guid userId, int skip, int take, CancellationToken ct);
    Task<AcceleratorProjectAssignment?> AssignmentAsync(Guid projectVersionId, Guid assignmentId, CancellationToken ct);
    Task<AcceleratorProjectSubmission?> SubmissionByKeyAsync(Guid userId, Guid projectId, Guid requestKey, CancellationToken ct);
    Task<AcceleratorProjectSubmission?> SubmissionAsync(Guid userId, Guid projectId, Guid submissionId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorProjectSubmission>> SubmissionsAsync(Guid userId, Guid projectId, Guid assignmentId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorProjectSubmission>> ProjectSubmissionsAsync(Guid userId, Guid projectId, int skip, int take, CancellationToken ct);
    Task<AcceleratorProjectEvaluation?> EvaluationAsync(Guid userId, Guid submissionId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorEvaluatedAssignment>> EvaluatedAssignmentsAsync(Guid userId, Guid projectId, CancellationToken ct);
    Task<AcceleratorProjectRubric?> ActiveRubricAsync(Guid projectVersionId, CancellationToken ct);
    void Add(object entity);
    void Remove(object entity);
}
