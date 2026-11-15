using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum AcceleratorInterviewType { Technical = 1, Behavioral, SystemDesign, ProductSense, ProductExecution, Analytics, CaseStudy, RoleSpecific, Mixed }
public enum AcceleratorInterviewCatalogStatus { Draft = 1, Published, Retired }
public enum AcceleratorInterviewQuestionKind { Static = 1, TemplateGenerated, AiSelected, AiGenerated }
public enum AcceleratorInterviewQuestionType { OpenEnded = 1, TechnicalConcept, Scenario, Behavioral, SystemDesign, ProductCase, Analytical, SqlReasoning, DebuggingReasoning, Tradeoff, FollowUp }
public enum AcceleratorInterviewSessionStatus { Created = 1, InProgress, Submitted, UnderEvaluation, Completed, NeedsReview, Abandoned }
public enum AcceleratorInterviewEvaluationMode { Deterministic = 1, Ai, Manual }

public sealed class AcceleratorInterviewDefinition : BaseEntity
{
    public string StableKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid TargetRoleId { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<AcceleratorInterviewVersion> Versions { get; set; } = [];
}

public sealed class AcceleratorInterviewVersion : BaseEntity
{
    public Guid DefinitionId { get; set; }
    public AcceleratorInterviewDefinition Definition { get; set; } = null!;
    public int Version { get; set; }
    public AcceleratorInterviewType Type { get; set; }
    public AcceleratorPracticeDifficulty Difficulty { get; set; }
    public int EstimatedMinutes { get; set; }
    public string TargetExperienceLevel { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public AcceleratorInterviewCatalogStatus Status { get; set; } = AcceleratorInterviewCatalogStatus.Draft;
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<AcceleratorInterviewCompetency> Competencies { get; set; } = [];
    public ICollection<AcceleratorInterviewSection> Sections { get; set; } = [];
    public ICollection<AcceleratorInterviewRubricCriterion> Criteria { get; set; } = [];
}

public sealed class AcceleratorInterviewCompetency : BaseEntity
{
    public Guid InterviewVersionId { get; set; }
    public Guid CompetencyId { get; set; }
    public int ExpectedProficiency { get; set; }
    public decimal Weight { get; set; }
    public bool IsRequired { get; set; }
    public int QuestionCoverageTarget { get; set; }
}

public sealed class AcceleratorInterviewSection : BaseEntity
{
    public Guid InterviewVersionId { get; set; }
    public AcceleratorInterviewVersion Version { get; set; } = null!;
    public int Sequence { get; set; }
    public string Title { get; set; } = string.Empty;
    public AcceleratorInterviewType Type { get; set; }
    public int EstimatedMinutes { get; set; }
    public bool IsRequired { get; set; }
    public int QuestionCount { get; set; }
    public int MaxFollowUpsPerQuestion { get; set; }
    public bool AllowStaticFallback { get; set; } = true;
    public ICollection<AcceleratorInterviewQuestion> Questions { get; set; } = [];
}

public sealed class AcceleratorInterviewQuestion : BaseEntity
{
    public Guid SectionId { get; set; }
    public AcceleratorInterviewSection Section { get; set; } = null!;
    public Guid? CompetencyId { get; set; }
    public int Sequence { get; set; }
    public AcceleratorInterviewQuestionKind Kind { get; set; }
    public AcceleratorInterviewQuestionType Type { get; set; }
    public AcceleratorInterviewEvaluationMode EvaluationMode { get; set; }
    public AcceleratorPracticeDifficulty Difficulty { get; set; }
    public string PublicPrompt { get; set; } = string.Empty;
    public string ExpectedConceptsJson { get; set; } = "[]";
    public string HiddenEvaluatorGuidance { get; set; } = string.Empty;
    public string DeterministicRulesJson { get; set; } = "[]";
    public string FollowUpPrompt { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class AcceleratorInterviewRubricCriterion : BaseEntity
{
    public Guid InterviewVersionId { get; set; }
    public Guid? CompetencyId { get; set; }
    public int Sequence { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal MaximumScore { get; set; }
}

public sealed class AcceleratorInterviewSession : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid GoalId { get; set; }
    public Guid InterviewVersionId { get; set; }
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public AcceleratorInterviewSessionStatus Status { get; set; } = AcceleratorInterviewSessionStatus.Created;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public decimal? OverallScore { get; set; }
    public decimal? ReadinessImpact { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<AcceleratorSessionQuestion> Questions { get; set; } = [];
}

public sealed class AcceleratorSessionQuestion : BaseEntity
{
    public Guid SessionId { get; set; }
    public AcceleratorInterviewSession Session { get; set; } = null!;
    public Guid SourceQuestionId { get; set; }
    public Guid SectionId { get; set; }
    public Guid? CompetencyId { get; set; }
    public Guid? ParentQuestionId { get; set; }
    public int Sequence { get; set; }
    public AcceleratorInterviewQuestionType Type { get; set; }
    public string PublicPrompt { get; set; } = string.Empty;
    public DateTime AssignedAtUtc { get; set; }
    public Guid? GenerationDispatchId { get; set; }
    public Guid? GenerationRequestKey { get; set; }
    public string GenerationRequestHash { get; set; } = string.Empty;
    public string GenerationProvider { get; set; } = string.Empty;
    public string GenerationModel { get; set; } = string.Empty;
    public string CompetencyIdsJson { get; set; } = "[]";
    public string GenerationMetadataJson { get; set; } = "{}";
    public bool FollowUpEligible { get; set; } = true;
}

public sealed class AcceleratorInterviewAnswer : BaseEntity
{
    public Guid SessionId { get; set; }
    public Guid SessionQuestionId { get; set; }
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; }
}

public sealed class AcceleratorInterviewEvaluation : BaseEntity
{
    public Guid AnswerId { get; set; }
    public AcceleratorInterviewEvaluationMode Evaluator { get; set; }
    public decimal Score { get; set; }
    public decimal Confidence { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public DateTime EvaluatedAtUtc { get; set; }
    public ICollection<AcceleratorInterviewCriterionScore> CriterionScores { get; set; } = [];
}

public sealed class AcceleratorInterviewCriterionScore : BaseEntity
{
    public Guid EvaluationId { get; set; }
    public Guid CriterionId { get; set; }
    public decimal Score { get; set; }
}

public sealed class AcceleratorReadinessSnapshot : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid GoalId { get; set; }
    public Guid? TriggerInterviewSessionId { get; set; }
    public string CalculationVersion { get; set; } = "readiness-v1";
    public decimal SkillCoverage { get; set; }
    public decimal PracticePerformance { get; set; }
    public decimal ProjectPerformance { get; set; }
    public decimal InterviewPerformance { get; set; }
    public decimal Total { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public DateTime CalculatedAtUtc { get; set; }
}

public sealed class AcceleratorPassportShare : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid GoalId { get; set; }
    public string PublicToken { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public DateTime? EnabledAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
