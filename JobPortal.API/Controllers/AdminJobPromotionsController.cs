using JobPortal.API.Extensions;
using JobPortal.Application.Features.JobPromotions;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin")]
[Produces("application/json")]
public sealed class AdminJobPromotionsController(IJobPromotionService promotions) : ControllerBase
{
    [HttpPut("jobs/{jobId:guid}/promotion")]
    public async Task<ActionResult<ApiResponse<JobPromotionConfigurationResponse>>> Save(
        Guid jobId, SaveJobPromotionRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobPromotionConfigurationResponse>(await promotions.SaveAsync(
            User.GetRequiredUserId(), jobId, request, cancellationToken)));

    [HttpGet("jobs/{jobId:guid}/promotion")]
    public async Task<ActionResult<ApiResponse<JobPromotionConfigurationResponse>>> Get(
        Guid jobId, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobPromotionConfigurationResponse>(await promotions.GetAsync(jobId, cancellationToken)));

    [HttpDelete("jobs/{jobId:guid}/promotion")]
    public async Task<ActionResult<ApiResponse<JobPromotionConfigurationResponse>>> Disable(
        Guid jobId, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobPromotionConfigurationResponse>(await promotions.DisableAsync(
            User.GetRequiredUserId(), jobId, cancellationToken)));

    [HttpPut("jobs/{jobId:guid}/promotion/banner")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024 + 64 * 1024)]
    public async Task<ActionResult<ApiResponse<JobPromotionConfigurationResponse>>> UploadBanner(
        Guid jobId, IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return Ok(new ApiResponse<JobPromotionConfigurationResponse>(await promotions.UploadBannerAsync(
            User.GetRequiredUserId(), jobId,
            new JobPromotionBannerUpload(stream, file.Length, file.FileName, file.ContentType), cancellationToken)));
    }

    [HttpGet("job-promotions")]
    public async Task<ActionResult<ApiResponse<PagedResponse<JobPromotionConfigurationResponse>>>> Search(
        [FromQuery] JobPromotionQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<JobPromotionConfigurationResponse>>(
            await promotions.SearchAsync(query, cancellationToken)));
}
