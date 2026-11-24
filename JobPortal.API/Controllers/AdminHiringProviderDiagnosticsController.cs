using JobPortal.API.Extensions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController,Authorize(Roles="Administrator"),Route("api/admin/outreach/providers")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),EnableRateLimiting("HiringOutreach")]
public sealed class AdminHiringProviderDiagnosticsController(IHiringProviderDiagnostics diagnostics) : ControllerBase
{
    [HttpPost("coresignal/probe")]
    public async Task<ActionResult<ApiResponse<HiringProviderProbeResult>>> Probe(HiringProviderProbeRequest request,CancellationToken ct)=>
        Ok(new ApiResponse<HiringProviderProbeResult>(await diagnostics.ProbeCoresignalAsync(User.GetRequiredUserId(),request,ct)));
}
