using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Administrator")]
[Route("api/admin/accelerator/market")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class AdminAcceleratorMarketController(MarketIntelligenceService market) : ControllerBase
{
    [HttpGet("roles/{id:guid}/accelerator-mapping")]
    public async Task<ActionResult<ApiResponse<MarketAcceleratorRoleMapping>>> AcceleratorMapping(Guid id,CancellationToken ct) =>
        Ok(new ApiResponse<MarketAcceleratorRoleMapping>(await market.AcceleratorRoleMappingAsync(User.GetRequiredUserId(),id,ct)));
    [HttpPut("roles/{id:guid}/accelerator-mapping")]
    public async Task<ActionResult<ApiResponse<MarketAcceleratorRoleMapping>>> MapAcceleratorRole(Guid id,MarketMapAcceleratorRoleRequest request,CancellationToken ct) =>
        Ok(new ApiResponse<MarketAcceleratorRoleMapping>(await market.MapAcceleratorRoleAsync(User.GetRequiredUserId(),id,request,ct)));

    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<MarketProcessingStatus>>> Status(CancellationToken ct) =>
        Ok(new ApiResponse<MarketProcessingStatus>(await market.StatusAsync(User.GetRequiredUserId(), ct)));

    [HttpPost("backfill")]
    public async Task<ActionResult<ApiResponse<MarketBatchResult>>> Backfill(MarketBackfillRequest request,
        CancellationToken ct) => Ok(new ApiResponse<MarketBatchResult>(await market.BackfillAsync(
            User.GetRequiredUserId(), request, ct)));

    [HttpGet("skill-discoveries")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketDiscoveryItem>>>> Skills(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<MarketDiscoveryItem>>(await market.DiscoveriesAsync(
            User.GetRequiredUserId(), false, page, pageSize, ct)));

    [HttpGet("role-discoveries")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketDiscoveryItem>>>> Roles(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<MarketDiscoveryItem>>(await market.DiscoveriesAsync(
            User.GetRequiredUserId(), true, page, pageSize, ct)));

    [HttpGet("skill-discoveries/{id:guid}")]
    public async Task<ActionResult<ApiResponse<MarketDiscoveryItem?>>> Skill(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<MarketDiscoveryItem?>(await market.DiscoveryAsync(User.GetRequiredUserId(), false, id, ct)));

    [HttpGet("role-discoveries/{id:guid}")]
    public async Task<ActionResult<ApiResponse<MarketDiscoveryItem?>>> Role(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<MarketDiscoveryItem?>(await market.DiscoveryAsync(User.GetRequiredUserId(), true, id, ct)));

    [HttpPost("skill-discoveries/{id:guid}/approve")]
    public async Task<IActionResult> ApproveSkill(Guid id, MarketApproveSkillRequest request, CancellationToken ct)
    { await market.ApproveSkillAsync(User.GetRequiredUserId(), id, request, ct); return NoContent(); }

    [HttpPost("role-discoveries/{id:guid}/approve")]
    public async Task<IActionResult> ApproveRole(Guid id, MarketApproveRoleRequest request, CancellationToken ct)
    { await market.ApproveRoleAsync(User.GetRequiredUserId(), id, request, ct); return NoContent(); }

    [HttpPost("skill-discoveries/{id:guid}/merge")]
    public async Task<IActionResult> MergeSkill(Guid id, MarketMergeRequest request, CancellationToken ct)
    { await market.MergeAsync(User.GetRequiredUserId(), false, id, request, ct); return NoContent(); }

    [HttpPost("role-discoveries/{id:guid}/map")]
    public async Task<IActionResult> MapRole(Guid id, MarketMergeRequest request, CancellationToken ct)
    { await market.MergeAsync(User.GetRequiredUserId(), true, id, request, ct); return NoContent(); }

    [HttpPost("skill-discoveries/{id:guid}/reject")]
    public async Task<IActionResult> RejectSkill(Guid id, MarketReviewRequest request, CancellationToken ct)
    { await market.ReviewAsync(User.GetRequiredUserId(), false, id, MarketDiscoveryStatus.Rejected, request, ct); return NoContent(); }

    [HttpPost("role-discoveries/{id:guid}/reject")]
    public async Task<IActionResult> RejectRole(Guid id, MarketReviewRequest request, CancellationToken ct)
    { await market.ReviewAsync(User.GetRequiredUserId(), true, id, MarketDiscoveryStatus.Rejected, request, ct); return NoContent(); }

    [HttpPost("skills/{id:guid}/aliases")]
    public async Task<IActionResult> SkillAlias(Guid id, MarketAliasRequest request, CancellationToken ct)
    { await market.AddAliasAsync(User.GetRequiredUserId(), false, id, request, ct); return NoContent(); }

    [HttpPost("roles/{id:guid}/aliases")]
    public async Task<IActionResult> RoleAlias(Guid id, MarketAliasRequest request, CancellationToken ct)
    { await market.AddAliasAsync(User.GetRequiredUserId(), true, id, request, ct); return NoContent(); }

    [HttpPost("skills/{id:guid}/merge")]
    public async Task<IActionResult> MergeCanonicalSkill(Guid id, MarketMergeRequest request, CancellationToken ct)
    { await market.MergeCanonicalAsync(User.GetRequiredUserId(), false, id, request, ct); return NoContent(); }

    [HttpPost("roles/{id:guid}/merge")]
    public async Task<IActionResult> MergeCanonicalRole(Guid id, MarketMergeRequest request, CancellationToken ct)
    { await market.MergeCanonicalAsync(User.GetRequiredUserId(), true, id, request, ct); return NoContent(); }
}
