using JobPortal.API.Extensions;
using JobPortal.Application.Features.Referrals;
using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize]
[Route("api/referrer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReferrerRequestsController(ReferralMarketplaceService marketplace) : ControllerBase
{
    [HttpGet("referral-requests")]
    public async Task<ActionResult<ApiResponse<PagedResponse<ReferrerRequestResponse>>>> Inbox([FromQuery] ReferralRequestStatus? status = null,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<PagedResponse<ReferrerRequestResponse>>(await marketplace.InboxAsync(User.GetRequiredUserId(), status, pageNumber, pageSize, ct)));
    [HttpGet("referral-requests/{id:guid}")]
    public async Task<ActionResult<ApiResponse<ReferrerRequestResponse>>> Detail(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<ReferrerRequestResponse>(await marketplace.ReferrerDetailAsync(User.GetRequiredUserId(), id, ct)));
    [HttpGet("referral-requests/{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var result = await marketplace.ResumeAsync(User.GetRequiredUserId(), id, ct);
        return File(result.Content, result.ContentType, result.FileName);
    }
    [HttpPost("referral-requests/{id:guid}/accept")]
    public async Task<ActionResult<ApiResponse<ReferrerRequestResponse>>> Accept(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<ReferrerRequestResponse>(await marketplace.AcceptAsync(User.GetRequiredUserId(), id, ct)));
    [HttpPost("referral-requests/{id:guid}/reject")]
    public async Task<ActionResult<ApiResponse<ReferrerRequestResponse>>> Reject(Guid id, [FromBody] RejectReferralRequest input, CancellationToken ct) =>
        Ok(new ApiResponse<ReferrerRequestResponse>(await marketplace.RejectAsync(User.GetRequiredUserId(), id, input, ct)));
    [HttpPost("referral-requests/{id:guid}/mark-referred")]
    public async Task<ActionResult<ApiResponse<ReferrerRequestResponse>>> Submit(Guid id, [FromBody] SubmitReferralRequest input, CancellationToken ct) =>
        Ok(new ApiResponse<ReferrerRequestResponse>(await marketplace.SubmitAsync(User.GetRequiredUserId(), id, input, ct)));
    [HttpGet("metrics")]
    public async Task<ActionResult<ApiResponse<ReferralMetricsResponse>>> Metrics(CancellationToken ct) =>
        Ok(new ApiResponse<ReferralMetricsResponse>(await marketplace.MetricsAsync(User.GetRequiredUserId(), ct)));
}
