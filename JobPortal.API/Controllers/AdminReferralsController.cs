using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Referrals;
using JobPortal.Application.Features.Referrals;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/admin/referrals")]
[Authorize(Roles = "Administrator")]
public sealed class AdminReferralsController(IJobReferralService referrals) : ControllerBase
{
    [HttpPost("{referralId:guid}/approve")]
    public async Task<ActionResult<ApiResponse<JobReferralResponse>>> Approve(Guid referralId, CancellationToken ct)
    {
        var result = await referrals.ReviewAsync(referralId, User.GetRequiredUserId(),
            new ReviewJobReferralRequest(JobPortal.Domain.Enums.JobReferralApprovalStatus.Approved, null), ct);
        return Ok(new ApiResponse<JobReferralResponse>(result, "Referral approved."));
    }

    [HttpPost("{referralId:guid}/reject")]
    public async Task<ActionResult<ApiResponse<JobReferralResponse>>> Reject(Guid referralId, [FromBody] string? reason, CancellationToken ct)
    {
        var result = await referrals.ReviewAsync(referralId, User.GetRequiredUserId(),
            new ReviewJobReferralRequest(JobPortal.Domain.Enums.JobReferralApprovalStatus.Rejected, reason), ct);
        return Ok(new ApiResponse<JobReferralResponse>(result, "Referral rejected."));
    }
}
