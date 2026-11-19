using System.Security.Claims;
using JobPortal.Application.Abstractions.EmployerTalentPool;
using JobPortal.Application.Features.EmployerTalentPool;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/employer/talent-pool")]
[Authorize(Roles = "Employer")]
public sealed class EmployerTalentPoolController(
    IEmployerTalentPoolService service)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EmployerTalentPoolResponse>> Get(
        [FromQuery] EmployerTalentPoolQuery query,
        CancellationToken cancellationToken)
    {
        var employerUserId = GetCurrentUserId();

        var result = await service.GetAsync(
            employerUserId,
            query,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<EmployerTalentPoolItemResponse>> Add(
        [FromBody] AddEmployerTalentPoolCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var employerUserId = GetCurrentUserId();

        var result = await service.AddAsync(
            employerUserId,
            request,
            cancellationToken);

        return Ok(result);
    }

    [HttpPut("{candidateUserId:guid}/notes")]
    public async Task<ActionResult<EmployerTalentPoolItemResponse>> UpdateNotes(
        Guid candidateUserId,
        [FromBody] UpdateEmployerTalentPoolNotesRequest request,
        CancellationToken cancellationToken)
    {
        var employerUserId = GetCurrentUserId();

        var result = await service.UpdateNotesAsync(
            employerUserId,
            candidateUserId,
            request,
            cancellationToken);

        return Ok(result);
    }

    [HttpDelete("{candidateUserId:guid}")]
    public async Task<IActionResult> Remove(
        Guid candidateUserId,
        CancellationToken cancellationToken)
    {
        var employerUserId = GetCurrentUserId();

        await service.RemoveAsync(
            employerUserId,
            candidateUserId,
            cancellationToken);

        return NoContent();
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
