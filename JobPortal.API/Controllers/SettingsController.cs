using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Settings;
using JobPortal.Application.Features.Settings;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController, Authorize]
[Route("api/settings")]
[Produces("application/json")]
public sealed class SettingsController(IAccountSettingsService settings) : ControllerBase
{
    [HttpGet("security-status")]
    public async Task<ActionResult<ApiResponse<AccountSecurityStatusResponse>>> SecurityStatus(CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AccountSecurityStatusResponse>(
            await settings.GetSecurityStatusAsync(User.GetRequiredUserId(), cancellationToken)));
}
