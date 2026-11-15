using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Route("api/accelerator/market")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorMarketController(MarketIntelligenceService market) : ControllerBase
{
    [HttpGet("career-paths"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketPathCard>>>> CareerPaths(
        [FromQuery] string? search, [FromQuery] string? category, [FromQuery] string? location,
        [FromQuery] int? experience, [FromQuery] string? sort, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<MarketPathCard>>(await market.CareerPathsAsync(search,
            category, location, experience, sort, page, pageSize, ct)));

    [HttpGet("career-paths/{id:guid}"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<MarketPathDetail>>> CareerPath(Guid id, CancellationToken ct)
    {
        var detail = await market.CareerPathAsync(id, ct);
        return detail is null ? NotFound() : Ok(new ApiResponse<MarketPathDetail>(detail));
    }

    [HttpGet("career-paths/{id:guid}/my-gaps"), Authorize(Roles = "Candidate")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketRoleGap>>>> MyRoleGaps(Guid id,
        CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<MarketRoleGap>>(
            await market.RoleGapAsync(User.GetRequiredUserId(), id, ct)));

    [HttpGet("skill-paths"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketPathCard>>>> SkillPaths(
        [FromQuery] string? search, [FromQuery] string? category, [FromQuery] Guid? roleId,
        [FromQuery] string? location, [FromQuery] int? experience, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<MarketPathCard>>(await market.SkillPathsAsync(search,
            category, roleId, location, experience, sort, page, pageSize, ct)));

    [HttpGet("skill-paths/{id:guid}"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<MarketPathDetail>>> SkillPath(Guid id, CancellationToken ct)
    {
        var detail = await market.SkillPathAsync(id, ct);
        return detail is null ? NotFound() : Ok(new ApiResponse<MarketPathDetail>(detail));
    }

    [HttpGet("search"), AllowAnonymous]
    public async Task<ActionResult<ApiResponse<MarketSearchResult>>> Search([FromQuery] string q,
        CancellationToken ct) => Ok(new ApiResponse<MarketSearchResult>(await market.SearchAsync(q, ct)));

    [HttpGet("recommendations/career-paths"), Authorize(Roles = "Candidate")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketRecommendation>>>> CareerRecommendations(
        CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<MarketRecommendation>>(
            await market.RecommendAsync(User.GetRequiredUserId(), true, ct)));

    [HttpGet("recommendations/skill-paths"), Authorize(Roles = "Candidate")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketRecommendation>>>> SkillRecommendations(
        CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<MarketRecommendation>>(
            await market.RecommendAsync(User.GetRequiredUserId(), false, ct)));
}
