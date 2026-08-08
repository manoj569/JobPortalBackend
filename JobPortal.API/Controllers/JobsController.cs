using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.Jobs;
using JobPortal.API.Extensions;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/jobs")]
[Produces("application/json")]
public sealed class JobsController(
    IJobService jobService,
    IOutputCacheStore outputCache) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<JobResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<JobResponse>>>> Search(
        [FromQuery] JobSearchQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<JobResponse>>(await jobService.SearchAsync(query, cancellationToken)));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<JobResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobResponse>>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobResponse>(await jobService.GetByIdAsync(id, cancellationToken)));

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<JobResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<JobResponse>>> Create(
        [FromBody] CreateJobRequest request, CancellationToken cancellationToken)
    {
        var job = await jobService.CreateAsync(request, cancellationToken);
        await InvalidatePublicJobsAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = job.Id },
            new ApiResponse<JobResponse>(job, "Job created successfully."));
    }

    [HttpPost("compose")]
    [ProducesResponseType(typeof(ApiResponse<ComposeJobResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ComposeJobResponse>>> Compose(
        [FromBody] ComposeJobRequest request, CancellationToken cancellationToken)
    {
        var result = await jobService.ComposeAsync(User.GetRequiredUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<ComposeJobResponse>(result, "Draft job composed successfully."));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<JobResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<JobResponse>>> Update(
        Guid id, [FromBody] UpdateJobRequest request, CancellationToken cancellationToken)
    {
        var job = await jobService.UpdateAsync(id, request, cancellationToken);
        await InvalidatePublicJobsAsync(cancellationToken);
        return Ok(new ApiResponse<JobResponse>(job, "Job updated successfully."));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        await jobService.SoftDeleteAsync(id, cancellationToken);
        await InvalidatePublicJobsAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/permanent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePermanently(Guid id, CancellationToken cancellationToken)
    {
        await jobService.DeletePermanentlyAsync(id, cancellationToken);
        await InvalidatePublicJobsAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/publish")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Publish(Guid id, CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobService.PublishAsync(id, cancellationToken),
            "Job published successfully.",
            cancellationToken);

    [HttpPost("{id:guid}/unpublish")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Unpublish(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobService.UnpublishAsync(id, cancellationToken),
            "Job unpublished successfully.",
            cancellationToken);

    [HttpPost("{id:guid}/close")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Close(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobService.CloseAsync(id, cancellationToken),
            "Job closed successfully.",
            cancellationToken);

    [HttpPost("{id:guid}/archive")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Archive(Guid id, CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobService.ArchiveAsync(id, cancellationToken),
            "Job archived successfully.",
            cancellationToken);

    [HttpPost("{id:guid}/feature")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Feature(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobService.SetFeaturedAsync(id, true, cancellationToken),
            "Job featured successfully.",
            cancellationToken);

    [HttpPost("{id:guid}/unfeature")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Unfeature(
        Guid id, CancellationToken cancellationToken) =>
        ExecuteStateChange(
            () => jobService.SetFeaturedAsync(id, false, cancellationToken),
            "Job unfeatured successfully.",
            cancellationToken);

    [HttpPut("{id:guid}/featured")]
    public Task<ActionResult<ApiResponse<JobResponse>>> SetFeatured(
        Guid id, [FromBody] SetJobFlagRequest request, CancellationToken cancellationToken) =>
        ExecuteStateChange(() => jobService.SetFeaturedAsync(id, request.Value, cancellationToken),
            request.Value ? "Job featured successfully." : "Job unfeatured successfully.",
            cancellationToken);

    [HttpPut("{id:guid}/hidden")]
    public Task<ActionResult<ApiResponse<JobResponse>>> SetHidden(
        Guid id, [FromBody] SetJobFlagRequest request, CancellationToken cancellationToken) =>
        ExecuteStateChange(() => jobService.SetHiddenAsync(id, request.Value, cancellationToken),
            request.Value ? "Job hidden successfully." : "Job made visible successfully.",
            cancellationToken);
    [HttpGet("{id:guid}/recruiter-contact")]
    [ProducesResponseType(
    typeof(ApiResponse<AdminRecruiterContactResponse>),
    StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminRecruiterContactResponse>>> GetRecruiterContact(
    Guid id,
    CancellationToken cancellationToken) =>
    Ok(new ApiResponse<AdminRecruiterContactResponse>(
        await jobService.GetRecruiterContactAsync(id, cancellationToken)));

    [HttpPut("{id:guid}/recruiter-contact")]
    [ProducesResponseType(
        typeof(ApiResponse<AdminRecruiterContactResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminRecruiterContactResponse>>> UpdateRecruiterContact(
        Guid id,
        [FromBody] UpdateRecruiterContactRequest request,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AdminRecruiterContactResponse>(
            await jobService.UpdateRecruiterContactAsync(id, request, cancellationToken),
            "Recruiter contact updated successfully."));
    private async Task<ActionResult<ApiResponse<JobResponse>>> ExecuteStateChange(
        Func<Task<JobResponse>> operation, string message, CancellationToken cancellationToken)
    {
        var job = await operation();
        await InvalidatePublicJobsAsync(cancellationToken);
        return Ok(new ApiResponse<JobResponse>(job, message));
    }

    private ValueTask InvalidatePublicJobsAsync(CancellationToken cancellationToken) =>
        outputCache.EvictByTagAsync("public-jobs", cancellationToken);
}

public sealed record SetJobFlagRequest(bool Value);
