using JobPortal.Application.Features.JobPromotions;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/job-promotions")]
[Produces("application/json")]
public sealed class JobPromotionsController(IJobPromotionService promotions) : ControllerBase
{
    [HttpGet("active")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PromotedJobResponse>>>> Active(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<IReadOnlyCollection<PromotedJobResponse>>(
            await promotions.ActiveAsync(cancellationToken)));

    [HttpGet("{jobId:guid}/banner")]
    [Produces("image/jpeg", "image/png", "image/webp")]
    public async Task<IActionResult> Banner(Guid jobId, CancellationToken cancellationToken)
    {
        var banner = await promotions.BannerAsync(jobId, cancellationToken);
        Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        return File(banner.Content, banner.ContentType, enableRangeProcessing: true);
    }
}
