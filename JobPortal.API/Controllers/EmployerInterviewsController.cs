using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.EmployerInterviews;
using JobPortal.Application.Features.EmployerInterviews;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Employer")]
[Route("api/employer/interviews")]
[Produces("application/json")]
public sealed class EmployerInterviewsController(
    IEmployerInterviewService interviews)
    : ControllerBase
{
    private Guid EmployerUserId =>
        User.GetRequiredUserId();

    [HttpGet]
    public async Task<
        ActionResult<
            ApiResponse<
                PagedResponse<EmployerInterviewResponse>>>> Search(
        [FromQuery] EmployerInterviewQuery query,
        CancellationToken cancellationToken)
    {
        var result =
            await interviews.SearchAsync(
                EmployerUserId,
                query,
                cancellationToken);

        return Ok(
            new ApiResponse<
                PagedResponse<EmployerInterviewResponse>>(
                result));
    }

    [HttpGet("{interviewId:guid}")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerInterviewResponse>>> Get(
        Guid interviewId,
        CancellationToken cancellationToken)
    {
        var result =
            await interviews.GetAsync(
                EmployerUserId,
                interviewId,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerInterviewResponse>(
                result));
    }

    [HttpGet(
        "~/api/employer/applications/{applicationId:guid}/interviews")]
    public async Task<
        ActionResult<
            ApiResponse<
                IReadOnlyCollection<EmployerInterviewResponse>>>>
        GetForApplication(
            Guid applicationId,
            CancellationToken cancellationToken)
    {
        var result =
            await interviews.GetForApplicationAsync(
                EmployerUserId,
                applicationId,
                cancellationToken);

        return Ok(
            new ApiResponse<
                IReadOnlyCollection<EmployerInterviewResponse>>(
                result));
    }

    [HttpPost(
        "~/api/employer/applications/{applicationId:guid}/interviews")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerInterviewResponse>>> Create(
        Guid applicationId,
        [FromBody]
        CreateEmployerInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await interviews.CreateAsync(
                EmployerUserId,
                applicationId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerInterviewResponse>(
                result,
                "Interview scheduled successfully."));
    }

    [HttpPut("{interviewId:guid}")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerInterviewResponse>>> Update(
        Guid interviewId,
        [FromBody]
        UpdateEmployerInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await interviews.UpdateAsync(
                EmployerUserId,
                interviewId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerInterviewResponse>(
                result,
                "Interview updated successfully."));
    }

    [HttpPost("{interviewId:guid}/reschedule")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerInterviewResponse>>> Reschedule(
        Guid interviewId,
        [FromBody]
        RescheduleEmployerInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await interviews.RescheduleAsync(
                EmployerUserId,
                interviewId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerInterviewResponse>(
                result,
                "Interview rescheduled successfully."));
    }

    [HttpPost("{interviewId:guid}/cancel")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerInterviewResponse>>> Cancel(
        Guid interviewId,
        [FromBody]
        CancelEmployerInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await interviews.CancelAsync(
                EmployerUserId,
                interviewId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerInterviewResponse>(
                result,
                "Interview cancelled successfully."));
    }

    [HttpPost("{interviewId:guid}/complete")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerInterviewResponse>>> Complete(
        Guid interviewId,
        [FromBody]
        CompleteEmployerInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await interviews.CompleteAsync(
                EmployerUserId,
                interviewId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerInterviewResponse>(
                result,
                "Interview completed successfully."));
    }
}
