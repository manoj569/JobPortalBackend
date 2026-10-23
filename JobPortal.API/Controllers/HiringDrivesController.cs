using JobPortal.Application.Features.HiringDrives;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/hiring-drives")]
[Produces("application/json")]
public sealed class HiringDrivesController(IHiringDriveService drives) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PublicHiringDriveResponse>>>> Get(CancellationToken cancellationToken) =>
        Ok(new ApiResponse<IReadOnlyCollection<PublicHiringDriveResponse>>(await drives.PublicAsync(cancellationToken)));

    [HttpGet("home")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PublicHiringDriveResponse>>>> Home(CancellationToken cancellationToken) =>
        Ok(new ApiResponse<IReadOnlyCollection<PublicHiringDriveResponse>>(await drives.HomepageAsync(cancellationToken)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<PublicHiringDriveDetailResponse>>> Detail(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PublicHiringDriveDetailResponse>(await drives.PublicDetailAsync(id, cancellationToken)));

    [HttpPost("{id:guid}/click")]
    [EnableRateLimiting("HiringDriveAnalytics")]
    public async Task<ActionResult<ApiResponse<HiringDriveClickResponse>>> Click(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<HiringDriveClickResponse>(await drives.ClickAsync(id, cancellationToken)));

    [HttpPost("impressions")]
    [EnableRateLimiting("HiringDriveAnalytics")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Impressions(HiringDriveImpressionsRequest request, CancellationToken cancellationToken)
    {
        await drives.ImpressionsAsync(request, cancellationToken);
        return NoContent();
    }
}
