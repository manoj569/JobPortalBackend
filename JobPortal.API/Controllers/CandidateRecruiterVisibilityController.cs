using System.Security.Claims;
using JobPortal.Application.Abstractions.CandidateRecruiterVisibility;
using JobPortal.Application.Features.CandidateRecruiterVisibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/candidate/recruiter-visibility")]
[Authorize(Roles = "Candidate")]
public sealed class CandidateRecruiterVisibilityController(
    ICandidateRecruiterVisibilityService service)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CandidateRecruiterVisibilityResponse>> Get(
        CancellationToken cancellationToken)
    {
        var candidateUserId = GetCurrentUserId();

        var result = await service.GetAsync(
            candidateUserId,
            cancellationToken);

        return Ok(result);
    }

    [HttpPut]
    public async Task<ActionResult<CandidateRecruiterVisibilityResponse>> Update(
        [FromBody] UpdateCandidateRecruiterVisibilityRequest request,
        CancellationToken cancellationToken)
    {
        var candidateUserId = GetCurrentUserId();

        var result = await service.UpdateAsync(
            candidateUserId,
            request,
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
