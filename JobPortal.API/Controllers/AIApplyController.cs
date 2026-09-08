using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate")]
[Route("api/ai-apply")]
[Produces("application/json")]
public sealed class AIApplyController(IAIApplyService service, IAIApplyAuthorizationService authorization, IExternalJobSiteSessionService externalSessions, IExternalSessionCaptureService captures) : ControllerBase
{
    private Guid UserId => User.GetRequiredUserId();
    [HttpGet("access")] public async Task<ActionResult<ApiResponse<AIApplyAccess>>> Access(CancellationToken ct) => Ok(new ApiResponse<AIApplyAccess>(await authorization.GetAccessAsync(UserId, ct)));
    [HttpGet("profile")] public async Task<ActionResult<ApiResponse<AIApplyProfileResponse>>> Profile(CancellationToken ct) => Ok(new ApiResponse<AIApplyProfileResponse>(await service.GetProfileAsync(UserId, ct)));
    [HttpPut("profile")] public async Task<ActionResult<ApiResponse<AIApplyProfileResponse>>> Profile(UpdateAIApplyProfileRequest request, CancellationToken ct) => Ok(new ApiResponse<AIApplyProfileResponse>(await service.UpdateProfileAsync(UserId, request, ct)));
    [HttpGet("preferences")] public async Task<ActionResult<ApiResponse<AIApplyPreferencesResponse>>> Preferences(CancellationToken ct) => Ok(new ApiResponse<AIApplyPreferencesResponse>(await service.GetPreferencesAsync(UserId, ct)));
    [HttpPut("preferences")] public async Task<ActionResult<ApiResponse<AIApplyPreferencesResponse>>> Preferences(UpdateAIApplyPreferencesRequest request, CancellationToken ct) => Ok(new ApiResponse<AIApplyPreferencesResponse>(await service.UpdatePreferencesAsync(UserId, request, ct)));
    [HttpGet("settings")] public async Task<ActionResult<ApiResponse<AIApplySettingsResponse>>> Settings(CancellationToken ct) => Ok(new ApiResponse<AIApplySettingsResponse>(await service.GetSettingsAsync(UserId, ct)));
    [HttpPut("settings")] public async Task<ActionResult<ApiResponse<AIApplySettingsResponse>>> Settings(UpdateAIApplySettingsRequest request, CancellationToken ct) => Ok(new ApiResponse<AIApplySettingsResponse>(await service.UpdateSettingsAsync(UserId, request, ct)));
    [HttpPost("enable")] public Task<ActionResult<ApiResponse<AIApplySettingsResponse>>> Enable(CancellationToken ct) => Mode(AIApplyControlAction.Enable, ct);
    [HttpPost("disable")] public Task<ActionResult<ApiResponse<AIApplySettingsResponse>>> Disable(CancellationToken ct) => Mode(AIApplyControlAction.Disable, ct);
    [HttpPost("pause")] public Task<ActionResult<ApiResponse<AIApplySettingsResponse>>> Pause(CancellationToken ct) => Mode(AIApplyControlAction.Pause, ct);
    [HttpPost("resume")] public Task<ActionResult<ApiResponse<AIApplySettingsResponse>>> Resume(CancellationToken ct) => Mode(AIApplyControlAction.Resume, ct);
    private async Task<ActionResult<ApiResponse<AIApplySettingsResponse>>> Mode(AIApplyControlAction action, CancellationToken ct) => Ok(new ApiResponse<AIApplySettingsResponse>(await service.SetModeAsync(UserId, action, ct)));
    [HttpGet("rules")] public async Task<ActionResult<ApiResponse<IReadOnlyList<AIApplyRuleResponse>>>> Rules(CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<AIApplyRuleResponse>>(await service.GetRulesAsync(UserId, ct)));
    [HttpPost("rules")] public async Task<ActionResult<ApiResponse<AIApplyRuleResponse>>> CreateRule(UpsertAIApplyRuleRequest request, CancellationToken ct) { var value = await service.CreateRuleAsync(UserId, request, ct); return CreatedAtAction(nameof(Rules), new ApiResponse<AIApplyRuleResponse>(value)); }
    [HttpPut("rules/{id:guid}")] public async Task<ActionResult<ApiResponse<AIApplyRuleResponse>>> UpdateRule(Guid id, UpsertAIApplyRuleRequest request, CancellationToken ct) => Ok(new ApiResponse<AIApplyRuleResponse>(await service.UpdateRuleAsync(UserId, id, request, ct)));
    [HttpDelete("rules/{id:guid}")] public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct) { await service.DeleteRuleAsync(UserId, id, ct); return NoContent(); }
    [HttpPost("applications/queue")] public async Task<ActionResult<ApiResponse<AIApplyApplicationResponse>>> Queue(QueueAIApplyRequest request, CancellationToken ct) { var value = await service.QueueAsync(UserId, request, ct); return CreatedAtAction(nameof(Application), new { id = value.Id }, new ApiResponse<AIApplyApplicationResponse>(value)); }
    [HttpGet("applications")] public async Task<ActionResult<ApiResponse<IReadOnlyList<AIApplyApplicationResponse>>>> Applications([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<AIApplyApplicationResponse>>(await service.GetApplicationsAsync(UserId, fromUtc, toUtc, ct)));
    [HttpGet("applications/{id:guid}")] public async Task<ActionResult<ApiResponse<AIApplyApplicationResponse>>> Application(Guid id, CancellationToken ct) => Ok(new ApiResponse<AIApplyApplicationResponse>(await service.GetApplicationAsync(UserId, id, ct)));
    [HttpPost("applications/{id:guid}/cancel")] public async Task<ActionResult<ApiResponse<AIApplyApplicationResponse>>> Cancel(Guid id, CancellationToken ct) => Ok(new ApiResponse<AIApplyApplicationResponse>(await service.CancelAsync(UserId, id, ct)));
    [HttpPost("applications/{id:guid}/continue"), EnableRateLimiting("AIApplyContinue")] public async Task<ActionResult<ApiResponse<AIApplyApplicationResponse>>> Continue(Guid id, CancellationToken ct) => Ok(new ApiResponse<AIApplyApplicationResponse>(await service.ContinueAsync(UserId, id, ct)));
    [HttpGet("questions")] public async Task<ActionResult<ApiResponse<IReadOnlyList<AIApplyQuestionResponse>>>> Questions([FromQuery] AIApplyQuestionStatus? status, CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<AIApplyQuestionResponse>>(await service.GetQuestionsAsync(UserId, status, ct)));
    [HttpGet("questions/{id:guid}")] public async Task<ActionResult<ApiResponse<AIApplyQuestionResponse>>> Question(Guid id, CancellationToken ct) => Ok(new ApiResponse<AIApplyQuestionResponse>(await service.GetQuestionAsync(UserId, id, ct)));
    [HttpPost("questions/{id:guid}/answer")] public async Task<ActionResult<ApiResponse<AIApplyQuestionResponse>>> Answer(Guid id, AnswerAIApplyQuestionRequest request, CancellationToken ct) => Ok(new ApiResponse<AIApplyQuestionResponse>(await service.AnswerAsync(UserId, id, request, ct)));
    [HttpPost("questions/{id:guid}/skip")] public async Task<ActionResult<ApiResponse<AIApplyQuestionResponse>>> Skip(Guid id, CancellationToken ct) => Ok(new ApiResponse<AIApplyQuestionResponse>(await service.SkipQuestionAsync(UserId, id, ct)));
    [HttpGet("answers")] public async Task<ActionResult<ApiResponse<IReadOnlyList<AIApplyAnswerResponse>>>> Answers(CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<AIApplyAnswerResponse>>(await service.GetAnswersAsync(UserId, ct)));
    [HttpPut("answers/{id:guid}")] public async Task<ActionResult<ApiResponse<AIApplyAnswerResponse>>> UpdateAnswer(Guid id, UpdateAIApplyAnswerRequest request, CancellationToken ct) => Ok(new ApiResponse<AIApplyAnswerResponse>(await service.UpdateAnswerAsync(UserId, id, request, ct)));
    [HttpDelete("answers/{id:guid}")] public async Task<IActionResult> DeleteAnswer(Guid id, CancellationToken ct) { await service.DeleteAnswerAsync(UserId, id, ct); return NoContent(); }
    [HttpGet("dashboard")] public Task<ActionResult<ApiResponse<AIApplyAnalyticsResponse>>> Dashboard(CancellationToken ct) { var now = DateTime.UtcNow; return AnalyticsCore(now.Date, now.Date.AddDays(1), ct); }
    [HttpGet("analytics")] public Task<ActionResult<ApiResponse<AIApplyAnalyticsResponse>>> Analytics([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct) => AnalyticsCore(fromUtc, toUtc, ct);
    private async Task<ActionResult<ApiResponse<AIApplyAnalyticsResponse>>> AnalyticsCore(DateTime from, DateTime to, CancellationToken ct) => Ok(new ApiResponse<AIApplyAnalyticsResponse>(await service.AnalyticsAsync(UserId, from, to, ct)));
    [HttpGet("external-sessions/{site}")] public async Task<ActionResult<ApiResponse<ExternalJobSiteSessionResponse>>> ExternalSession(JobSiteIdentifier site, CancellationToken ct) => Ok(new ApiResponse<ExternalJobSiteSessionResponse>(await externalSessions.GetAsync(UserId, site, ct)));
    [HttpDelete("external-sessions/{id:guid}")] public async Task<IActionResult> RevokeExternalSession(Guid id, CancellationToken ct) { await externalSessions.RevokeAsync(UserId, id, ct); return NoContent(); }
    [HttpPost("external-sessions/{site}/capture"), EnableRateLimiting("ExternalSessionCapture")] public async Task<ActionResult<ApiResponse<ExternalSessionCaptureResponse>>> StartCapture(JobSiteIdentifier site, StartExternalSessionCaptureRequest request, CancellationToken ct) => Ok(new ApiResponse<ExternalSessionCaptureResponse>(await captures.StartAsync(UserId, site, request, ct)));
    [HttpGet("external-sessions/captures/{captureId:guid}"), EnableRateLimiting("ExternalSessionCapture")] public async Task<ActionResult<ApiResponse<ExternalSessionCaptureResponse>>> Capture(Guid captureId, CancellationToken ct) => Ok(new ApiResponse<ExternalSessionCaptureResponse>(await captures.GetAsync(UserId, captureId, ct)));
    [HttpPost("external-sessions/captures/{captureId:guid}/complete"), EnableRateLimiting("ExternalSessionCapture")] public async Task<ActionResult<ApiResponse<CompleteExternalSessionCaptureResponse>>> CompleteCapture(Guid captureId, CancellationToken ct) => Ok(new ApiResponse<CompleteExternalSessionCaptureResponse>(await captures.CompleteAsync(UserId, captureId, ct)));
    [HttpDelete("external-sessions/captures/{captureId:guid}"), EnableRateLimiting("ExternalSessionCapture")] public async Task<IActionResult> CancelCapture(Guid captureId, CancellationToken ct) { await captures.CancelAsync(UserId, captureId, ct); return NoContent(); }
}
