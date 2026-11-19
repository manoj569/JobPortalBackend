using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.EmployerPipeline;
using JobPortal.Application.Features.EmployerPipeline;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Employer")]
[Route("api/employer/pipeline")]
[Produces("application/json")]
public sealed class EmployerPipelineController(
    IEmployerPipelineService pipeline)
    : ControllerBase
{
    private Guid EmployerUserId =>
        User.GetRequiredUserId();

    [HttpGet]
    public async Task<
        ActionResult<
            ApiResponse<
                PagedResponse<EmployerPipelineResponse>>>> Search(
        [FromQuery] EmployerPipelineQuery query,
        CancellationToken cancellationToken)
    {
        var result =
            await pipeline.SearchAsync(
                EmployerUserId,
                query,
                cancellationToken);

        return Ok(
            new ApiResponse<
                PagedResponse<EmployerPipelineResponse>>(
                result));
    }

    [HttpGet("{applicationId:guid}")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerPipelineResponse>>> Get(
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var result =
            await pipeline.GetAsync(
                EmployerUserId,
                applicationId,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerPipelineResponse>(
                result));
    }

    [HttpPost("{applicationId:guid}/screening/start")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerPipelineResponse>>> StartScreening(
        Guid applicationId,
        [FromBody] StartEmployerScreeningRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await pipeline.StartScreeningAsync(
                EmployerUserId,
                applicationId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerPipelineResponse>(
                result,
                "Screening started successfully."));
    }

    [HttpPut("{applicationId:guid}/screening")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerPipelineResponse>>> UpdateScreening(
        Guid applicationId,
        [FromBody] UpdateEmployerScreeningRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await pipeline.UpdateScreeningAsync(
                EmployerUserId,
                applicationId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerPipelineResponse>(
                result,
                "Screening updated successfully."));
    }

    [HttpPost("{applicationId:guid}/screening/pass")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerPipelineResponse>>> PassScreening(
        Guid applicationId,
        [FromBody] CompleteEmployerScreeningRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await pipeline.PassScreeningAsync(
                EmployerUserId,
                applicationId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerPipelineResponse>(
                result,
                "Candidate passed screening."));
    }

    [HttpPost("{applicationId:guid}/screening/fail")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerPipelineResponse>>> FailScreening(
        Guid applicationId,
        [FromBody] CompleteEmployerScreeningRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await pipeline.FailScreeningAsync(
                EmployerUserId,
                applicationId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerPipelineResponse>(
                result,
                "Candidate failed screening."));
    }

    [HttpPut("{applicationId:guid}/stage")]
    public async Task<
        ActionResult<
            ApiResponse<EmployerPipelineResponse>>> UpdateStage(
        Guid applicationId,
        [FromBody] UpdateEmployerPipelineStageRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await pipeline.UpdateStageAsync(
                EmployerUserId,
                applicationId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerPipelineResponse>(
                result,
                "Pipeline stage updated successfully."));
    }
}
