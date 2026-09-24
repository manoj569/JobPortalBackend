using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Referrals;
using JobPortal.Application.Features.Referrals;
using JobPortal.Shared.Models;
using JobPortal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/referrals")]
[Produces("application/json")]
public sealed class ReferralsController(
    IJobReferralService referralService,
    IJobUrlExtractionService urlExtractionService) : ControllerBase
{
    /// <summary>Referrer pastes a job URL; AI attempts to pre-fill job fields. Always review/edit
    /// before calling Submit — extraction failures return Succeeded=false with a reason, never an error.</summary>
    [HttpPost("extract-from-url")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ExtractedJobDetails>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ExtractedJobDetails>>> ExtractFromUrl(
        [FromBody] ExtractFromUrlRequest request, CancellationToken cancellationToken)
    {
        var result = await urlExtractionService.ExtractAsync(request.Url, cancellationToken);
        return Ok(new ApiResponse<ExtractedJobDetails>(result));
    }

    /// <summary>Referrer submits a referral for a job they've created (any authenticated user).</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<JobReferralResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<JobReferralResponse>>> Submit(
        [FromBody] SubmitJobReferralRequest request, CancellationToken cancellationToken)
    {
        var referrerUserId = User.GetRequiredUserId();
        var result = await referralService.SubmitAsync(referrerUserId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<JobReferralResponse>(result, "Referral submitted for admin approval."));
    }

    /// <summary>
    /// Public listing of approved referral jobs for the "Referral jobs" browse tab.
    /// No contact details.
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<PublicReferralJobResponse>>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<PublicReferralJobResponse>>>> GetApprovedPublic(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await referralService.GetApprovedPublicAsync(
            pageNumber,
            pageSize,
            User.TryGetUserId(),
            cancellationToken);

        return Ok(new ApiResponse<PagedResponse<PublicReferralJobResponse>>(result));
    }
    /// <summary>
    /// Referrer's own past submissions with search, status filtering, and pagination.
    /// </summary>
    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<JobReferralResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<JobReferralResponse>>>> GetMine(
        [FromQuery] string? search = null,
        [FromQuery] JobReferralApprovalStatus? status = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var referrerUserId = User.GetRequiredUserId();

        var result = await referralService.GetMySubmissionsAsync(
            referrerUserId,
            search,
            status,
            pageNumber,
            pageSize,
            cancellationToken);

        return Ok(new ApiResponse<PagedResponse<JobReferralResponse>>(result));
    }

    /// <summary>Admin queue of referrals awaiting approval.</summary>
    [HttpGet("pending")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<JobReferralResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<JobReferralResponse>>>> GetPending(
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await referralService.GetPendingForAdminAsync(pageNumber, pageSize, cancellationToken);
        return Ok(new ApiResponse<PagedResponse<JobReferralResponse>>(result));
    }

    /// <summary>Admin approves or rejects a pending referral. Approving publishes the underlying job.</summary>
    [HttpPost("{id:guid}/review")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(ApiResponse<JobReferralResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<JobReferralResponse>>> Review(
        Guid id, [FromBody] ReviewJobReferralRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = User.GetRequiredUserId();
        var result = await referralService.ReviewAsync(id, adminUserId, request, cancellationToken);
        return Ok(new ApiResponse<JobReferralResponse>(result, "Referral reviewed."));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Administrator")]
    public Task<ActionResult<ApiResponse<JobReferralResponse>>> Approve(Guid id, CancellationToken cancellationToken) =>
        Review(id, new ReviewJobReferralRequest(JobReferralApprovalStatus.Approved, null), cancellationToken);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Administrator")]
    public Task<ActionResult<ApiResponse<JobReferralResponse>>> Reject(Guid id, [FromBody] string? reason, CancellationToken cancellationToken) =>
        Review(id, new ReviewJobReferralRequest(JobReferralApprovalStatus.Rejected, reason), cancellationToken);

    /// <summary>Job seeker attempts to unlock referrer contact details for a job.
    /// Open to anonymous callers so the response can distinguish "please log in"
    /// from "please subscribe" — access itself is still gated inside the service.</summary>
    [HttpGet("unlock/{jobId:guid}")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<ReferralUnlockResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ReferralUnlockResponse>>> UnlockContact(
        Guid jobId, CancellationToken cancellationToken)
    {
        var seekerUserId = User.TryGetUserId();
        var result = await referralService.UnlockContactAsync(seekerUserId, jobId, cancellationToken);
        return Ok(new ApiResponse<ReferralUnlockResponse>(result));
    }
}
