using JobPortal.API.Extensions;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate"), Route("api/accelerator/mailbox")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorMailboxController(AcceleratorTalentService talent) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CandidateMailboxView>>>> Connections(
        CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<CandidateMailboxView>>(
            await talent.MailboxesAsync(User.GetRequiredUserId(), ct)));

    [HttpPost("connect/google")]
    public ActionResult<ApiResponse<string>> ConnectGoogle() =>
        Ok(new ApiResponse<string>(talent.BeginMailboxConnect(User.GetRequiredUserId(), "google")));

    [HttpPost("connect/microsoft")]
    public ActionResult<ApiResponse<string>> ConnectMicrosoft() =>
        Ok(new ApiResponse<string>(talent.BeginMailboxConnect(User.GetRequiredUserId(), "microsoft")));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct)
    { await talent.DisconnectMailboxAsync(User.GetRequiredUserId(), id, ct); return NoContent(); }
}
