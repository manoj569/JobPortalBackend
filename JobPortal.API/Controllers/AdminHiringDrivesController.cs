using JobPortal.API.Extensions;
using JobPortal.Application.Features.HiringDrives;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/hiring-drives")]
[Produces("application/json")]
public sealed class AdminHiringDrivesController(IHiringDriveService drives) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminHiringDriveResponse>>>> Search([FromQuery] HiringDriveQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<AdminHiringDriveResponse>>(await drives.SearchAsync(query, cancellationToken)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminHiringDriveResponse>>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AdminHiringDriveResponse>(await drives.GetAsync(id, cancellationToken)));
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminHiringDriveResponse>>> Create(SaveHiringDriveRequest request, CancellationToken cancellationToken)
    {
        var result = await drives.SaveAsync(User.GetRequiredUserId(), null, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, new ApiResponse<AdminHiringDriveResponse>(result));
    }
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminHiringDriveResponse>>> Update(Guid id, SaveHiringDriveRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AdminHiringDriveResponse>(await drives.SaveAsync(User.GetRequiredUserId(), id, request, cancellationToken)));
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await drives.DeleteAsync(User.GetRequiredUserId(), id, cancellationToken);
        return NoContent();
    }
}
