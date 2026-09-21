using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate"), Route("api/career-guidance")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CareerCandidateTrustController(CareerTrustService service) : ControllerBase
{
    [HttpGet("bookings/{bookingId:guid}/review")]
    public async Task<ActionResult<ApiResponse<CareerReviewResponse>>> Review(Guid bookingId, CancellationToken ct) =>
        Ok(new ApiResponse<CareerReviewResponse>(await service.GetReviewAsync(User.GetRequiredUserId(), bookingId, false, ct)));
    [HttpPost("bookings/{bookingId:guid}/review"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerReviewResponse>>> Submit(Guid bookingId, CareerReviewRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerReviewResponse>(await service.SubmitReviewAsync(User.GetRequiredUserId(), bookingId, request, ct)));
    [HttpPut("bookings/{bookingId:guid}/review"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerReviewResponse>>> Edit(Guid bookingId, CareerReviewRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerReviewResponse>(await service.EditReviewAsync(User.GetRequiredUserId(), bookingId, request, ct)));
    [HttpDelete("bookings/{bookingId:guid}/review"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerReviewResponse>>> Withdraw(Guid bookingId, [FromQuery] Guid revision, CancellationToken ct) =>
        Ok(new ApiResponse<CareerReviewResponse>(await service.WithdrawReviewAsync(User.GetRequiredUserId(), bookingId, revision, ct)));
    [HttpGet("bookings/{bookingId:guid}/dispute")]
    public async Task<ActionResult<ApiResponse<CareerDisputeResponse>>> Dispute(Guid bookingId, CancellationToken ct) =>
        Ok(new ApiResponse<CareerDisputeResponse>(await service.GetDisputeAsync(User.GetRequiredUserId(), bookingId, CareerSessionAudience.Candidate, true, ct)));
    [HttpPost("bookings/{bookingId:guid}/dispute"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerDisputeResponse>>> Open(Guid bookingId, CareerDisputeRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerDisputeResponse>(await service.OpenDisputeAsync(User.GetRequiredUserId(), bookingId, request, ct)));
    [HttpPost("disputes/{id:guid}/evidence"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerDisputeResponse>>> Evidence(Guid id, CareerEvidenceRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerDisputeResponse>(await service.EvidenceAsync(User.GetRequiredUserId(), id, CareerSessionAudience.Candidate, request, ct)));
}

[ApiController, Authorize, Route("api/career-guidance/me/disputes")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CareerConsultantTrustController(CareerTrustService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerDisputeResponse>>>> List([FromQuery] CareerTrustQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<CareerDisputeResponse>>(await service.DisputesAsync(User.GetRequiredUserId(), CareerSessionAudience.Consultant, query, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerDisputeResponse>>> Get(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<CareerDisputeResponse>(await service.GetDisputeAsync(User.GetRequiredUserId(), id, CareerSessionAudience.Consultant, false, ct)));
    [HttpPost("{id:guid}/evidence"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerDisputeResponse>>> Evidence(Guid id, CareerEvidenceRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerDisputeResponse>(await service.EvidenceAsync(User.GetRequiredUserId(), id, CareerSessionAudience.Consultant, request, ct)));
}

[ApiController, AllowAnonymous, Route("api/career-guidance/consultants/{consultantId:guid}/reviews")]
public sealed class CareerPublicReviewsController(CareerTrustService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerPublicReview>>>> Get(Guid consultantId, [FromQuery] CareerTrustQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<CareerPublicReview>>(await service.PublicReviewsAsync(consultantId, query, ct)));
}

[ApiController, Authorize(Roles = "Administrator"), Route("api/admin/career-guidance")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCareerTrustController(CareerTrustService service) : ControllerBase
{
    [HttpGet("reviews")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerReviewResponse>>>> Reviews([FromQuery] CareerTrustQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<CareerReviewResponse>>(await service.ReviewsAsync(User.GetRequiredUserId(), query, ct)));
    [HttpGet("reviews/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerReviewResponse>>> Review(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<CareerReviewResponse>(await service.GetReviewAsync(User.GetRequiredUserId(), id, true, ct)));
    [HttpPost("reviews/{id:guid}/moderation"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerReviewResponse>>> Moderate(Guid id, CareerReviewModerationRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerReviewResponse>(await service.ModerateAsync(User.GetRequiredUserId(), id, request, ct)));
    [HttpGet("disputes")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerDisputeResponse>>>> Disputes([FromQuery] CareerTrustQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<CareerDisputeResponse>>(await service.DisputesAsync(User.GetRequiredUserId(), CareerSessionAudience.Administrator, query, ct)));
    [HttpGet("disputes/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerDisputeResponse>>> Dispute(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<CareerDisputeResponse>(await service.GetDisputeAsync(User.GetRequiredUserId(), id, CareerSessionAudience.Administrator, false, ct)));
    [HttpPost("disputes/{id:guid}/evidence"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerDisputeResponse>>> Evidence(Guid id, CareerEvidenceRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerDisputeResponse>(await service.EvidenceAsync(User.GetRequiredUserId(), id, CareerSessionAudience.Administrator, request, ct)));
    [HttpPost("disputes/{id:guid}/status"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerDisputeResponse>>> Status(Guid id, CareerDisputeStatusRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerDisputeResponse>(await service.StatusAsync(User.GetRequiredUserId(), id, request, ct)));
    [HttpPost("disputes/{id:guid}/resolve"), EnableRateLimiting("CareerTrustWrites")]
    public async Task<ActionResult<ApiResponse<CareerDisputeResponse>>> Resolve(Guid id, CareerDisputeResolveRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerDisputeResponse>(await service.ResolveAsync(User.GetRequiredUserId(), id, request, ct)));
}
