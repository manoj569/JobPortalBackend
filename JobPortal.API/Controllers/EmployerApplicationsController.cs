using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.EmployerApplications;
using JobPortal.Application.Features.AdminApplications;
using JobPortal.Application.Features.EmployerApplications;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Employer")]
[Route("api/employer/applications")]
[Produces("application/json")]
public sealed class EmployerApplicationsController(
    IEmployerApplicationService applications)
    : ControllerBase
{
    private Guid EmployerUserId =>
        User.GetRequiredUserId();

    [HttpGet]
    public async Task<
        ActionResult<ApiResponse<
            PagedResponse<AdminApplicationListItem>>>> Search(
        [FromQuery] EmployerApplicationQuery query,
        CancellationToken cancellationToken)
    {
        var result =
            await applications.SearchAsync(
                EmployerUserId,
                query,
                cancellationToken);

        return Ok(
            new ApiResponse<
                PagedResponse<AdminApplicationListItem>>(
                result));
    }

    [HttpGet("{applicationId:guid}")]
    public async Task<
        ActionResult<ApiResponse<AdminApplicationDetail>>> Get(
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var result =
            await applications.GetAsync(
                EmployerUserId,
                applicationId,
                cancellationToken);

        return Ok(
            new ApiResponse<AdminApplicationDetail>(
                result));
    }

    [HttpGet("{applicationId:guid}/resume")]
    public async Task<IActionResult> DownloadResume(
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var result =
            await applications.DownloadResumeAsync(
                EmployerUserId,
                applicationId,
                cancellationToken);

        return File(
            result.Content,
            result.ContentType,
            result.FileName);
    }

    [HttpPut("{applicationId:guid}/status")]
    public async Task<
        ActionResult<ApiResponse<AdminApplicationDetail>>> UpdateStatus(
        Guid applicationId,
        [FromBody]
        UpdateEmployerApplicationStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await applications.UpdateStatusAsync(
                EmployerUserId,
                applicationId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<AdminApplicationDetail>(
                result,
                "Application status updated successfully."));
    }
}
