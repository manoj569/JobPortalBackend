using JobPortal.API.Extensions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController,Authorize(Roles="Administrator"),Route("api/admin/outreach")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),EnableRateLimiting("HiringOutreach")]
public sealed class AdminHiringOutreachController(IHiringOutreachService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<HiringSupportSession>>>> Sessions(CancellationToken ct,[FromQuery] int page=1,[FromQuery] int pageSize=20)=>
        Ok(new ApiResponse<IReadOnlyList<HiringSupportSession>>(await service.SupportAsync(User.GetRequiredUserId(),page,pageSize,ct)));
}
