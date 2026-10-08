using JobPortal.Application.Features.Referrals;
using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/referrals/requests")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminReferralRequestsController(ReferralMarketplaceService marketplace) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminReferralRequestResponse>>>> List([FromQuery] bool issuesOnly = false,
        [FromQuery] ReferralRequestStatus? status = null, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<PagedResponse<AdminReferralRequestResponse>>(await marketplace.AdminListAsync(issuesOnly, status, pageNumber, pageSize, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminReferralRequestResponse>>> Detail(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<AdminReferralRequestResponse>(await marketplace.AdminDetailAsync(id, ct)));
}
