using JobPortal.API.Extensions;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

public sealed record SendTalentOutreachRequest(Guid MailboxConnectionId);

[ApiController, Authorize(Roles = "Candidate"), Route("api/accelerator")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(16384), EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorOutreachController(AcceleratorTalentService talent) : ControllerBase
{
    [HttpGet("jobs/{jobId:guid}/hiring-contacts")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<HiringContactView>>>> Contacts(Guid jobId,
        CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<HiringContactView>>(
            await talent.HiringContactsAsync(User.GetRequiredUserId(), jobId, ct)));

    [HttpPost("jobs/{jobId:guid}/outreach")]
    public async Task<ActionResult<ApiResponse<TalentDraftView>>> Create(Guid jobId,
        CreateTalentDraftRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<TalentDraftView>(await talent.CreateDraftAsync(
            User.GetRequiredUserId(), jobId, request, ct)));

    [HttpPut("outreach/{id:guid}")]
    public async Task<ActionResult<ApiResponse<TalentDraftView>>> Edit(Guid id,
        EditTalentDraftRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<TalentDraftView>(await talent.EditDraftAsync(
            User.GetRequiredUserId(), id, request, ct)));

    [HttpGet("outreach/{id:guid}")]
    public async Task<ActionResult<ApiResponse<TalentDraftView>>> Detail(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<TalentDraftView>(await talent.DraftAsync(User.GetRequiredUserId(), id, ct)));

    [HttpGet("outreach")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TalentDraftView>>>> History(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<TalentDraftView>>(await talent.OutreachHistoryAsync(
            User.GetRequiredUserId(), page, pageSize, ct)));

    [HttpPost("outreach/{id:guid}/send")]
    public async Task<ActionResult<ApiResponse<TalentDraftView>>> Send(Guid id,
        SendTalentOutreachRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken ct) =>
        Ok(new ApiResponse<TalentDraftView>(await talent.SendAsync(
            User.GetRequiredUserId(), id, request.MailboxConnectionId, idempotencyKey, ct)));
}
