using JobPortal.Application.Abstractions.Portfolios;
using JobPortal.Application.Features.Portfolios;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/portfolios")]
[Produces("application/json")]
public sealed class PublicPortfoliosController(ICandidatePortfolioService portfolios) : ControllerBase
{
    [HttpGet("{slug}")]
    public async Task<ActionResult<ApiResponse<PublicPortfolioResponse>>> Get(
        string slug, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PublicPortfolioResponse>(
            await portfolios.GetPublicAsync(slug, cancellationToken)));
}
