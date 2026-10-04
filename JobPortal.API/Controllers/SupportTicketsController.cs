using JobPortal.API.Extensions;
using JobPortal.Application.Features.Support;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

public sealed class CreateSupportTicketForm
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public SupportCategory Category { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IFormFile? Screenshot { get; set; }
}

[ApiController]
[Authorize]
[Route("api/support/tickets")]
[Produces("application/json")]
public sealed class SupportTicketsController(ISupportTicketService tickets) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("SupportTickets")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024, ValueLengthLimit = 8192, ValueCountLimit = 10)]
    [ProducesResponseType(typeof(ApiResponse<SupportTicketCreatedResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<SupportTicketCreatedResponse>>> Create(
        [FromForm] CreateSupportTicketForm form, CancellationToken cancellationToken)
    {
        var userId = User.Identity?.IsAuthenticated == true ? User.GetRequiredUserId() : (Guid?)null;
        if (Request.HasFormContentType && (Request.Form.Files.Count > 1 ||
            Request.Form.Files.Any(file => !file.Name.Equals(nameof(CreateSupportTicketForm.Screenshot), StringComparison.OrdinalIgnoreCase))))
            throw new BadRequestException("Only one Screenshot attachment is supported.", "invalid_screenshot");
        await using var stream = form.Screenshot?.OpenReadStream();
        var upload = form.Screenshot is { } file
            ? new SupportScreenshotUpload(stream!, file.Length, file.FileName, file.ContentType) : null;
        var result = await tickets.CreateAsync(userId,
            new(form.Name, form.Email, form.Category, form.Subject, form.Description), upload, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new ApiResponse<SupportTicketCreatedResponse>(result,
            "Your support request has been submitted successfully."));
    }

    [HttpGet("my")]
    public async Task<ActionResult<ApiResponse<PagedResponse<SupportTicketResponse>>>> Mine(
        [FromQuery] SupportTicketPageQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<SupportTicketResponse>>(await tickets.GetMineAsync(User.GetRequiredUserId(), query, cancellationToken)));

    [HttpGet("my/{ticketNumber}")]
    public async Task<ActionResult<ApiResponse<SupportTicketResponse>>> GetMine(string ticketNumber, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<SupportTicketResponse>(await tickets.GetMineAsync(User.GetRequiredUserId(), ticketNumber, cancellationToken)));

    [HttpGet("my/{ticketNumber}/screenshot")]
    public async Task<IActionResult> Screenshot(string ticketNumber, CancellationToken cancellationToken)
    {
        var image = await tickets.GetMyScreenshotAsync(User.GetRequiredUserId(), ticketNumber, cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Vary = "Authorization";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(image.Content, image.ContentType, "screenshot" + Extension(image.ContentType));
    }

    internal static string Extension(string contentType) => contentType switch
    {
        "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg"
    };
}
