using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum AcceleratorProjectType { BackendApi = 1, Frontend, FullStack, DataSql, ProductManagement, SystemDesign, Debugging, BusinessCase, Mixed }
public enum AcceleratorCandidateProjectStatus { NotStarted = 1, InProgress, Submitted, UnderEvaluation, Completed, NeedsRevision, Abandoned }
public enum AcceleratorAssignmentType { Text = 1, Structured, Code, Sql, Architecture, ProductCase, DocumentLink }
public enum AcceleratorSubmissionStatus { Submitted = 1, Evaluating, Evaluated, NeedsRevision, Failed, Uncertain }
public enum AcceleratorProjectEvaluator { Deterministic = 1, ProviderAssisted, Manual }
public enum AcceleratorProjectEvaluationMode { Deterministic = 1, Manual, Ai }
public enum AcceleratorProjectCatalogStatus { Draft = 1, Published, Retired }

public sealed class AcceleratorProjectDefinition : BaseEntity
{
    public string StableKey { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public ICollection<AcceleratorProjectVersion> Versions { get; set; } = [];
}
public sealed class AcceleratorProjectVersion : BaseEntity
{
    public Guid DefinitionId { get; set; }
    public AcceleratorProjectDefinition Definition { get; set; } = null!;
    public int Version { get; set; }
    public Guid TargetRoleId { get; set; }
    public AcceleratorProjectType Type { get; set; }
    public AcceleratorPracticeDifficulty Difficulty { get; set; }
    public int EstimatedMinutes { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string BusinessContext { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public AcceleratorProjectCatalogStatus CatalogStatus { get; set; } = AcceleratorProjectCatalogStatus.Draft;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<AcceleratorProjectMilestone> Milestones { get; set; } = [];
    public ICollection<AcceleratorProjectCompetency> Competencies { get; set; } = [];
    public ICollection<AcceleratorProjectRubric> Rubrics { get; set; } = [];
}
public sealed class AcceleratorProjectCompetency : BaseEntity
{
    public Guid ProjectVersionId { get; set; }
    public Guid CompetencyId { get; set; }
    public bool IsPrerequisite { get; set; }
    public int RequiredProficiency { get; set; }
}
public sealed class AcceleratorProjectMilestone : BaseEntity
{
    public Guid ProjectVersionId { get; set; }
    public AcceleratorProjectVersion ProjectVersion { get; set; } = null!;
    public int Sequence { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; }
    public bool IsRequired { get; set; }
    public ICollection<AcceleratorProjectAssignment> Assignments { get; set; } = [];
}
public sealed class AcceleratorProjectAssignment : BaseEntity
{
    public Guid MilestoneId { get; set; }
    public int Sequence { get; set; }
    public AcceleratorProjectMilestone Milestone { get; set; } = null!;
    public AcceleratorAssignmentType Type { get; set; }
    public AcceleratorProjectEvaluationMode EvaluationMode { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string CompletionRequirements { get; set; } = string.Empty;
    // Server-only, versioned evaluator rules. Never serialize the entity in candidate responses.
    public string DeterministicRulesJson { get; set; } = "[]";
    public int MaximumAttempts { get; set; }
    public bool RetryAllowed { get; set; }
    public int CooldownMinutes { get; set; }
}
public sealed class AcceleratorProjectRubric : BaseEntity
{
    public Guid ProjectVersionId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public ICollection<AcceleratorProjectRubricCriterion> Criteria { get; set; } = [];
}
public sealed class AcceleratorProjectRubricCriterion : BaseEntity
{
    public Guid RubricId { get; set; }
    public Guid? CompetencyId { get; set; }
    public int Sequence { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal MaximumScore { get; set; }
}
public sealed class AcceleratorCandidateProject : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid GoalId { get; set; }
    public Guid ProjectVersionId { get; set; }
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public AcceleratorCandidateProjectStatus Status { get; set; }
    public int ProgressPercent { get; set; }
    public Guid? CurrentMilestoneId { get; set; }
    public Guid? RequiredRevisionAssignmentId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public decimal? FinalScore { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
public sealed class AcceleratorProjectSubmission : BaseEntity
{
    public Guid CandidateProjectId { get; set; }
    public Guid AssignmentId { get; set; }
    public int AttemptNumber { get; set; }
    public Guid RequestKey { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public AcceleratorSubmissionStatus Status { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}
public sealed class AcceleratorProjectEvaluation : BaseEntity
{
    public Guid SubmissionId { get; set; }
    public decimal Score { get; set; }
    public decimal MaximumScore { get; set; }
    public AcceleratorProjectEvaluator Evaluator { get; set; }
    public decimal Confidence { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public DateTime EvaluatedAtUtc { get; set; }
    public ICollection<AcceleratorProjectEvaluationScore> Scores { get; set; } = [];
}
public sealed class AcceleratorProjectEvaluationScore : BaseEntity
{
    public Guid EvaluationId { get; set; }
    public Guid CriterionId { get; set; }
    public decimal Score { get; set; }
    public string Feedback { get; set; } = string.Empty;
}
