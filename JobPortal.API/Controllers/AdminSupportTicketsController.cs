using JobPortal.API.Extensions;
using JobPortal.Application.Features.Support;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/support/tickets")]
[Produces("application/json")]
public sealed class AdminSupportTicketsController(ISupportTicketService tickets) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminSupportTicketResponse>>>> Search(
        [FromQuery] AdminSupportTicketQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<AdminSupportTicketResponse>>(await tickets.SearchAsync(query, cancellationToken)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminSupportTicketResponse>>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AdminSupportTicketResponse>(await tickets.GetAsync(id, cancellationToken)));

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<AdminSupportTicketResponse>>> UpdateStatus(
        Guid id, UpdateSupportTicketStatusRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AdminSupportTicketResponse>(await tickets.UpdateStatusAsync(User.GetRequiredUserId(), id, request, cancellationToken)));

    [HttpPatch("{id:guid}/notes")]
    public async Task<ActionResult<ApiResponse<AdminSupportTicketResponse>>> UpdateNotes(
        Guid id, UpdateSupportTicketNotesRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AdminSupportTicketResponse>(await tickets.UpdateNotesAsync(User.GetRequiredUserId(), id, request, cancellationToken)));

    [HttpGet("{id:guid}/screenshot")]
    public async Task<IActionResult> Screenshot(Guid id, CancellationToken cancellationToken)
    {
        var image = await tickets.GetScreenshotAsync(id, cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Vary = "Authorization";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(image.Content, image.ContentType, "screenshot" + SupportTicketsController.Extension(image.ContentType));
    }
}
