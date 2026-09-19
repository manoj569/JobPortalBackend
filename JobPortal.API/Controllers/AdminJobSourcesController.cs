using JobPortal.Application.Abstractions.AdminManagement;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/job-sources")]
[Produces("application/json")]
public sealed class AdminJobSourcesController(IJobSourceManagementService sources) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<JobSourceResponse>>>> Search(
        [FromQuery] JobSourceSearchQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<JobSourceResponse>>(await sources.SearchAsync(query, cancellationToken)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<JobSourceResponse>>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobSourceResponse>(await sources.GetByIdAsync(id, cancellationToken)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<JobSourceResponse>>> Create(
        [FromBody] SaveJobSourceRequest request, CancellationToken cancellationToken)
    {
        var result = await sources.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id },
            new ApiResponse<JobSourceResponse>(result, "Job source created successfully."));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<JobSourceResponse>>> Update(
        Guid id, [FromBody] SaveJobSourceRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobSourceResponse>(await sources.UpdateAsync(id, request, cancellationToken),
            "Job source updated successfully."));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sources.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/run")]
    public async Task<ActionResult<ApiResponse<JobSourceRunResult>>> Run(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobSourceRunResult>(await sources.RunAsync(id, cancellationToken)));
}
