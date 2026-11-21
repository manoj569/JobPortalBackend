using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

/// <summary>Candidate journey layer over the immutable taxonomy and existing Accelerator engines.</summary>
public sealed class AcceleratorLearningPathService(IAcceleratorStore writes, IAcceleratorLearningPathStore store,
    AcceleratorService access, TimeProvider clock, IAuditWriter audit)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<LearningPathCardResponse>> PathsAsync(Guid actor, AcceleratorLearningPathType? type,
        int page, int pageSize, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        AcceleratorService.Page(page, pageSize);
        return (await store.PublishedPathsAsync(type, (page - 1) * pageSize, pageSize, ct)).Select(PathCard).ToArray();
    }

    public async Task<LearningPathDetailResponse> PathAsync(Guid actor, string slug, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 160) throw Invalid("Invalid learning path.");
        var path = await store.PublishedPathBySlugAsync(slug.Trim().ToLowerInvariant(), ct) ?? throw new NotFoundException("Learning path not found.");
        return new LearningPathDetailResponse(PathCard(path), path.Modules.Where(Published).OrderBy(x => x.Sequence)
            .Select(x => ModuleDto(x, null)).ToArray());
    }

    public Task<LearningEnrollmentResponse> EnrollAsync(Guid actor, Guid pathId, EnrollLearningPathRequest request,
        CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        ValidateEnrollment(request);
        var replay = await store.EnrollmentByRequestKeyAsync(actor, request.RequestKey, ct);
        if (replay is not null)
        {
            if (replay.RequestHash != Hash(request)) throw new ConflictException("Enrollment request key was reused with different data.", "accelerator_idempotency_conflict");
            return EnrollmentDto(replay);
        }
        var path = await store.PathAsync(pathId, ct) ?? throw new NotFoundException("Learning path not found.");
        if (path.Status != AcceleratorLearningCatalogStatus.Published) throw new ConflictException("Learning path is not available.", "accelerator_path_unavailable");
        var duplicate = await store.ActiveEnrollmentForPathAsync(actor, pathId, ct);
        if (duplicate is not null) return EnrollmentDto(duplicate);
        var enrollment = new AcceleratorPathEnrollment
        {
            UserId = actor, LearningPathId = path.Id, RequestKey = request.RequestKey, RequestHash = Hash(request),
            CurrentLevel = request.CurrentLevel, TargetLevel = request.TargetLevel,
            WeeklyCommitmentMinutes = request.WeeklyCommitmentMinutes, TargetCompletionAtUtc = request.TargetCompletionAtUtc,
            EnrolledAtUtc = Now, LastActivityAtUtc = Now, Status = AcceleratorLearningEnrollmentStatus.Enrolled
        };
        store.Add(enrollment);
        await AuditAsync(actor, AuditAction.Create, "AcceleratorPathEnrollment", enrollment.Id, ct);
        return EnrollmentDto(enrollment, path);
    }, ct);

    public async Task<LearningEnrollmentResponse> EnrollmentAsync(Guid actor, Guid enrollmentId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        return EnrollmentDto(await RequiredEnrollment(actor, enrollmentId, ct));
    }

    public async Task<LearningModuleResponse> ModuleAsync(Guid actor, Guid enrollmentId, Guid moduleId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var enrollment = await RequiredEnrollment(actor, enrollmentId, ct);
        var module = enrollment.LearningPath.Modules.SingleOrDefault(x => x.Id == moduleId && Published(x)) ?? throw new NotFoundException("Learning module not found.");
        return ModuleDto(module, enrollment);
    }

    public async Task<LearningLessonResponse> LessonAsync(Guid actor, Guid enrollmentId, Guid moduleId, Guid lessonId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var enrollment = await RequiredEnrollment(actor, enrollmentId, ct);
        var module = enrollment.LearningPath.Modules.SingleOrDefault(x => x.Id == moduleId && Published(x)) ?? throw new NotFoundException("Learning module not found.");
        var lesson = module.Lessons.SingleOrDefault(x => x.Id == lessonId && Published(x)) ?? throw new NotFoundException("Lesson not found.");
        return LessonDto(lesson, enrollment);
    }

    public Task<LearningEnrollmentResponse> StartLessonAsync(Guid actor, Guid enrollmentId, Guid moduleId, Guid lessonId,
        StartLearningLessonRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        var enrollment = await RequiredEnrollment(actor, enrollmentId, ct);
        var replay = await store.ProgressEventAsync(enrollmentId, request.RequestKey, ct);
        if (replay is not null)
        {
            if (replay.Kind != AcceleratorEnrollmentProgressKind.LessonStarted || replay.LessonId != lessonId) throw new ConflictException("Lesson request key was reused with different data.", "accelerator_idempotency_conflict");
            return EnrollmentDto(enrollment);
        }
        var lesson = RequiredLesson(enrollment, moduleId, lessonId);
        EnsureModuleAvailable(enrollment, lesson.ModuleId);
        var progress = enrollment.LessonProgress.SingleOrDefault(x => x.LessonId == lesson.Id);
        if (progress is null)
        {
            progress = new AcceleratorLessonProgress { EnrollmentId = enrollment.Id, LessonId = lesson.Id, Status = AcceleratorLessonProgressStatus.InProgress, StartedAtUtc = Now, LastRequestKey = request.RequestKey };
            store.Add(progress);
        }
        else if (progress.Status == AcceleratorLessonProgressStatus.NotStarted)
        {
            progress.Status = AcceleratorLessonProgressStatus.InProgress; progress.StartedAtUtc ??= Now; progress.LastRequestKey = request.RequestKey; progress.Revision = Guid.NewGuid();
        }
        enrollment.Status = AcceleratorLearningEnrollmentStatus.InProgress; enrollment.StartedAtUtc ??= Now; enrollment.CurrentModuleId = moduleId; enrollment.CurrentLessonId = lessonId; enrollment.LastActivityAtUtc = Now; enrollment.Revision = Guid.NewGuid();
        store.Add(new AcceleratorEnrollmentProgressEvent { EnrollmentId = enrollment.Id, LessonId = lesson.Id, RequestKey = request.RequestKey, Kind = AcceleratorEnrollmentProgressKind.LessonStarted, OccurredAtUtc = Now });
        await AuditAsync(actor, AuditAction.Update, "AcceleratorLessonProgress", progress.Id, ct);
        return EnrollmentDto(enrollment);
    }, ct);

    public Task<LearningEnrollmentResponse> CompleteLessonAsync(Guid actor, Guid enrollmentId, Guid moduleId, Guid lessonId,
        CompleteLearningLessonRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        var enrollment = await RequiredEnrollment(actor, enrollmentId, ct);
        var replay = await store.ProgressEventAsync(enrollmentId, request.RequestKey, ct);
        if (replay is not null)
        {
            if (replay.Kind != AcceleratorEnrollmentProgressKind.LessonCompleted || replay.LessonId != lessonId) throw new ConflictException("Lesson request key was reused with different data.", "accelerator_idempotency_conflict");
            return EnrollmentDto(enrollment);
        }
        var lesson = RequiredLesson(enrollment, moduleId, lessonId);
        EnsureModuleAvailable(enrollment, lesson.ModuleId);
        var progress = enrollment.LessonProgress.SingleOrDefault(x => x.LessonId == lesson.Id);
        if (progress is null)
        {
            progress = new AcceleratorLessonProgress { EnrollmentId = enrollment.Id, LessonId = lesson.Id, Status = AcceleratorLessonProgressStatus.Completed, StartedAtUtc = Now, CompletedAtUtc = Now, LastRequestKey = request.RequestKey };
            store.Add(progress);
        }
        else if (progress.Status != AcceleratorLessonProgressStatus.Completed)
        {
            progress.Status = AcceleratorLessonProgressStatus.Completed; progress.StartedAtUtc ??= Now; progress.CompletedAtUtc = Now; progress.LastRequestKey = request.RequestKey; progress.Revision = Guid.NewGuid();
        }
        store.Add(new AcceleratorEnrollmentProgressEvent { EnrollmentId = enrollment.Id, LessonId = lesson.Id, RequestKey = request.RequestKey, Kind = AcceleratorEnrollmentProgressKind.LessonCompleted, OccurredAtUtc = Now });
        RefreshEnrollment(enrollment);
        await AuditAsync(actor, AuditAction.Update, "AcceleratorLessonProgress", progress.Id, ct);
        return EnrollmentDto(enrollment);
    }, ct);

    private async Task<AcceleratorPathEnrollment> RequiredEnrollment(Guid actor, Guid enrollmentId, CancellationToken ct) =>
        await store.EnrollmentAsync(actor, enrollmentId, ct) ?? throw new NotFoundException("Learning enrollment not found.");

    private static AcceleratorLesson RequiredLesson(AcceleratorPathEnrollment enrollment, Guid moduleId, Guid lessonId)
    {
        var module = enrollment.LearningPath.Modules.SingleOrDefault(x => x.Id == moduleId && Published(x)) ?? throw new NotFoundException("Learning module not found.");
        return module.Lessons.SingleOrDefault(x => x.Id == lessonId && Published(x)) ?? throw new NotFoundException("Lesson not found.");
    }

    private static void EnsureModuleAvailable(AcceleratorPathEnrollment enrollment, Guid moduleId)
    {
        var module = enrollment.LearningPath.Modules.Single(x => x.Id == moduleId);
        var prerequisites = enrollment.LearningPath.ModulePrerequisites.Where(x => x.ModuleId == moduleId)
            .Select(x => x.RequiredModuleId).ToHashSet();
        var blocked = prerequisites.Any(id => !ModuleComplete(enrollment.LearningPath.Modules.Single(x => x.Id == id), enrollment));
        if (blocked) throw new ConflictException("Complete prerequisite learning modules first.", "accelerator_module_locked");
    }

    private void RefreshEnrollment(AcceleratorPathEnrollment enrollment)
    {
        var modules = enrollment.LearningPath.Modules.Where(x => Published(x) && x.IsRequired).OrderBy(x => x.Sequence).ToArray();
        var completed = modules.Count(x => ModuleComplete(x, enrollment));
        enrollment.OverallProgressPercent = modules.Length == 0 ? 0 : (int)Math.Floor(completed * 100m / modules.Length);
        var next = modules.FirstOrDefault(x => !ModuleComplete(x, enrollment));
        var nextLesson = next?.Lessons.Where(Published).OrderBy(x => x.Sequence)
            .FirstOrDefault(x => enrollment.LessonProgress.All(p => p.LessonId != x.Id || p.Status != AcceleratorLessonProgressStatus.Completed));
        enrollment.CurrentModuleId = next?.Id;
        enrollment.CurrentLessonId = nextLesson?.Id;
        enrollment.LastActivityAtUtc = Now;
        enrollment.Status = enrollment.OverallProgressPercent == 100 && modules.Length > 0 ? AcceleratorLearningEnrollmentStatus.Completed : AcceleratorLearningEnrollmentStatus.InProgress;
        enrollment.CompletedAtUtc = enrollment.Status == AcceleratorLearningEnrollmentStatus.Completed ? Now : null;
        enrollment.Revision = Guid.NewGuid();
    }

    private static bool ModuleComplete(AcceleratorLearningModule module, AcceleratorPathEnrollment enrollment)
    {
        var required = module.Lessons.Where(x => Published(x) && x.IsRequired).Select(x => x.Id).ToArray();
        return required.All(id => enrollment.LessonProgress.Any(x => x.LessonId == id && x.Status == AcceleratorLessonProgressStatus.Completed));
    }

    private static bool Published(AcceleratorLearningModule module) => !module.IsDeleted && module.Status == AcceleratorLearningCatalogStatus.Published;
    private static bool Published(AcceleratorLesson lesson) => !lesson.IsDeleted && lesson.Status == AcceleratorLearningCatalogStatus.Published;
    private static LearningPathCardResponse PathCard(AcceleratorLearningPath path) => new(path.Id, path.Slug, path.Type, path.Title, path.ShortDescription, path.Difficulty, path.EstimatedMinutes, path.DisplayOrder, path.CanonicalCareerRoleId, path.CanonicalSkillId,path.ConfigurationSource);
    private static LearningModuleResponse ModuleDto(AcceleratorLearningModule module, AcceleratorPathEnrollment? enrollment)
    {
        var required = module.Lessons.Where(x => Published(x) && x.IsRequired).ToArray();
        var completed = enrollment is null ? 0 : required.Count(x => enrollment.LessonProgress.Any(p => p.LessonId == x.Id && p.Status == AcceleratorLessonProgressStatus.Completed));
        var progress = required.Length == 0 ? 0 : (int)Math.Floor(completed * 100m / required.Length);
        return new(module.Id, module.Title, module.Description, module.Sequence, module.Difficulty, module.EstimatedMinutes, module.IsRequired, module.Status, progress, required.Length > 0 && completed == required.Length, module.Lessons.Where(Published).OrderBy(x => x.Sequence).Select(x => LessonDto(x, enrollment)).ToArray());
    }
    private static LearningLessonResponse LessonDto(AcceleratorLesson lesson, AcceleratorPathEnrollment? enrollment) => new(lesson.Id, lesson.Title, lesson.Description, lesson.Type, lesson.Content, lesson.Sequence, lesson.EstimatedMinutes, lesson.IsRequired, lesson.Status, enrollment?.LessonProgress.SingleOrDefault(x => x.LessonId == lesson.Id)?.Status ?? AcceleratorLessonProgressStatus.NotStarted, lesson.Resources.Where(x => !x.IsDeleted && x.Status == AcceleratorLearningCatalogStatus.Published).OrderBy(x => x.Sequence).Select(x => new LearningLessonResourceResponse(x.Id, x.Type, x.Title, x.Url, x.Description, x.Sequence)).ToArray());
    private static LearningEnrollmentResponse EnrollmentDto(AcceleratorPathEnrollment enrollment, AcceleratorLearningPath? path = null)
    {
        path ??= enrollment.LearningPath;
        var next = enrollment.CurrentLessonId.HasValue ? "Continue the current lesson." : enrollment.Status == AcceleratorLearningEnrollmentStatus.Completed ? "Learning path completed." : "Start the next required lesson.";
        return new(enrollment.Id, enrollment.LearningPathId, path.Slug, path.Type, enrollment.Status, enrollment.OverallProgressPercent, enrollment.CurrentModuleId, enrollment.CurrentLessonId, next, enrollment.EnrolledAtUtc, enrollment.StartedAtUtc, enrollment.CompletedAtUtc, enrollment.LastActivityAtUtc, enrollment.Revision);
    }
    private static void ValidateEnrollment(EnrollLearningPathRequest request)
    {
        if (request.RequestKey == Guid.Empty || request.CurrentLevel is < 0 or > 5 || request.TargetLevel is < 1 or > 5 || request.TargetLevel < request.CurrentLevel || request.WeeklyCommitmentMinutes is < 15 or > 10080 || request.TargetCompletionAtUtc is { } target && target <= DateTime.UtcNow.Date)
            throw Invalid("Invalid learning enrollment request.");
    }
    private Task AuditAsync(Guid actor, AuditAction action, string entity, Guid id, CancellationToken ct) => audit.AppendAsync(new AuditEvent(action, entity, id.ToString(), Actor: new AuditActor(actor, "Candidate")), ct);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static BadRequestException Invalid(string message) => new(message, "accelerator_invalid_request");
}
