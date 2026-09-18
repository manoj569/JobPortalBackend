using JobPortal.Application.Abstractions.AdminManagement;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/admin/categories")]
[Produces("application/json")]
public sealed class AdminCategoriesController(ICategoryManagementService categories) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CategoryResponse>>>> Search(
        [FromQuery] CategorySearchQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<CategoryResponse>>(await categories.SearchAsync(query, cancellationToken)));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CategoryResponse>(await categories.GetByIdAsync(id, cancellationToken)));

    [HttpPost]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> Create(
        [FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await categories.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id },
            new ApiResponse<CategoryResponse>(result, "Category created successfully."));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<ApiResponse<CategoryResponse>>> Update(
        Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CategoryResponse>(await categories.UpdateAsync(id, request, cancellationToken),
            "Category updated successfully."));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await categories.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("options")]
    [Authorize(Roles = "Candidate,Administrator")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AdminOptionResponse>>>> Options(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<IReadOnlyCollection<AdminOptionResponse>>(
            await categories.GetOptionsAsync(cancellationToken)));
}
