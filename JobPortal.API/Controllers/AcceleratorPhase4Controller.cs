using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate")]
[Route("api/accelerator/v1/projects")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(65536), EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorPhase4Controller(AcceleratorPhase4Service service, AcceleratorProjectAiService projectAi) : ControllerBase
{
    [HttpGet("~/api/accelerator/v1/learning/enrollments/{enrollmentId:guid}/projects/recommended")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorProjectRecommendation>>>> EnrollmentRecommendations(Guid enrollmentId, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorProjectRecommendation>>(await service.RecommendAsync(User.GetRequiredUserId(),Guid.Empty,ct,enrollmentId)));
    [HttpGet("recommended")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorProjectRecommendation>>>> Recommended([FromQuery] Guid goalId, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorProjectRecommendation>>(await service.RecommendAsync(User.GetRequiredUserId(), goalId, ct)));

    [HttpGet("{versionId:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectDetailResponse>>> Detail(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectDetailResponse>(await service.CatalogDetailAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPost("{versionId:guid}/start")]
    public async Task<ActionResult<ApiResponse<CandidateAcceleratorProjectResponse>>> Start(Guid versionId, StartAcceleratorProjectRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CandidateAcceleratorProjectResponse>(await service.StartProjectAsync(User.GetRequiredUserId(), request with { ProjectVersionId = versionId }, ct)));

    [HttpGet("mine")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CandidateAcceleratorProjectResponse>>>> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? journeyContextId=null, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<CandidateAcceleratorProjectResponse>>(await service.ProjectsAsync(User.GetRequiredUserId(), page, pageSize, ct,journeyContextId)));

    [HttpGet("mine/{projectId:guid}")]
    public async Task<ActionResult<ApiResponse<CandidateAcceleratorProjectResponse>>> MineDetail(Guid projectId, CancellationToken ct) =>
        Ok(new ApiResponse<CandidateAcceleratorProjectResponse>(await service.ProjectAsync(User.GetRequiredUserId(), projectId, ct)));

    [HttpGet("mine/{projectId:guid}/progress")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectProgressResponse>>> Progress(Guid projectId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectProgressResponse>(await service.ProgressAsync(User.GetRequiredUserId(), projectId, ct)));

    [HttpGet("mine/{projectId:guid}/version")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectDetailResponse>>> MyVersion(Guid projectId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectDetailResponse>(await service.CandidateVersionAsync(User.GetRequiredUserId(), projectId, ct)));

    [HttpGet("mine/{projectId:guid}/milestones/{milestoneId:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectMilestoneResponse>>> Milestone(Guid projectId, Guid milestoneId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectMilestoneResponse>(await service.CandidateMilestoneAsync(User.GetRequiredUserId(), projectId, milestoneId, ct)));

    [HttpGet("mine/{projectId:guid}/assignments/{assignmentId:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectAssignmentResponse>>> Assignment(Guid projectId, Guid assignmentId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectAssignmentResponse>(await service.CandidateAssignmentAsync(User.GetRequiredUserId(), projectId, assignmentId, ct)));

    [HttpPost("mine/{projectId:guid}/submissions")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectSubmissionResponse>>> Submit(Guid projectId, SubmitAcceleratorAssignmentRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectSubmissionResponse>(await service.SubmitAsync(User.GetRequiredUserId(), projectId, request, ct)));

    [HttpPost("mine/{projectId:guid}/submissions/{submissionId:guid}/evaluate")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectEvaluationResponse>>> Evaluate(Guid projectId, Guid submissionId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectEvaluationResponse>(await service.EvaluateDeterministicAsync(User.GetRequiredUserId(), projectId, submissionId, ct)));

    [HttpPost("mine/{projectId:guid}/submissions/{submissionId:guid}/evaluate-ai")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectEvaluationResponse>>> EvaluateAi(Guid projectId, Guid submissionId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectEvaluationResponse>(await projectAi.EvaluateAsync(User.GetRequiredUserId(), projectId, submissionId, ct)));

    [HttpGet("mine/{projectId:guid}/submissions/{submissionId:guid}/evaluation")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectEvaluationResponse?>>> Evaluation(Guid projectId, Guid submissionId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectEvaluationResponse?>(await service.EvaluationAsync(User.GetRequiredUserId(), projectId, submissionId, ct)));

    [HttpGet("mine/{projectId:guid}/submissions/{submissionId:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorProjectSubmissionResponse>>> Submission(Guid projectId, Guid submissionId, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProjectSubmissionResponse>(await service.SubmissionAsync(User.GetRequiredUserId(), projectId, submissionId, ct)));

    [HttpGet("mine/{projectId:guid}/history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorProjectHistoryEntry>>>> History(
        Guid projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorProjectHistoryEntry>>(await service.HistoryAsync(User.GetRequiredUserId(), projectId, page, pageSize, ct)));

    [HttpPatch("mine/{projectId:guid}/status")]
    public async Task<ActionResult<ApiResponse<CandidateAcceleratorProjectResponse>>> Status(Guid projectId, ChangeAcceleratorProjectStatusRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CandidateAcceleratorProjectResponse>(await service.ChangeStatusAsync(User.GetRequiredUserId(), projectId, request, ct)));
}
