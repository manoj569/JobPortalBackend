using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Administrator")]
[Route("api/admin/accelerator/v1/interviews")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(65536), EnableRateLimiting("CareerAccelerator")]
public sealed class AdminAcceleratorPhase5Controller(AcceleratorInterviewCatalogService catalog,
    AcceleratorPhase5Service interviews, AcceleratorInterviewAiService ai) : ControllerBase
{
    [HttpPost("definitions")]
    public async Task<ActionResult<ApiResponse<Guid>>> Definition(CreateInterviewDefinitionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await catalog.CreateDefinitionAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPost("versions")]
    public async Task<ActionResult<ApiResponse<InterviewDraftResponse>>> Draft(CreateInterviewDraftRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewDraftResponse>(await catalog.CreateDraftAsync(User.GetRequiredUserId(), request, ct)));

    [HttpGet("versions/{versionId:guid}")]
    public async Task<ActionResult<ApiResponse<InterviewAdminVersionResponse>>> Version(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewAdminVersionResponse>(await catalog.AdminVersionAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPut("versions/{versionId:guid}")]
    public async Task<ActionResult<ApiResponse<InterviewDraftResponse>>> Edit(Guid versionId,
        EditInterviewDraftRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewDraftResponse>(await catalog.EditDraftAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPut("versions/{versionId:guid}/competencies")]
    public async Task<ActionResult<ApiResponse<Guid>>> Competency(Guid versionId,
        UpsertInterviewCompetencyRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await catalog.UpsertCompetencyAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPut("versions/{versionId:guid}/sections")]
    public async Task<ActionResult<ApiResponse<Guid>>> Section(Guid versionId,
        UpsertInterviewSectionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await catalog.UpsertSectionAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPut("versions/{versionId:guid}/questions")]
    public async Task<ActionResult<ApiResponse<Guid>>> Question(Guid versionId,
        UpsertInterviewQuestionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await catalog.UpsertQuestionAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPut("versions/{versionId:guid}/criteria")]
    public async Task<ActionResult<ApiResponse<Guid>>> Criterion(Guid versionId,
        UpsertInterviewCriterionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await catalog.UpsertCriterionAsync(User.GetRequiredUserId(), versionId, request, ct)));

    [HttpPost("versions/{versionId:guid}/validate")]
    public async Task<ActionResult<ApiResponse<Guid>>> Validate(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await catalog.ValidateAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPost("versions/{versionId:guid}/publish")]
    public async Task<ActionResult<ApiResponse<Guid>>> Publish(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await catalog.PublishAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPost("versions/{versionId:guid}/retire")]
    public async Task<ActionResult<ApiResponse<Guid>>> Retire(Guid versionId, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await catalog.RetireAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPost("sessions/{sessionId:guid}/answers/{answerId:guid}/evaluate")]
    public async Task<ActionResult<ApiResponse<InterviewEvaluationResponse>>> Evaluate(Guid sessionId, Guid answerId,
        ManualInterviewEvaluationRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewEvaluationResponse>(await interviews.EvaluateManualAsync(User.GetRequiredUserId(),
            sessionId, answerId, request, ct)));

    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<ActionResult<ApiResponse<InterviewSessionResponse>>> Session(Guid sessionId,
        [FromQuery] Guid candidateId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewSessionResponse>(await interviews.AdminSessionAsync(User.GetRequiredUserId(),
            candidateId, sessionId, ct)));

    [HttpGet("sessions/{sessionId:guid}/answers/{answerId:guid}")]
    public async Task<ActionResult<ApiResponse<InterviewAdminAnswerResponse>>> Answer(Guid sessionId, Guid answerId,
        [FromQuery] Guid candidateId, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewAdminAnswerResponse>(await interviews.AdminAnswerAsync(User.GetRequiredUserId(),
            candidateId, sessionId, answerId, ct)));

    [HttpGet("dispatches/unresolved")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DispatchAdminResponse>>>> Unresolved(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<DispatchAdminResponse>>(
            await ai.UnresolvedAsync(User.GetRequiredUserId(), page, pageSize, ct)));

    [HttpPost("dispatches/{dispatchId:guid}/resolve")]
    public async Task<ActionResult<ApiResponse<DispatchAdminResponse>>> Resolve(Guid dispatchId,
        ManualInterviewDispatchResolutionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<DispatchAdminResponse>(await ai.ResolveAsync(User.GetRequiredUserId(), dispatchId, request, ct)));
}
