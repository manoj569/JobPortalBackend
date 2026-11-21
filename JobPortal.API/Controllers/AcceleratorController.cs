using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.Payments;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate")]
[Route("api/accelerator/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(131072), EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorController(AcceleratorService service, AcceleratorQuotaService usage,
    AcceleratorPhase2Service phase2, IPaymentService payments) : ControllerBase
{
    [HttpGet("plan")]
    public ActionResult<ApiResponse<AcceleratorPlanResponse>> Plan() => Ok(new ApiResponse<AcceleratorPlanResponse>(service.Plan()));

    [HttpGet("taxonomy")]
    public async Task<ActionResult<ApiResponse<TaxonomyResponse?>>> Taxonomy([FromQuery] Guid? versionId, CancellationToken ct) =>
        Ok(new ApiResponse<TaxonomyResponse?>(await service.TaxonomyAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpGet("skills")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CanonicalAcceleratorSkill>>>> Skills(
        [FromQuery] string? query, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<CanonicalAcceleratorSkill>>(await service.SkillsAsync(User.GetRequiredUserId(), query, page, pageSize, ct)));

    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<AcceleratorProfileResponse?>>> Profile(CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProfileResponse?>(await service.ProfileAsync(User.GetRequiredUserId(), ct)));

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<AcceleratorProfileResponse>>> SaveProfile(SaveAcceleratorProfileRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorProfileResponse>(await service.SaveProfileAsync(User.GetRequiredUserId(), request, ct)));

    [HttpDelete("profile")]
    public async Task<IActionResult> EraseProfile(CancellationToken ct)
    {
        await service.EraseProfileAsync(User.GetRequiredUserId(), ct);
        return NoContent();
    }

    [HttpGet("career-goals")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CareerGoalResponse>>>> Goals(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<CareerGoalResponse>>(await service.GoalsAsync(User.GetRequiredUserId(), page, pageSize, ct)));

    [HttpGet("career-goals/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerGoalResponse>>> Goal(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<CareerGoalResponse>(await service.GoalAsync(User.GetRequiredUserId(), id, ct)));

    [HttpPost("career-goals")]
    public async Task<ActionResult<ApiResponse<CareerGoalResponse>>> CreateGoal(CreateCareerGoalRequest request, CancellationToken ct)
    {
        var result = await service.CreateGoalAsync(User.GetRequiredUserId(), request, ct);
        return CreatedAtAction(nameof(Goal), new { id = result.Id }, new ApiResponse<CareerGoalResponse>(result));
    }

    [HttpPatch("career-goals/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerGoalResponse>>> ChangeGoal(Guid id, ChangeCareerGoalRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerGoalResponse>(await service.ChangeGoalAsync(User.GetRequiredUserId(), id, request, ct)));

    [HttpGet("subscription")]
    public async Task<ActionResult<ApiResponse<AcceleratorEntitlementResponse>>> Subscription(CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorEntitlementResponse>(await service.EntitlementAsync(User.GetRequiredUserId(), ct)));

    [HttpPost("subscription/checkout")]
    public async Task<ActionResult<ApiResponse<PhonePeCheckoutResponse>>> Checkout(
        AcceleratorCheckoutRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PhonePeCheckoutResponse>(await payments.CreatePhonePeCheckoutAsync(
            User.GetRequiredUserId(), new CreatePaymentOrderRequest(AcceleratorOptions.PlanCode,
                request.RequestKey), null, ct)));

    [HttpGet("subscription/payments/{merchantOrderId}")]
    public async Task<ActionResult<ApiResponse<PhonePeReturnStatusResponse>>> PaymentStatus(
        string merchantOrderId, CancellationToken ct) =>
        Ok(new ApiResponse<PhonePeReturnStatusResponse>(await payments.GetPhonePeStatusAsync(
            User.GetRequiredUserId(), merchantOrderId, ct)));

    [HttpGet("usage")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorUsageHistoryItem>>>> Usage(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorUsageHistoryItem>>(await usage.HistoryAsync(User.GetRequiredUserId(), page, pageSize, ct)));

    [HttpPost("assessments")]
    public async Task<ActionResult<ApiResponse<AssessmentAttemptResponse>>> StartAssessment(CreateAssessmentAttemptRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AssessmentAttemptResponse>(await phase2.StartAttemptAsync(User.GetRequiredUserId(), request, ct)));

    [HttpGet("assessments/{attemptId:guid}")]
    public async Task<ActionResult<ApiResponse<AssessmentAttemptResponse>>> Assessment(Guid attemptId,CancellationToken ct) =>
        Ok(new ApiResponse<AssessmentAttemptResponse>(await phase2.AttemptAsync(User.GetRequiredUserId(),attemptId,ct)));

    [HttpGet("journeys/{journeyContextId:guid}/assessments/current")]
    public async Task<ActionResult<ApiResponse<AssessmentAttemptResponse?>>> CurrentAssessment(Guid journeyContextId,CancellationToken ct) =>
        Ok(new ApiResponse<AssessmentAttemptResponse?>(await phase2.CurrentAttemptAsync(User.GetRequiredUserId(),journeyContextId,ct)));

    [HttpPost("career-goals/{goalId:guid}/assessments")]
    public async Task<ActionResult<ApiResponse<AssessmentAttemptResponse>>> StartGoalAssessment(Guid goalId,StartResolvedAssessmentRequest request,CancellationToken ct) =>
        Ok(new ApiResponse<AssessmentAttemptResponse>(await phase2.StartGoalAssessmentAsync(User.GetRequiredUserId(),goalId,request.RequestKey,ct)));

    [HttpPost("journeys/{journeyContextId:guid}/assessments")]
    public async Task<ActionResult<ApiResponse<AssessmentAttemptResponse>>> StartJourneyAssessment(Guid journeyContextId,StartResolvedAssessmentRequest request,CancellationToken ct) =>
        Ok(new ApiResponse<AssessmentAttemptResponse>(await phase2.StartJourneyAssessmentAsync(User.GetRequiredUserId(),journeyContextId,request.RequestKey,ct)));

    [HttpPut("assessments/{attemptId:guid}/answers")]
    public async Task<ActionResult<ApiResponse<AssessmentAttemptResponse>>> SubmitAnswer(Guid attemptId, SubmitAssessmentAnswerRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AssessmentAttemptResponse>(await phase2.SubmitAnswerAsync(User.GetRequiredUserId(), attemptId, request, ct)));

    [HttpPost("assessments/{attemptId:guid}/finalize")]
    public async Task<ActionResult<ApiResponse<AssessmentAttemptResponse>>> FinalizeAssessment(Guid attemptId, FinalizeAssessmentRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AssessmentAttemptResponse>(await phase2.FinalizeAttemptAsync(User.GetRequiredUserId(), attemptId, request, ct)));

    [HttpGet("career-goals/{goalId:guid}/gaps")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SkillGapResponse>>>> Gaps(Guid goalId, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<SkillGapResponse>>(await phase2.GapsAsync(User.GetRequiredUserId(), goalId, ct)));

    [HttpPost("roadmaps")]
    public async Task<ActionResult<ApiResponse<RoadmapResponse>>> GenerateRoadmap(GenerateRoadmapRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<RoadmapResponse>(await phase2.GenerateRoadmapAsync(User.GetRequiredUserId(), request, ct)));

    [HttpGet("roadmaps/{roadmapId:guid}")]
    public async Task<ActionResult<ApiResponse<RoadmapResponse>>> Roadmap(Guid roadmapId, CancellationToken ct) =>
        Ok(new ApiResponse<RoadmapResponse>(await phase2.RoadmapAsync(User.GetRequiredUserId(), roadmapId, ct)));

    [HttpPost("roadmaps/{roadmapId:guid}/recover-missed-days")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LearningActivityResponse>>>> RecoverMissedDays(Guid roadmapId, RecoverMissedScheduleRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<LearningActivityResponse>>(await phase2.RecoverMissedScheduleAsync(User.GetRequiredUserId(), roadmapId, request, ct)));

    [HttpPost("learning-activities/{activityId:guid}/progress")]
    public async Task<ActionResult<ApiResponse<LearningActivityResponse>>> Progress(Guid activityId, RecordLearningProgressRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<LearningActivityResponse>(await phase2.RecordProgressAsync(User.GetRequiredUserId(), activityId, request, ct)));

    [HttpGet("career-goals/{goalId:guid}/recommendations")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RecommendationResponse>>>> Recommendations(Guid goalId, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<RecommendationResponse>>(await phase2.RecommendationsAsync(User.GetRequiredUserId(), goalId, ct)));
}
