using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController,Authorize(Roles="Administrator"),Route("api/admin/accelerator/v1/readiness")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),EnableRateLimiting("CareerAccelerator"),RequestSizeLimit(4096)]
public sealed class AdminAcceleratorReadinessController(AcceleratorCatalogReadinessService service) : ControllerBase
{
    [HttpGet("roles")]
    public async Task<ActionResult<ApiResponse<AcceleratorReadinessPage>>> Roles([FromQuery] AcceleratorReadinessQuery query,CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorReadinessPage>(await service.RolesAsync(User.GetRequiredUserId(),query,ct)));
    [HttpGet("roles/{roleId:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorRoleReadiness>>> Role(Guid roleId,CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorRoleReadiness>(await service.RoleAsync(User.GetRequiredUserId(),roleId,ct)));
    [HttpGet("market-paths/{marketPathId:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorMarketReadiness>>> Market(Guid marketPathId,CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorMarketReadiness>(await service.MarketAsync(User.GetRequiredUserId(),marketPathId,ct)));
    [HttpGet("launch-roles")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorLaunchReadiness>>>> Launch([FromQuery] Guid taxonomyId,CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorLaunchReadiness>>(await service.LaunchAsync(User.GetRequiredUserId(),taxonomyId,ct)));
    [HttpPost("catalog/bootstrap")]
    public async Task<ActionResult<ApiResponse<AcceleratorCatalogBootstrapResult>>> Bootstrap(AcceleratorCatalogBootstrapRequest request,CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorCatalogBootstrapResult>(await service.BootstrapAsync(User.GetRequiredUserId(),request,ct)));
}
