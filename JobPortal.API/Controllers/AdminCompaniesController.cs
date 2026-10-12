using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.AdminManagement;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/companies")]
[Produces("application/json")]
public sealed class AdminCompaniesController(ICompanyManagementService companies) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CompanyResponse>>>> Search(
        [FromQuery] CompanySearchQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<CompanyResponse>>(await companies.SearchAsync(query, cancellationToken)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CompanyResponse>>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CompanyResponse>(await companies.GetByIdAsync(id, cancellationToken)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CompanyResponse>>> Create(
        [FromBody] CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        var result = await companies.CreateAsync(User.GetRequiredUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id },
            new ApiResponse<CompanyResponse>(result, "Company created successfully."));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CompanyResponse>>> Update(
        Guid id, [FromBody] UpdateCompanyRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CompanyResponse>(await companies.UpdateAsync(id, request, cancellationToken),
            "Company updated successfully."));

    [HttpPost("{id:guid}/logo/resolve")]
    public async Task<ActionResult<ApiResponse<CompanyLogoPreviewResponse>>> ResolveLogo(
        Guid id, [FromBody] ResolveCompanyLogoRequest request,
        [FromServices] ICompanyLogoResolver resolver, CancellationToken cancellationToken)
    {
        var company = await companies.GetByIdAsync(id, cancellationToken);
        var website = request.WebsiteUrl ?? company.WebsiteUrl;
        var result = !request.ReplaceExisting && !string.IsNullOrWhiteSpace(company.LogoUrl)
            ? new CompanyLogoResolutionResult(company.LogoUrl, "ExistingCompanyLogo")
            : await resolver.ResolveAsync(company.Name, website, request.CareerPageUrl, cancellationToken);
        return Ok(new ApiResponse<CompanyLogoPreviewResponse>(new(company.Id, company.Name, website,
            result.LogoUrl, result.Source, result.Resolved)));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await companies.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("options")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AdminOptionResponse>>>> Options(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<IReadOnlyCollection<AdminOptionResponse>>(
            await companies.GetOptionsAsync(cancellationToken)));
}
