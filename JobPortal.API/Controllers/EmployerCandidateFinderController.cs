using System.Security.Claims;
using JobPortal.Application.Abstractions.EmployerCandidateFinder;
using JobPortal.Application.Features.EmployerCandidateFinder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/employer/ai-candidates")]
[Authorize(Roles = "Employer")]
public sealed class EmployerCandidateFinderController(
    IEmployerCandidateFinderService service)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EmployerCandidateFinderResponse>> Search(
        [FromQuery] Guid jobId,
        [FromQuery] EmployerCandidateFinderQuery query,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "jobId is required."
            });
        }

        var employerUserId = GetCurrentUserId();

        var result = await service.SearchAsync(
            employerUserId,
            jobId,
            query,
            cancellationToken);

        return Ok(result);
    }

    private Guid GetCurrentUserId()
    {
        var value =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        if (!Guid.TryParse(value, out var userId))
        {
            throw new UnauthorizedAccessException(
                "Authenticated user identifier is missing.");
        }

        return userId;
    }
}
