using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate")]
[Route("api/accelerator/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(131072), EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorLearningPathsController(AcceleratorLearningPathService service, AcceleratorPhase2Service phase2) : ControllerBase
{
    [HttpPost("enrollments/{enrollmentId:guid}/roadmaps")]
    public async Task<ActionResult<ApiResponse<RoadmapResponse>>> GenerateRoadmap(Guid enrollmentId, StartEnrollmentAssessmentRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<RoadmapResponse>(await phase2.GenerateEnrollmentRoadmapAsync(User.GetRequiredUserId(), enrollmentId, request.RequestKey, ct)));

    [HttpGet("enrollments/{enrollmentId:guid}/recommendations")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RecommendationResponse>>>> Recommendations(Guid enrollmentId, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<RecommendationResponse>>(await phase2.EnrollmentRecommendationsAsync(User.GetRequiredUserId(), enrollmentId, ct)));
    [HttpGet("enrollments/{enrollmentId:guid}/gaps")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SkillGapResponse>>>> Gaps(Guid enrollmentId, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<SkillGapResponse>>(await phase2.EnrollmentGapsAsync(User.GetRequiredUserId(), enrollmentId, ct)));
    [HttpPost("enrollments/{enrollmentId:guid}/assessments/{purpose}")]
    public async Task<ActionResult<ApiResponse<AssessmentAttemptResponse>>> StartAssessment(Guid enrollmentId,
        AcceleratorLearningPathAssessmentPurpose purpose, StartEnrollmentAssessmentRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AssessmentAttemptResponse>(await phase2.StartEnrollmentAssessmentAsync(User.GetRequiredUserId(), enrollmentId, purpose, request.RequestKey, ct)));
    [HttpGet("learning-paths")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LearningPathCardResponse>>>> Paths(
        [FromQuery] AcceleratorLearningPathType? type, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<LearningPathCardResponse>>(await service.PathsAsync(User.GetRequiredUserId(), type, page, pageSize, ct)));

    [HttpGet("learning-paths/{slug}")]
    public async Task<ActionResult<ApiResponse<LearningPathDetailResponse>>> Path(string slug, CancellationToken ct) =>
        Ok(new ApiResponse<LearningPathDetailResponse>(await service.PathAsync(User.GetRequiredUserId(), slug, ct)));

    [HttpPost("learning-paths/{pathId:guid}/enrollments")]
    public async Task<ActionResult<ApiResponse<LearningEnrollmentResponse>>> Enroll(Guid pathId,
        EnrollLearningPathRequest request, CancellationToken ct)
    {
        var result = await service.EnrollAsync(User.GetRequiredUserId(), pathId, request, ct);
        return CreatedAtAction(nameof(Enrollment), new { enrollmentId = result.Id }, new ApiResponse<LearningEnrollmentResponse>(result));
    }

    [HttpGet("enrollments/{enrollmentId:guid}")]
    public async Task<ActionResult<ApiResponse<LearningEnrollmentResponse>>> Enrollment(Guid enrollmentId, CancellationToken ct) =>
        Ok(new ApiResponse<LearningEnrollmentResponse>(await service.EnrollmentAsync(User.GetRequiredUserId(), enrollmentId, ct)));

    [HttpGet("enrollments/{enrollmentId:guid}/modules/{moduleId:guid}")]
    public async Task<ActionResult<ApiResponse<LearningModuleResponse>>> Module(Guid enrollmentId, Guid moduleId, CancellationToken ct) =>
        Ok(new ApiResponse<LearningModuleResponse>(await service.ModuleAsync(User.GetRequiredUserId(), enrollmentId, moduleId, ct)));

    [HttpGet("enrollments/{enrollmentId:guid}/modules/{moduleId:guid}/lessons/{lessonId:guid}")]
    public async Task<ActionResult<ApiResponse<LearningLessonResponse>>> Lesson(Guid enrollmentId, Guid moduleId, Guid lessonId, CancellationToken ct) =>
        Ok(new ApiResponse<LearningLessonResponse>(await service.LessonAsync(User.GetRequiredUserId(), enrollmentId, moduleId, lessonId, ct)));

    [HttpPost("enrollments/{enrollmentId:guid}/modules/{moduleId:guid}/lessons/{lessonId:guid}/start")]
    public async Task<ActionResult<ApiResponse<LearningEnrollmentResponse>>> StartLesson(Guid enrollmentId, Guid moduleId,
        Guid lessonId, StartLearningLessonRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<LearningEnrollmentResponse>(await service.StartLessonAsync(User.GetRequiredUserId(), enrollmentId, moduleId, lessonId, request, ct)));

    [HttpPost("enrollments/{enrollmentId:guid}/modules/{moduleId:guid}/lessons/{lessonId:guid}/complete")]
    public async Task<ActionResult<ApiResponse<LearningEnrollmentResponse>>> CompleteLesson(Guid enrollmentId, Guid moduleId,
        Guid lessonId, CompleteLearningLessonRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<LearningEnrollmentResponse>(await service.CompleteLessonAsync(User.GetRequiredUserId(), enrollmentId, moduleId, lessonId, request, ct)));
}
