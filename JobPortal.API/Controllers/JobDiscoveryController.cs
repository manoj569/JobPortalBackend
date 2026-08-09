using JobPortal.API.Extensions;
using JobPortal.Application.Features.AdminImports;
using JobPortal.Application.Features.JobDiscovery;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Administrator"), Route("api/admin/job-discovery")]
public sealed class JobDiscoveryController(IJobDiscoveryService service) : ControllerBase
{
    [HttpPost("run")]
    public async Task<ActionResult<ApiResponse<JobDiscoveryRunSummary>>> Run([FromBody] JobDiscoveryCriteria? criteria, CancellationToken ct) =>
        Accepted(new ApiResponse<JobDiscoveryRunSummary>(await service.RunAsync("Manual", criteria, ct)));
    [HttpGet("runs")]
    public async Task<ApiResponse<IReadOnlyCollection<JobDiscoveryRunSummary>>> Runs([FromQuery] int take = 25, CancellationToken ct = default) =>
        new(await service.ListAsync(take, ct));
    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<ApiResponse<JobDiscoveryRunDetailsResponse>>> Details(Guid runId, CancellationToken ct) =>
        await service.GetAsync(runId, ct) is { } run ? Ok(new ApiResponse<JobDiscoveryRunDetailsResponse>(run)) : NotFound();
    [HttpPost("runs/{runId:guid}/preview")]
    public async Task<ApiResponse<CsvImportResult>> Preview(Guid runId, [FromBody] JobDiscoveryCommitRequest request, CancellationToken ct) =>
        new(await service.PreviewAsync(runId, request.ItemIds, ct));
    [HttpPost("runs/{runId:guid}/commit")]
    public async Task<ActionResult<ApiResponse<JobDiscoveryCommitResult>>> Commit(Guid runId, [FromBody] JobDiscoveryCommitRequest request, CancellationToken ct)
    {
        var result = await service.CommitAsync(User.GetRequiredUserId(), runId, request.ItemIds, ct);
        return result.Import.InvalidRows == 0 ? Ok(new ApiResponse<JobDiscoveryCommitResult>(result)) : BadRequest(new ApiResponse<JobDiscoveryCommitResult>(result));
    }
}
