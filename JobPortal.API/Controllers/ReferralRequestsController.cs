using JobPortal.API.Extensions;
using JobPortal.Application.Features.Referrals;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Candidate")]
[Route("api/referral-requests")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReferralRequestsController(ReferralMarketplaceService marketplace) : ControllerBase
{
    [HttpPost("/api/referrals/{referralId:guid}/requests")]
    public async Task<ActionResult<ApiResponse<ReferralRequestResponse>>> Create(Guid referralId, [FromBody] CreateReferralRequest input, CancellationToken ct)
    {
        var result = await marketplace.CreateAsync(User.GetRequiredUserId(), referralId, input, ct);
        return StatusCode(201, new ApiResponse<ReferralRequestResponse>(result));
    }
    [HttpGet("mine")]
    public async Task<ActionResult<ApiResponse<MyReferralRequestsResponse>>> Mine([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<MyReferralRequestsResponse>(await marketplace.MineAsync(User.GetRequiredUserId(), pageNumber, pageSize, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ReferralRequestResponse>>> Detail(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<ReferralRequestResponse>(await marketplace.CandidateDetailAsync(User.GetRequiredUserId(), id, ct)));
    [HttpGet("{id:guid}/contact")]
    public async Task<ActionResult<ApiResponse<ReferrerContactDetailsResponse>>> Contact(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<ReferrerContactDetailsResponse>(await marketplace.ContactAsync(User.GetRequiredUserId(), id, ct)));
    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<ApiResponse<ReferralRequestResponse>>> Confirm(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<ReferralRequestResponse>(await marketplace.ConfirmAsync(User.GetRequiredUserId(), id, ct)));
    [HttpPost("{id:guid}/not-received")]
    public async Task<ActionResult<ApiResponse<ReferralRequestResponse>>> NotReceived(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<ReferralRequestResponse>(await marketplace.NotReceivedAsync(User.GetRequiredUserId(), id, ct)));
}
