using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController,Authorize(Roles="Candidate"),Route("api/accelerator/v1/journeys/provision")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),EnableRateLimiting("CareerAccelerator"),RequestSizeLimit(4096)]
public sealed class AcceleratorJourneyProvisioningController(AcceleratorJourneyProvisioningService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProvisionAcceleratorJourneyResponse>>> Provision(ProvisionAcceleratorJourneyRequest request,CancellationToken ct) =>
        Ok(new ApiResponse<ProvisionAcceleratorJourneyResponse>(await service.ProvisionAsync(User.GetRequiredUserId(),request,ct)));
}
