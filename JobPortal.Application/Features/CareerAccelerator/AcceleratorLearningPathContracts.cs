using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record LearningPathCardResponse(Guid Id, string Slug, AcceleratorLearningPathType Type, string Title,
    string ShortDescription, int Difficulty, int EstimatedMinutes, int DisplayOrder, Guid? CanonicalCareerRoleId,
    Guid? CanonicalSkillId);
public sealed record LearningPathDetailResponse(LearningPathCardResponse Path, IReadOnlyList<LearningModuleResponse> Modules);
public sealed record LearningModuleResponse(Guid Id, string Title, string Description, int Sequence, int Difficulty,
    int EstimatedMinutes, bool IsRequired, AcceleratorLearningCatalogStatus Status, int ProgressPercent,
    bool IsCompleted, IReadOnlyList<LearningLessonResponse> Lessons);
public sealed record LearningLessonResponse(Guid Id, string Title, string Description, AcceleratorLessonType Type,
    string Content, int Sequence, int EstimatedMinutes, bool IsRequired, AcceleratorLearningCatalogStatus Status,
    AcceleratorLessonProgressStatus ProgressStatus, IReadOnlyList<LearningLessonResourceResponse> Resources);
public sealed record LearningLessonResourceResponse(Guid Id, AcceleratorLessonResourceType Type, string Title,
    string Url, string Description, int Sequence);
public sealed record EnrollLearningPathRequest(Guid RequestKey, int CurrentLevel, int TargetLevel,
    int WeeklyCommitmentMinutes, DateTime? TargetCompletionAtUtc);
public sealed record StartLearningLessonRequest(Guid RequestKey);
public sealed record CompleteLearningLessonRequest(Guid RequestKey);
public sealed record LearningEnrollmentResponse(Guid Id, Guid LearningPathId, string PathSlug,
    AcceleratorLearningPathType PathType, AcceleratorLearningEnrollmentStatus Status, int OverallProgressPercent,
    Guid? CurrentModuleId, Guid? CurrentLessonId, string? NextAction, DateTime EnrolledAtUtc,
    DateTime? StartedAtUtc, DateTime? CompletedAtUtc, DateTime LastActivityAtUtc, Guid Revision);

public interface IAcceleratorLearningPathStore
{
    Task<IReadOnlyList<AcceleratorLearningPath>> PublishedPathsAsync(AcceleratorLearningPathType? type, int skip, int take, CancellationToken ct);
    Task<AcceleratorLearningPath?> PublishedPathBySlugAsync(string slug, CancellationToken ct);
    Task<AcceleratorLearningPath?> PathAsync(Guid id, CancellationToken ct);
    Task<AcceleratorPathEnrollment?> EnrollmentAsync(Guid userId, Guid enrollmentId, CancellationToken ct);
    Task<AcceleratorPathEnrollment?> ActiveEnrollmentForPathAsync(Guid userId, Guid pathId, CancellationToken ct);
    Task<AcceleratorPathEnrollment?> EnrollmentByRequestKeyAsync(Guid userId, Guid requestKey, CancellationToken ct);
    Task<AcceleratorEnrollmentProgressEvent?> ProgressEventAsync(Guid enrollmentId, Guid requestKey, CancellationToken ct);
    void Add(object entity);
}
