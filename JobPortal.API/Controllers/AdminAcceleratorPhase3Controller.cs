using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Administrator")]
[Route("api/admin/accelerator/v1/dispatches")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class AdminAcceleratorPhase3Controller(IAcceleratorPhase3Store store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DispatchAdminResponse>>>> Failed([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page is < 1 or > 10000 || pageSize is < 1 or > 100)
            throw new BadRequestException("Invalid pagination.", "accelerator_invalid_request");
        var rows = await store.FailedDispatchesAsync((page - 1) * pageSize, pageSize, ct);
        return Ok(new ApiResponse<IReadOnlyList<DispatchAdminResponse>>(rows.Select(x => new DispatchAdminResponse(
            x.Id, x.UserId, x.ReservationId, x.Status, x.ReconciliationAttemptCount, x.RequiresManualReview,
            x.ManualReviewReason, x.NextReconciliationAtUtc)).ToArray()));
    }
    // Deliberately no manual "verified" endpoint until evidence validation and quota
    // ledger reconciliation for dispatched failures are implemented.
}
