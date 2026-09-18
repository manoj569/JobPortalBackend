using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace JobPortal.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/jobs")]
[Produces("application/json")]
[OutputCache(PolicyName = "PublicJobs")]
public sealed class PublicJobsController(IPublicJobService jobService) : ControllerBase
{
    [HttpGet]
    [HttpGet("search")]
    [HttpGet("filter")]
    [ProducesResponseType(typeof(ApiResponse<PublicJobSearchResponse>), StatusCodes.Status200OK)]
    public Task<ActionResult<ApiResponse<PublicJobSearchResponse>>> Search(
        [FromQuery] PublicJobQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query, cancellationToken);

    [HttpGet("referrals")]
    [ProducesResponseType(typeof(ApiResponse<PublicJobSearchResponse>), StatusCodes.Status200OK)]
    public Task<ActionResult<ApiResponse<PublicJobSearchResponse>>> ReferralJobs(
        [FromQuery] PublicJobQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query with { ReferralOnly = true }, cancellationToken);

    [HttpGet("latest")]
    [ProducesResponseType(typeof(ApiResponse<PublicJobSearchResponse>), StatusCodes.Status200OK)]
    public Task<ActionResult<ApiResponse<PublicJobSearchResponse>>> Latest(
        [FromQuery] PublicJobQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query with { SortBy = PublicJobSort.LatestPublished }, cancellationToken);

    [HttpGet("newest")]
    [ProducesResponseType(typeof(ApiResponse<PublicJobSearchResponse>), StatusCodes.Status200OK)]
    public Task<ActionResult<ApiResponse<PublicJobSearchResponse>>> Newest(
        [FromQuery] PublicJobQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query with { SortBy = PublicJobSort.NewestAdded }, cancellationToken);

    [HttpGet("featured")]
    [ProducesResponseType(typeof(ApiResponse<PublicJobSearchResponse>), StatusCodes.Status200OK)]
    public Task<ActionResult<ApiResponse<PublicJobSearchResponse>>> Featured(
        [FromQuery] PublicJobQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query with { IsFeatured = true, FeaturedOnly = true }, cancellationToken);

    [HttpGet("filter-options")]
    [HttpGet("facets")]
    [ProducesResponseType(typeof(ApiResponse<PublicJobFilterOptionsResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PublicJobFilterOptionsResponse>>> FilterOptions(
        [FromQuery] PublicJobQuery query,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PublicJobFilterOptionsResponse>(
            await jobService.GetFilterOptionsAsync(query, cancellationToken)));

    [HttpGet("companies/popular")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyCollection<PopularCompanyResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PopularCompanyResponse>>>> PopularCompanies(
        [FromQuery] int limit = 10, CancellationToken cancellationToken = default) =>
        Ok(new ApiResponse<IReadOnlyCollection<PopularCompanyResponse>>(
            await jobService.GetPopularCompaniesAsync(limit, cancellationToken)));

    [HttpGet("{slug}/related")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyCollection<PublicJobSummary>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PublicJobSummary>>>> Related(
        string slug, [FromQuery] int limit = 6, CancellationToken cancellationToken = default) =>
        Ok(new ApiResponse<IReadOnlyCollection<PublicJobSummary>>(
            await jobService.GetRelatedAsync(slug, limit, cancellationToken)));

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ApiResponse<PublicJobDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PublicJobDetails>>> Details(
        string slug, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PublicJobDetails>(
            await jobService.GetDetailsAsync(slug, cancellationToken)));

    private async Task<ActionResult<ApiResponse<PublicJobSearchResponse>>> GetPageAsync(
        PublicJobQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PublicJobSearchResponse>(
            await jobService.SearchAsync(query, cancellationToken)));
}
