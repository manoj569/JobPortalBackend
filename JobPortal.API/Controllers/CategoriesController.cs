using JobPortal.Persistence.Context;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public sealed class CategoriesController(JobPortalDbContext db) : ControllerBase
{
    public sealed record CategoryOptionDto(Guid Id, string Name);

    [HttpGet("lookup")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<CategoryOptionDto>>>> Lookup(
        CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new CategoryOptionDto(x.Id, x.Name))
            .ToArrayAsync(cancellationToken);
        return Ok(new ApiResponse<IReadOnlyCollection<CategoryOptionDto>>(categories));
    }

    [HttpGet("options")]
    [Authorize(Roles = "Candidate,Administrator")]
    public Task<ActionResult<ApiResponse<IReadOnlyCollection<CategoryOptionDto>>>> Options(
        CancellationToken cancellationToken) => Lookup(cancellationToken);
}
