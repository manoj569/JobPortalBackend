using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.EmployerCompanies;
using JobPortal.Application.Features.EmployerCompanies;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Employer")]
[Route("api/employer/company")]
[Produces("application/json")]
public sealed class EmployerCompaniesController(
    IEmployerCompanyService companies)
    : ControllerBase
{
    [HttpGet]
    public async Task<
        ActionResult<ApiResponse<EmployerCompanyResponse?>>> GetMine(
        CancellationToken cancellationToken)
    {
        var result =
            await companies.GetMineAsync(
                User.GetRequiredUserId(),
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerCompanyResponse?>(
                result));
    }

    [HttpPost("setup")]
    public async Task<
        ActionResult<ApiResponse<EmployerCompanyResponse>>> Setup(
        [FromBody] EmployerCompanySetupRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await companies.SetupAsync(
                User.GetRequiredUserId(),
                request,
                cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new ApiResponse<EmployerCompanyResponse>(
                result,
                "Company setup completed successfully."));
    }

    [HttpPut]
    public async Task<
        ActionResult<ApiResponse<EmployerCompanyResponse>>> Update(
        [FromBody] EmployerCompanyUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await companies.UpdateAsync(
                User.GetRequiredUserId(),
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<EmployerCompanyResponse>(
                result,
                "Company updated successfully."));
    }
}
