using JobPortal.API.Extensions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

public sealed record CreateTalentShareRequest(Guid GoalId, Guid? JobId, int ValidHours = 24);
public sealed record SubmitTalentRequest(Guid GoalId);
public sealed record EmployerTalentDecisionRequest(string? Message);

[ApiController, Authorize(Roles = "Candidate"), Route("api/accelerator")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorTalentController(AcceleratorTalentService talent) : ControllerBase
{
    [HttpGet("opportunities")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TalentOpportunity>>>> Opportunities(
        [FromQuery] TalentOpportunityQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<TalentOpportunity>>(
            await talent.OpportunitiesAsync(User.GetRequiredUserId(), query, ct)));

    [HttpPost("talent-shares")]
    public async Task<ActionResult<ApiResponse<TalentShareCreated>>> Share(CreateTalentShareRequest request,
        CancellationToken ct) => Ok(new ApiResponse<TalentShareCreated>(
            await talent.CreateShareAsync(User.GetRequiredUserId(), request.GoalId, request.JobId,
                request.ValidHours, ct)));

    [HttpDelete("talent-shares/{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    { await talent.RevokeShareAsync(User.GetRequiredUserId(), id, ct); return NoContent(); }

    [HttpPost("jobs/{jobId:guid}/talent-submit")]
    public async Task<ActionResult<ApiResponse<TalentSubmissionView>>> Submit(Guid jobId,
        SubmitTalentRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<TalentSubmissionView>(await talent.SubmitAsync(
            User.GetRequiredUserId(), jobId, request.GoalId, ct)));

    [HttpGet("talent-submissions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TalentSubmissionView>>>> Submissions(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<TalentSubmissionView>>(
            await talent.CandidateSubmissionsAsync(User.GetRequiredUserId(), page, pageSize, ct)));

    [HttpPost("talent-submissions/{id:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
    { await talent.WithdrawAsync(User.GetRequiredUserId(), id, ct); return NoContent(); }
}

[ApiController, AllowAnonymous, Route("api/accelerator/talent-share")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class PublicTalentShareController(AcceleratorTalentService talent) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ActionResult<ApiResponse<TalentPublicPackage>>> Get(string token, CancellationToken ct) =>
        Ok(new ApiResponse<TalentPublicPackage>(await talent.PublicShareAsync(token, ct)));
}

[ApiController, Authorize(Roles = "Employer"), Route("api/employer/talent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class EmployerTalentController(AcceleratorTalentService talent) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TalentSubmissionView>>>> Inbox(
        [FromQuery] Guid companyId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(new ApiResponse<IReadOnlyList<TalentSubmissionView>>(
            await talent.EmployerInboxAsync(User.GetRequiredUserId(), companyId, page, pageSize, ct)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Detail(Guid id, CancellationToken ct)
    {
        var result = await talent.EmployerDetailAsync(User.GetRequiredUserId(), id, ct);
        return Ok(new ApiResponse<object>(new { result.Submission, result.Package }));
    }

    [HttpPost("{id:guid}/shortlist")]
    public async Task<ActionResult<ApiResponse<TalentSubmissionView>>> Shortlist(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<TalentSubmissionView>(await talent.EmployerDecisionAsync(
            User.GetRequiredUserId(), id, TalentSubmissionStatus.Shortlisted, null, ct)));

    [HttpPost("{id:guid}/request-interview")]
    public async Task<ActionResult<ApiResponse<TalentSubmissionView>>> RequestInterview(Guid id,
        EmployerTalentDecisionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<TalentSubmissionView>(await talent.EmployerDecisionAsync(
            User.GetRequiredUserId(), id, TalentSubmissionStatus.InterviewRequested, request.Message, ct)));

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<ApiResponse<TalentSubmissionView>>> Reject(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<TalentSubmissionView>(await talent.EmployerDecisionAsync(
            User.GetRequiredUserId(), id, TalentSubmissionStatus.Rejected, null, ct)));
}
