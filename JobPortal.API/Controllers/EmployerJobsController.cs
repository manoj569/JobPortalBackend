using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.EmployerJobs;
using JobPortal.Application.Features.EmployerJobs;
using JobPortal.Application.Features.Jobs;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Employer")]
[Route("api/employer/jobs")]
[Produces("application/json")]
public sealed class EmployerJobsController(
    IEmployerJobService jobs,
    IOutputCacheStore outputCache)
    : ControllerBase
{
    private Guid EmployerUserId =>
        User.GetRequiredUserId();

    [HttpGet]
    public async Task<
        ActionResult<ApiResponse<PagedResponse<JobResponse>>>> Search(
        [FromQuery] EmployerJobSearchQuery query,
        CancellationToken cancellationToken) =>
        Ok(
            new ApiResponse<PagedResponse<JobResponse>>(
                await jobs.SearchAsync(
                    EmployerUserId,
                    query,
                    cancellationToken)));

    [HttpGet("{id:guid}")]
    public async Task<
        ActionResult<ApiResponse<JobResponse>>> Get(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(
            new ApiResponse<JobResponse>(
                await jobs.GetAsync(
                    EmployerUserId,
                    id,
                    cancellationToken)));

    [HttpPost]
    public async Task<
        ActionResult<ApiResponse<JobResponse>>> Create(
        EmployerCreateJobRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await jobs.CreateAsync(
                EmployerUserId,
                request,
                cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new ApiResponse<JobResponse>(
                result,
                "Job draft created successfully."));
    }

    [HttpPut("{id:guid}")]
    public async Task<
        ActionResult<ApiResponse<JobResponse>>> Update(
        Guid id,
        EmployerUpdateJobRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await jobs.UpdateAsync(
                EmployerUserId,
                id,
                request,
                cancellationToken);

        await InvalidatePublicJobsAsync(
            cancellationToken);

        return Ok(
            new ApiResponse<JobResponse>(
                result,
                "Job updated successfully."));
    }

    [HttpPost("{id:guid}/publish")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Publish(
        Guid id,
        CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobs.PublishAsync(
                EmployerUserId,
                id,
                cancellationToken),
            "Job published successfully.",
            cancellationToken);

    [HttpPost("{id:guid}/pause")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Pause(
        Guid id,
        CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobs.PauseAsync(
                EmployerUserId,
                id,
                cancellationToken),
            "Job paused successfully.",
            cancellationToken);

    [HttpPost("{id:guid}/close")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Close(
        Guid id,
        CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobs.CloseAsync(
                EmployerUserId,
                id,
                cancellationToken),
            "Job closed successfully.",
            cancellationToken);

    [HttpPost("{id:guid}/archive")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Archive(
        Guid id,
        CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobs.ArchiveAsync(
                EmployerUserId,
                id,
                cancellationToken),
            "Job archived successfully.",
            cancellationToken);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await jobs.DeleteAsync(
            EmployerUserId,
            id,
            cancellationToken);

        await InvalidatePublicJobsAsync(
            cancellationToken);

        return NoContent();
    }

    private async Task<
        ActionResult<ApiResponse<JobResponse>>> ExecuteStateChange(
        Func<Task<JobResponse>> operation,
        string message,
        CancellationToken cancellationToken)
    {
        var result = await operation();

        await InvalidatePublicJobsAsync(
            cancellationToken);

        return Ok(
            new ApiResponse<JobResponse>(
                result,
                message));
    }

    private ValueTask InvalidatePublicJobsAsync(
        CancellationToken cancellationToken) =>
        outputCache.EvictByTagAsync(
            "public-jobs",
            cancellationToken);
}
