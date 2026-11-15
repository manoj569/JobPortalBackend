using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate")]
[Route("api/accelerator/v1/interviews")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(65536), EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorPhase5Controller(AcceleratorPhase5Service service, AcceleratorInterviewAiService ai) : ControllerBase
{
    [HttpGet("recommended")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<InterviewRecommendationResponse>>>> Recommended(
        [FromQuery] Guid goalId, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<InterviewRecommendationResponse>>(
            await service.RecommendationsAsync(User.GetRequiredUserId(), goalId, ct)));

    [HttpGet("versions/{versionId:guid}")]
    public async Task<ActionResult<ApiResponse<InterviewVersionResponse>>> Version(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewVersionResponse>(await service.VersionAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPost("sessions")]
    public async Task<ActionResult<ApiResponse<InterviewSessionResponse>>> Start(StartInterviewRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewSessionResponse>(await service.StartAsync(User.GetRequiredUserId(), request, ct)));

    [HttpGet("sessions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<InterviewSessionResponse>>>> History(
        [FromQuery] Guid goalId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<InterviewSessionResponse>>(
            await service.HistoryAsync(User.GetRequiredUserId(), goalId, page, pageSize, ct)));

    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<ActionResult<ApiResponse<InterviewSessionResponse>>> Session(Guid sessionId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewSessionResponse>(await service.SessionAsync(User.GetRequiredUserId(), sessionId, ct)));

    [HttpGet("sessions/{sessionId:guid}/questions/next")]
    public async Task<ActionResult<ApiResponse<InterviewQuestionResponse?>>> Next(Guid sessionId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewQuestionResponse?>(await service.NextQuestionAsync(User.GetRequiredUserId(), sessionId, ct)));

    [HttpPost("sessions/{sessionId:guid}/sections/{sectionId:guid}/questions/generate")]
    public async Task<ActionResult<ApiResponse<InterviewQuestionResponse>>> GenerateQuestion(Guid sessionId, Guid sectionId,
        GenerateInterviewQuestionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewQuestionResponse>(await ai.GenerateQuestionAsync(User.GetRequiredUserId(),
            sessionId, sectionId, request, ct)));

    [HttpPost("sessions/{sessionId:guid}/answers")]
    public async Task<ActionResult<ApiResponse<InterviewAnswerResponse>>> Answer(Guid sessionId,
        SubmitInterviewAnswerRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewAnswerResponse>(await service.SubmitAnswerAsync(User.GetRequiredUserId(), sessionId, request, ct)));

    [HttpPost("sessions/{sessionId:guid}/answers/{answerId:guid}/evaluate")]
    public async Task<ActionResult<ApiResponse<InterviewEvaluationResponse>>> Evaluate(Guid sessionId, Guid answerId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewEvaluationResponse>(await service.EvaluateDeterministicAsync(User.GetRequiredUserId(), sessionId, answerId, ct)));

    [HttpPost("sessions/{sessionId:guid}/answers/{answerId:guid}/evaluate-ai")]
    public async Task<ActionResult<ApiResponse<InterviewEvaluationResponse>>> EvaluateAi(Guid sessionId, Guid answerId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewEvaluationResponse>(await ai.EvaluateAsync(User.GetRequiredUserId(), sessionId, answerId, ct)));

    [HttpGet("sessions/{sessionId:guid}/answers/{answerId:guid}/evaluation")]
    public async Task<ActionResult<ApiResponse<InterviewEvaluationResponse?>>> Evaluation(Guid sessionId, Guid answerId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewEvaluationResponse?>(await service.EvaluationAsync(User.GetRequiredUserId(), sessionId, answerId, ct)));

    [HttpPost("sessions/{sessionId:guid}/complete")]
    public async Task<ActionResult<ApiResponse<InterviewReportResponse>>> Complete(Guid sessionId,
        CompleteInterviewRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewReportResponse>(await service.CompleteAsync(User.GetRequiredUserId(), sessionId, request, ct)));

    [HttpGet("sessions/{sessionId:guid}/report")]
    public async Task<ActionResult<ApiResponse<InterviewReportResponse>>> Report(Guid sessionId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewReportResponse>(await service.ReportAsync(User.GetRequiredUserId(), sessionId, ct)));
}

[ApiController, Authorize(Roles = "Candidate")]
[Route("api/accelerator/v1/passport")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(65536), EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorPassportController(AcceleratorPassportService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SkillPassportResponse>>> Passport([FromQuery] Guid goalId, CancellationToken ct) =>
        Ok(new ApiResponse<SkillPassportResponse>(await service.PassportAsync(User.GetRequiredUserId(), goalId, ct)));

    [HttpGet("skills/{competencyId:guid}")]
    public async Task<ActionResult<ApiResponse<PassportSkillResponse>>> Skill(Guid competencyId, [FromQuery] Guid goalId,
        CancellationToken ct) =>
        Ok(new ApiResponse<PassportSkillResponse>(await service.SkillAsync(User.GetRequiredUserId(), goalId, competencyId, ct)));

    [HttpGet("readiness")]
    public async Task<ActionResult<ApiResponse<ReadinessResponse>>> Readiness([FromQuery] Guid goalId, CancellationToken ct) =>
        Ok(new ApiResponse<ReadinessResponse>(await service.ReadinessAsync(User.GetRequiredUserId(), goalId, ct)));

    [HttpGet("readiness/history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ReadinessHistoryResponse>>>> History([FromQuery] Guid goalId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<ReadinessHistoryResponse>>(
            await service.ReadinessHistoryAsync(User.GetRequiredUserId(), goalId, page, pageSize, ct)));

    [HttpPost("share")]
    public async Task<ActionResult<ApiResponse<PassportShareResponse>>> Share(PassportShareRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PassportShareResponse>(await service.EnableShareAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPost("share/revoke")]
    public async Task<ActionResult<ApiResponse<PassportShareResponse>>> Revoke(PassportShareRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PassportShareResponse>(await service.RevokeShareAsync(User.GetRequiredUserId(), request, ct)));
}

[ApiController, AllowAnonymous]
[Route("api/accelerator/v1/public/passports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class PublicAcceleratorPassportController(AcceleratorPassportService service) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ActionResult<ApiResponse<PublicSkillPassportResponse>>> View(string token, CancellationToken ct) =>
        Ok(new ApiResponse<PublicSkillPassportResponse>(await service.PublicAsync(token, ct)));
}
