using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum AcceleratorAssessmentStatus { Draft = 1, Ready, InProgress, Submitted, Evaluating, Completed, Failed, Retryable }
public enum AcceleratorQuestionType { MultipleChoice = 1, MultipleSelect, TrueFalse, ShortAnswer, Scenario, TechnicalReasoning, SqlReasoning, CodingDescription, ProductCase }
public enum AcceleratorProficiency { NotAssessed = 0, Beginner, Developing, Intermediate, Advanced }
public enum AcceleratorEvidenceSource { SelfReported = 1, ResumeInferred, AssessmentDemonstrated, AssignmentDemonstrated }
public enum AcceleratorRoadmapStatus { Active = 1, Superseded, Completed, Archived }
public enum AcceleratorNodeStatus { Locked = 1, Available, InProgress, Completed, NeedsPractice }
public enum AcceleratorProgressKind { Started = 1, Completed, EvidenceSubmitted, MilestoneCompleted, Rescheduled }

public sealed class AcceleratorAssessmentTemplate : BaseEntity
{
    public Guid RoleId { get; set; }
    public AcceleratorCareerRole Role { get; set; } = null!;
    public string Version { get; set; } = string.Empty;
    public string SelectionStrategy { get; set; } = "deterministic-v1";
    public int TimeLimitMinutes { get; set; }
    public int MaximumQuestions { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AcceleratorQuestion : BaseEntity
{
    public Guid TaxonomyId { get; set; }
    public Guid CompetencyId { get; set; }
    public AcceleratorCompetency Competency { get; set; } = null!;
    public string Version { get; set; } = string.Empty;
    public AcceleratorQuestionType Type { get; set; }
    public int Difficulty { get; set; }
    public decimal Weight { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string OptionsJson { get; set; } = "[]";
    public string AnswerKeyJson { get; set; } = "{}";
    public string RubricJson { get; set; } = "{}";
    public string Explanation { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class AcceleratorAssessmentAttempt : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid GoalId { get; set; }
    public AcceleratorCareerGoal Goal { get; set; } = null!;
    public Guid TemplateId { get; set; }
    public string TemplateVersion { get; set; } = string.Empty;
    public Guid TaxonomyId { get; set; }
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public AcceleratorAssessmentStatus Status { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public decimal? Score { get; set; }
    public string SelectionStrategy { get; set; } = string.Empty;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<AcceleratorAssessmentAnswer> Answers { get; set; } = [];
}

public sealed class AcceleratorAssessmentAnswer : BaseEntity
{
    public Guid AttemptId { get; set; }
    public AcceleratorAssessmentAttempt Attempt { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public string QuestionVersion { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string SubmittedAnswerJson { get; set; } = "{}";
    public bool? IsCorrect { get; set; }
    public decimal? Score { get; set; }
    public string EvaluationEvidenceJson { get; set; } = "{}";
}

public sealed class AcceleratorSkillEvidence : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid GoalId { get; set; }
    public Guid CompetencyId { get; set; }
    public AcceleratorEvidenceSource Source { get; set; }
    public AcceleratorProficiency Proficiency { get; set; }
    public decimal Confidence { get; set; }
    public string ScoringVersion { get; set; } = string.Empty;
    public Guid? AttemptId { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
}

public sealed class AcceleratorRoadmap : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid GoalId { get; set; }
    public int Version { get; set; }
    public AcceleratorRoadmapStatus Status { get; set; }
    public string GenerationStrategy { get; set; } = "deterministic-v1";
    public Guid TaxonomyId { get; set; }
    public DateTime PlannedStartAtUtc { get; set; }
    public DateTime TargetAtUtc { get; set; }
    public int EstimatedMinutes { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public ICollection<AcceleratorRoadmapNode> Nodes { get; set; } = [];
}

public sealed class AcceleratorRoadmapNode : BaseEntity
{
    public Guid RoadmapId { get; set; }
    public AcceleratorRoadmap Roadmap { get; set; } = null!;
    public Guid CompetencyId { get; set; }
    public string StableKey { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public AcceleratorNodeStatus Status { get; set; }
    public int EstimatedMinutes { get; set; }
    public int ProgressPercent { get; set; }
    public string CompletionEvidenceJson { get; set; } = "[]";
}

public sealed class AcceleratorRoadmapEdge : BaseEntity
{
    public Guid RoadmapId { get; set; }
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
}

public sealed class AcceleratorLearningActivity : BaseEntity
{
    public Guid RoadmapNodeId { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public AcceleratorNodeStatus Status { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public sealed class AcceleratorProgressEvent : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid ActivityId { get; set; }
    public Guid RequestKey { get; set; }
    public AcceleratorProgressKind Kind { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string EvidenceJson { get; set; } = "{}";
}

public sealed class AcceleratorRecommendation : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid GoalId { get; set; }
    public Guid? RoadmapNodeId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string EvidenceJson { get; set; } = "[]";
    public DateTime GeneratedAtUtc { get; set; }
}
