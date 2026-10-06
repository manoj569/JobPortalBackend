using JobPortal.API.Extensions;
using JobPortal.Application.Features.AIResume;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate")]
[Route("api/ai-resume")]
[Produces("application/json")]
public sealed class AIResumeController(IAIResumeService service) : ControllerBase
{
    private Guid UserId => User.GetRequiredUserId();

    [HttpGet("packages"), AllowAnonymous]
    public ActionResult<ApiResponse<IReadOnlyList<AIResumePackage>>> Packages() =>
        Ok(new ApiResponse<IReadOnlyList<AIResumePackage>>(service.Packages()));

    [HttpGet("credits")]
    public async Task<ActionResult<ApiResponse<AIResumeCreditResponse>>> Credits(CancellationToken ct) =>
        Ok(new ApiResponse<AIResumeCreditResponse>(await service.CreditsAsync(UserId, ct)));

    [HttpPost("sessions")]
    public async Task<ActionResult<ApiResponse<AIResumeSessionResponse>>> CreateSession(
        CreateAIResumeSessionRequest request, CancellationToken ct)
    {
        var result = await service.CreateSessionAsync(UserId, request, ct);
        return StatusCode(StatusCodes.Status201Created, new ApiResponse<AIResumeSessionResponse>(result));
    }

    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<ActionResult<ApiResponse<AIResumeSessionResponse>>> GetSession(Guid sessionId, CancellationToken ct) =>
        Ok(new ApiResponse<AIResumeSessionResponse>(await service.GetSessionAsync(UserId, sessionId, ct)));

    [HttpPost("sessions/{sessionId:guid}/analyze"), EnableRateLimiting("AIResumeAnalysis")]
    public async Task<ActionResult<ApiResponse<AIResumeSessionResponse>>> Analyze(Guid sessionId, CancellationToken ct) =>
        Ok(new ApiResponse<AIResumeSessionResponse>(await service.AnalyzeAsync(UserId, sessionId, ct)));

    [HttpPost("checkout")]
    public async Task<ActionResult<ApiResponse<AIResumeCheckout>>> Checkout(AIResumeCheckoutRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created,
            new ApiResponse<AIResumeCheckout>(await service.CheckoutAsync(UserId, request, ct), "Resume credit checkout created."));

    [HttpGet("purchases/{merchantOrderId}")]
    public async Task<ActionResult<ApiResponse<AIResumeCheckout>>> PurchaseStatus(string merchantOrderId, CancellationToken ct) =>
        Ok(new ApiResponse<AIResumeCheckout>(await service.PhonePeReturnAsync(UserId, merchantOrderId, ct)));

    [HttpPost("sessions/{sessionId:guid}/generate"), EnableRateLimiting("AIResumeGeneration")]
    public async Task<ActionResult<ApiResponse<AIResumeResponse>>> Generate(
        Guid sessionId, AIResumeGenerationRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AIResumeResponse>(await service.GenerateAsync(UserId, sessionId, request, ct)));

    [HttpPost("resumes/{resumeId:guid}/regenerate"), EnableRateLimiting("AIResumeGeneration")]
    public async Task<ActionResult<ApiResponse<AIResumeResponse>>> Regenerate(
        Guid resumeId, AIResumeGenerationRequest request, CancellationToken ct)
    {
        var current = await service.GetResumeAsync(UserId, resumeId, ct);
        return Ok(new ApiResponse<AIResumeResponse>(await service.GenerateAsync(UserId, current.SessionId, request, ct)));
    }

    [HttpGet("resumes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AIResumeHistoryItem>>>> History(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<AIResumeHistoryItem>>(await service.HistoryAsync(UserId, page, pageSize, ct)));

    [HttpGet("resumes/{resumeId:guid}")]
    public async Task<ActionResult<ApiResponse<AIResumeResponse>>> GetResume(Guid resumeId, CancellationToken ct) =>
        Ok(new ApiResponse<AIResumeResponse>(await service.GetResumeAsync(UserId, resumeId, ct)));

    [HttpPatch("resumes/{resumeId:guid}")]
    public async Task<ActionResult<ApiResponse<AIResumeResponse>>> Edit(
        Guid resumeId, AIResumeEditRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AIResumeResponse>(await service.EditAsync(UserId, resumeId, request, ct)));

    [HttpGet("resumes/{resumeId:guid}/download")]
    public async Task<IActionResult> Download(Guid resumeId, [FromQuery] string format = "original", CancellationToken ct = default)
    {
        var result = await service.DownloadAsync(UserId, resumeId, format, ct);
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpPatch("resumes/{resumeId:guid}/replacements/{targetId}")]
    public async Task<ActionResult<ApiResponse<AIResumeResponse>>> ReviewReplacement(Guid resumeId, string targetId,
        AIResumeReplacementReviewRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AIResumeResponse>(await service.ReviewReplacementAsync(UserId, resumeId, targetId, request, ct)));
}
