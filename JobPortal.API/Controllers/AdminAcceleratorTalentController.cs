using JobPortal.API.Extensions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

public sealed record SuppressTalentContactRequest(string Reason);
public sealed record ReconcileTalentOutreachRequest(string Reason);
public sealed record AdminTalentContactView(Guid Id, Guid CompanyId, string FullName,
    string Title, EmployerContactVerification Verification, bool IsSuppressed);

[ApiController, Authorize(Roles = "Administrator"), Route("api/admin/accelerator/talent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(16384), EnableRateLimiting("CareerAccelerator")]
public sealed class AdminAcceleratorTalentController(AcceleratorTalentService talent) : ControllerBase
{
    [HttpGet("operations")]
    public async Task<ActionResult<ApiResponse<TalentOperationsSummary>>> Operations(CancellationToken ct) =>
        Ok(new ApiResponse<TalentOperationsSummary>(await talent.OperationsAsync(
            User.GetRequiredUserId(), ct)));

    [HttpPost("contacts")]
    public async Task<ActionResult<ApiResponse<AdminTalentContactView>>> AddContact(
        AdminEmployerContactRequest request, CancellationToken ct)
    {
        var contact = await talent.AddContactAsync(User.GetRequiredUserId(), request, ct);
        return Ok(new ApiResponse<AdminTalentContactView>(new(contact.Id, contact.CompanyId,
            contact.FullName, contact.Title, contact.Verification, contact.IsSuppressed)));
    }

    [HttpPost("contacts/{id:guid}/suppress")]
    public async Task<IActionResult> Suppress(Guid id, SuppressTalentContactRequest request,
        CancellationToken ct)
    { await talent.SuppressContactAsync(User.GetRequiredUserId(), id, request.Reason, ct);
        return NoContent(); }

    [HttpGet("unresolved-outreach")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UnresolvedOutreachView>>>> Unresolved(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<UnresolvedOutreachView>>(
            await talent.UnresolvedAsync(User.GetRequiredUserId(), page, pageSize, ct)));

    [HttpPost("reconcile-outreach")]
    public async Task<ActionResult<ApiResponse<int>>> Reconcile(ReconcileTalentOutreachRequest request,
        CancellationToken ct) => Ok(new ApiResponse<int>(await talent.AdminReconcileOutreachAsync(
            User.GetRequiredUserId(), request.Reason, ct)));
}
