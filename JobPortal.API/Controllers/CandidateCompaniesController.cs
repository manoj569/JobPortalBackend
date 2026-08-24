using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.CandidateCompanies;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Candidate")]
[Route("api/candidate/companies")]
[Produces("application/json")]
public sealed class CandidateCompaniesController(ICandidateCompanyService companies) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<CompanyOption>>>> Search(
        [FromQuery] string query, [FromQuery] int limit = 10, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyCollection<CompanyOption>>(await companies.SearchAsync(User.GetRequiredUserId(), query, limit, ct)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CreateCandidateCompanyResponse>>> Create(
        CreateCandidateCompanyRequest request, CancellationToken ct)
    {
        var result = await companies.CreateAsync(User.GetRequiredUserId(), request, ct);
        var response = new ApiResponse<CreateCandidateCompanyResponse>(result);
        return result.Created ? StatusCode(StatusCodes.Status201Created, response) : Ok(response);
    }
}
