using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles="Candidate"), Route("api/accelerator/v1/journey")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None), EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorJourneyController(AcceleratorJourneySummaryService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<AcceleratorJourneySummaryResponse>>> Get([FromQuery] Guid? marketPathId=null,[FromQuery] Guid? journeyContextId=null,[FromQuery] Guid? marketSkillPathId=null,CancellationToken ct=default) =>
        Ok(new ApiResponse<AcceleratorJourneySummaryResponse>(await service.GetAsync(User.GetRequiredUserId(),marketPathId,journeyContextId,ct,marketSkillPathId)));
}
