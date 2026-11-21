using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate")]
[Route("api/accelerator/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(131072), EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorPhase3Controller(AcceleratorPhase3Service service, AcceleratorQuotaService quota) : ControllerBase
{
    [HttpPost("learning/enrollments/{enrollmentId:guid}/tutor/conversations")]
    public async Task<ActionResult<ApiResponse<TutorConversationResponse>>> CreateEnrollmentConversation(Guid enrollmentId, StartEnrollmentTutorRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<TutorConversationResponse>(await service.StartConversationAsync(User.GetRequiredUserId(),
            new(request.RequestKey, Guid.Empty, request.CompetencyId, request.RoadmapNodeId, request.Topic, request.Title, enrollmentId, request.ModuleId, request.LessonId), ct)));

    [HttpPost("learning/enrollments/{enrollmentId:guid}/practice/sessions")]
    public async Task<ActionResult<ApiResponse<PracticeSessionResponse>>> CreateEnrollmentPractice(Guid enrollmentId, StartEnrollmentPracticeRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PracticeSessionResponse>(await service.StartPracticeAsync(User.GetRequiredUserId(),
            new(request.RequestKey, Guid.Empty, request.CompetencyId, request.RoadmapNodeId, request.Difficulty, request.QuestionType, enrollmentId, request.ModuleId, request.LessonId), ct)));

    [HttpGet("tutor/conversations/{id:guid}")]
    public async Task<ActionResult<ApiResponse<TutorConversationResponse>>> Conversation(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<TutorConversationResponse>(await service.ConversationAsync(User.GetRequiredUserId(), id, ct)));
    [HttpPost("tutor/conversations")]
    public async Task<ActionResult<ApiResponse<TutorConversationResponse>>> CreateConversation(StartTutorConversationRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<TutorConversationResponse>(await service.StartConversationAsync(User.GetRequiredUserId(), request, ct)));

    [HttpGet("tutor/conversations")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TutorConversationResponse>>>> Conversations([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? journeyContextId = null, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<TutorConversationResponse>>(await service.ConversationsAsync(User.GetRequiredUserId(), page, pageSize, ct, journeyContextId)));

    [HttpGet("tutor/conversations/{id:guid}/messages")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TutorMessageResponse>>>> Messages(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<TutorMessageResponse>>(await service.MessagesAsync(User.GetRequiredUserId(), id, ct)));

    [HttpPost("tutor/conversations/{id:guid}/messages")]
    public async Task<ActionResult<ApiResponse<TutorMessageResponse>>> Send(Guid id, SendTutorMessageRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<TutorMessageResponse>(await service.SendMessageAsync(User.GetRequiredUserId(), id, request, ct)));

    [HttpPost("tutor/conversations/{id:guid}/archive")]
    public async Task<ActionResult<ApiResponse<TutorConversationResponse>>> Archive(Guid id, [FromQuery] Guid expectedRevision, CancellationToken ct) =>
        Ok(new ApiResponse<TutorConversationResponse>(await service.ArchiveConversationAsync(User.GetRequiredUserId(), id, expectedRevision, ct)));

    [HttpGet("tutor/usage")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorUsageHistoryItem>>>> TutorUsage([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorUsageHistoryItem>>(await quota.HistoryAsync(User.GetRequiredUserId(), page, pageSize, ct)));

    [HttpPost("practice/sessions")]
    public async Task<ActionResult<ApiResponse<PracticeSessionResponse>>> StartPractice(StartPracticeSessionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PracticeSessionResponse>(await service.StartPracticeAsync(User.GetRequiredUserId(), request, ct)));

    [HttpGet("practice/sessions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PracticeSessionResponse>>>> PracticeHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? journeyContextId = null, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<PracticeSessionResponse>>(await service.PracticeHistoryAsync(User.GetRequiredUserId(), page, pageSize, ct, journeyContextId)));

    [HttpGet("practice/sessions/{id:guid}")]
    public async Task<ActionResult<ApiResponse<PracticeSessionResponse>>> PracticeSession(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<PracticeSessionResponse>(await service.PracticeSessionAsync(User.GetRequiredUserId(), id, ct)));

    [HttpPost("practice/sessions/{id:guid}/answers")]
    public async Task<ActionResult<ApiResponse<PracticeEvaluationResponse>>> PracticeAnswer(Guid id, SubmitPracticeAnswerRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PracticeEvaluationResponse>(await service.SubmitPracticeAnswerAsync(User.GetRequiredUserId(), id, request, ct)));

    [HttpGet("practice/sessions/{id:guid}/results")]
    public async Task<ActionResult<ApiResponse<PracticeResultResponse>>> PracticeResults(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<PracticeResultResponse>(await service.PracticeResultsAsync(User.GetRequiredUserId(), id, ct)));

    [HttpGet("practice/recommendations")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PracticeRecommendationResponse>>>> Recommendations([FromQuery] Guid? journeyContextId = null, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<PracticeRecommendationResponse>>(await service.PracticeRecommendationsAsync(User.GetRequiredUserId(), ct, journeyContextId)));
}
