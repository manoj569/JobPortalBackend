using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum AcceleratorLearningPathType { Career = 1, Skill }
public enum AcceleratorLearningCatalogStatus { Draft = 1, Published, Retired }
public enum AcceleratorLearningEnrollmentStatus { Enrolled = 1, InProgress, Completed, Paused, Abandoned }
public enum AcceleratorLessonProgressStatus { NotStarted = 1, InProgress, Completed }
public enum AcceleratorLessonType { Article = 1, Video, Interactive, Reference, Exercise }
public enum AcceleratorLessonResourceType { Link = 1, Document, Video }
public enum AcceleratorEnrollmentProgressKind { LessonStarted = 1, LessonCompleted }

/// <summary>Published, reusable learning content. It references canonical Career Accelerator data rather than copying it.</summary>
public sealed class AcceleratorLearningPath : BaseEntity
{
    public AcceleratorLearningPathType Type { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? CanonicalCareerRoleId { get; set; }
    public AcceleratorCareerRole? CanonicalCareerRole { get; set; }
    public Guid? CanonicalSkillId { get; set; }
    public Skill? CanonicalSkill { get; set; }
    public int Difficulty { get; set; }
    public int EstimatedMinutes { get; set; }
    public AcceleratorLearningCatalogStatus Status { get; set; }
    public int DisplayOrder { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<AcceleratorLearningModule> Modules { get; set; } = [];
    public ICollection<AcceleratorLearningModulePrerequisite> ModulePrerequisites { get; set; } = [];
}

public sealed class AcceleratorLearningModule : BaseEntity
{
    public Guid LearningPathId { get; set; }
    public AcceleratorLearningPath LearningPath { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public int Difficulty { get; set; }
    public int EstimatedMinutes { get; set; }
    public bool IsRequired { get; set; }
    public AcceleratorLearningCatalogStatus Status { get; set; }
    public Guid? AssessmentTemplateId { get; set; }
    public Guid? ProjectVersionId { get; set; }
    public Guid? InterviewVersionId { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<AcceleratorLesson> Lessons { get; set; } = [];
}

public sealed class AcceleratorLearningModulePrerequisite : BaseEntity
{
    public Guid LearningPathId { get; set; }
    public Guid ModuleId { get; set; }
    public AcceleratorLearningModule Module { get; set; } = null!;
    public Guid RequiredModuleId { get; set; }
    public AcceleratorLearningModule RequiredModule { get; set; } = null!;
}

public sealed class AcceleratorLesson : BaseEntity
{
    public Guid ModuleId { get; set; }
    public AcceleratorLearningModule Module { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AcceleratorLessonType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public int EstimatedMinutes { get; set; }
    public bool IsRequired { get; set; }
    public AcceleratorLearningCatalogStatus Status { get; set; }
    public ICollection<AcceleratorLessonResource> Resources { get; set; } = [];
}

public sealed class AcceleratorLessonResource : BaseEntity
{
    public Guid LessonId { get; set; }
    public AcceleratorLesson Lesson { get; set; } = null!;
    public AcceleratorLessonResourceType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public AcceleratorLearningCatalogStatus Status { get; set; }
}

public sealed class AcceleratorPathEnrollment : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid LearningPathId { get; set; }
    public AcceleratorLearningPath LearningPath { get; set; } = null!;
    public int CurrentLevel { get; set; }
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public int TargetLevel { get; set; }
    public int WeeklyCommitmentMinutes { get; set; }
    public DateTime? TargetCompletionAtUtc { get; set; }
    public DateTime EnrolledAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CurrentModuleId { get; set; }
    public Guid? CurrentLessonId { get; set; }
    public AcceleratorLearningEnrollmentStatus Status { get; set; }
    public int OverallProgressPercent { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<AcceleratorLessonProgress> LessonProgress { get; set; } = [];
}

public sealed class AcceleratorLessonProgress : BaseEntity
{
    public Guid EnrollmentId { get; set; }
    public AcceleratorPathEnrollment Enrollment { get; set; } = null!;
    public Guid LessonId { get; set; }
    public AcceleratorLesson Lesson { get; set; } = null!;
    public AcceleratorLessonProgressStatus Status { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? LastRequestKey { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

/// <summary>Append-only, candidate-owned lesson journey evidence used for idempotent progression.</summary>
public sealed class AcceleratorEnrollmentProgressEvent : BaseEntity
{
    public Guid EnrollmentId { get; set; }
    public AcceleratorPathEnrollment Enrollment { get; set; } = null!;
    public Guid LessonId { get; set; }
    public Guid RequestKey { get; set; }
    public AcceleratorEnrollmentProgressKind Kind { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
