using System.Globalization;
using FluentValidation;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.Support;

public sealed class SupportTicketService(
    ISupportTicketRepository tickets, IUserRepository users, IUnitOfWork unitOfWork,
    ISupportScreenshotStorage storage, IEmailService email, IOptions<SupportSettings> settings,
    TimeProvider timeProvider, ILogger<SupportTicketService> logger,
    IValidator<CreateSupportTicketRequest> createValidator,
    IValidator<SupportTicketPageQuery> pageValidator, IValidator<AdminSupportTicketQuery> searchValidator,
    IValidator<UpdateSupportTicketStatusRequest> statusValidator, IValidator<UpdateSupportTicketNotesRequest> notesValidator) : ISupportTicketService
{
    private readonly SupportSettings supportSettings = settings.Value;
    private static readonly Action<ILogger, Guid, Exception?> Created = LoggerMessage.Define<Guid>(
        LogLevel.Information, new(7101, nameof(Created)), "Support ticket {TicketId} created.");
    private static readonly Action<ILogger, Guid, Exception?> ScreenshotFailed = LoggerMessage.Define<Guid>(
        LogLevel.Warning, new(7102, nameof(ScreenshotFailed)), "Support screenshot storage failed for ticket {TicketId}.");
    private static readonly Action<ILogger, Guid, string, Exception?> EmailFailed = LoggerMessage.Define<Guid, string>(
        LogLevel.Warning, new(7103, nameof(EmailFailed)), "Support email for ticket {TicketId}, audience {Audience}, was not delivered.");
    private static readonly Action<ILogger, Guid, Guid, Exception?> Updated = LoggerMessage.Define<Guid, Guid>(
        LogLevel.Information, new(7104, nameof(Updated)), "Support ticket {TicketId} updated by administrator {ActorId}.");

    public async Task<SupportTicketCreatedResponse> CreateAsync(Guid? userId, CreateSupportTicketRequest request,
        SupportScreenshotUpload? screenshot = null, CancellationToken cancellationToken = default)
    {
        if (userId is { } id)
        {
            var user = await users.GetByIdWithRoleAsync(id, cancellationToken) ?? throw new UnauthorizedException();
            request = request with { Name = $"{user.FirstName} {user.LastName}".Trim(), Email = user.Email };
        }
        request = request with { Name = request.Name?.Trim(), Email = request.Email?.Trim().ToLowerInvariant(),
            Subject = request.Subject?.Trim()!, Description = request.Description?.Trim()! };
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var ticket = new SupportTicket
        {
            UserId = userId, Name = request.Name!, Email = request.Email!, Category = request.Category,
            Subject = request.Subject, Description = request.Description, CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        // Full random GUID avoids a shared counter and keeps ticket numbers non-enumerable.
        ticket.TicketNumber = $"CH-{ticket.Id:N}".ToUpperInvariant();
        if (screenshot is not null)
        {
            var (content, extension) = await SupportScreenshotValidation.ReadAsync(screenshot, cancellationToken);
            try
            {
                using var stream = new MemoryStream(content, writable: false);
                ticket.ScreenshotPath = await storage.StoreAsync(stream, extension, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                ScreenshotFailed(logger, ticket.Id, null);
                throw new AppException("Screenshot could not be saved. Please try again.", 503, "support_storage_unavailable");
            }
        }
        try
        {
            await tickets.AddAsync(ticket, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (ticket.ScreenshotPath is { } key)
            {
                try { await storage.DeleteAsync(key, CancellationToken.None); }
                catch (Exception) { ScreenshotFailed(logger, ticket.Id, null); }
            }
            throw;
        }
        Created(logger, ticket.Id, null);
        // The ticket is durable before email. A disconnect or email failure cannot undo it.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await NotifyAsync(ticket, timeout.Token);
        return new(ticket.TicketNumber);
    }

    public async Task<PagedResponse<SupportTicketResponse>> GetMineAsync(Guid userId, SupportTicketPageQuery query, CancellationToken cancellationToken = default)
    {
        await pageValidator.ValidateAndThrowAsync(query, cancellationToken);
        var (items, total) = await tickets.GetMineAsync(userId, query, cancellationToken);
        return new(items.Select(ToUserResponse).ToArray(), query.PageNumber, query.PageSize, total);
    }

    public async Task<SupportTicketResponse> GetMineAsync(Guid userId, string ticketNumber, CancellationToken cancellationToken = default) =>
        ToUserResponse(await RequiredOwnedAsync(userId, ticketNumber, cancellationToken));

    public async Task<SupportScreenshotDownload> GetMyScreenshotAsync(Guid userId, string ticketNumber, CancellationToken cancellationToken = default) =>
        await OpenScreenshotAsync(await RequiredOwnedAsync(userId, ticketNumber, cancellationToken), cancellationToken);

    public async Task<PagedResponse<AdminSupportTicketResponse>> SearchAsync(AdminSupportTicketQuery query, CancellationToken cancellationToken = default)
    {
        await searchValidator.ValidateAndThrowAsync(query, cancellationToken);
        var (items, total) = await tickets.SearchAsync(query, cancellationToken);
        return new(items.Select(ToAdminResponse).ToArray(), query.PageNumber, query.PageSize, total);
    }

    public async Task<AdminSupportTicketResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ToAdminResponse(await RequiredAsync(id, cancellationToken));

    public async Task<SupportScreenshotDownload> GetScreenshotAsync(Guid id, CancellationToken cancellationToken = default) =>
        await OpenScreenshotAsync(await RequiredAsync(id, cancellationToken), cancellationToken);

    public async Task<AdminSupportTicketResponse> UpdateStatusAsync(Guid actorId, Guid id, UpdateSupportTicketStatusRequest request, CancellationToken cancellationToken = default)
    {
        await statusValidator.ValidateAndThrowAsync(request, cancellationToken);
        var ticket = await RequiredAsync(id, cancellationToken);
        if (ticket.Status == request.Status) return ToAdminResponse(ticket);
        ticket.Status = request.Status;
        ticket.ResolvedAtUtc = request.Status == SupportTicketStatus.Resolved ? timeProvider.GetUtcNow().UtcDateTime : null;
        ticket.Revision++;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        Updated(logger, ticket.Id, actorId, null);
        return ToAdminResponse(ticket);
    }

    public async Task<AdminSupportTicketResponse> UpdateNotesAsync(Guid actorId, Guid id, UpdateSupportTicketNotesRequest request, CancellationToken cancellationToken = default)
    {
        await notesValidator.ValidateAndThrowAsync(request, cancellationToken);
        var ticket = await RequiredAsync(id, cancellationToken);
        ticket.AdminNotes = string.IsNullOrWhiteSpace(request.AdminNotes) ? null : request.AdminNotes.Trim();
        ticket.Revision++;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        Updated(logger, ticket.Id, actorId, null);
        return ToAdminResponse(ticket);
    }

    private async Task<SupportTicket> RequiredAsync(Guid id, CancellationToken ct) =>
        await tickets.GetAsync(id, ct) ?? throw new NotFoundException("Support ticket not found.");

    private async Task<SupportTicket> RequiredOwnedAsync(Guid userId, string number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > 40) throw new NotFoundException("Support ticket not found.");
        return await tickets.GetOwnedAsync(userId, number.Trim().ToUpperInvariant(), ct)
            ?? throw new NotFoundException("Support ticket not found.");
    }

    private async Task<SupportScreenshotDownload> OpenScreenshotAsync(SupportTicket ticket, CancellationToken ct)
    {
        if (ticket.ScreenshotPath is not { } key) throw new NotFoundException("Screenshot not found.");
        var stream = await storage.OpenReadAsync(key, ct) ?? throw new NotFoundException("Screenshot not found.");
        return new(stream, SupportScreenshotValidation.ContentType(Path.GetExtension(key)));
    }

    private async Task NotifyAsync(SupportTicket ticket, CancellationToken ct)
    {
        var supportEmail = supportSettings.SupportEmail?.Trim();
        if (!string.IsNullOrWhiteSpace(supportEmail))
        {
            var support = new User { Email = supportEmail };
            await SendAsync(support, "New CareerHarbor Support Ticket", $"Ticket: {ticket.TicketNumber}\nCategory: {ticket.Category}\n" +
                $"Name: {ticket.Name}\nEmail: {ticket.Email}\nUser: {(ticket.UserId is null ? "Guest" : "Authenticated")}\n" +
                $"Subject: {ticket.Subject}\nDescription: {ticket.Description}\nCreated UTC: {ticket.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)}",
                ticket.Id, "support", ct);
        }
        else EmailFailed(logger, ticket.Id, "support-unconfigured", null);
        var recipient = new User { Id = ticket.UserId ?? Guid.NewGuid(), Email = ticket.Email, FirstName = ticket.Name };
        await SendAsync(recipient, $"CareerHarbor Support Request – {ticket.TicketNumber}",
            $"Hi {ticket.Name},\n\nWe have received your support request.\nTicket Number: {ticket.TicketNumber}\n\n" +
            "Our team will review the issue and get back to you.\n\nCareerHarbor Support", ticket.Id, "requester", ct);
    }

    private async Task SendAsync(User recipient, string subject, string body, Guid ticketId, string audience, CancellationToken ct)
    {
        try
        {
            var result = await email.SendNotificationAsync(recipient,
                new Notification { UserId = recipient.Id, Title = subject, Message = body }, ct);
            if (result != EmailDeliveryResult.Sent) EmailFailed(logger, ticketId, audience, null);
        }
        catch (Exception) { EmailFailed(logger, ticketId, audience, null); }
    }

    private static SupportTicketResponse ToUserResponse(SupportTicket x) => new(x.TicketNumber, x.Category, x.Subject,
        x.Description, x.Status, x.ScreenshotPath is not null, x.CreatedAtUtc, x.UpdatedAtUtc, x.ResolvedAtUtc);

    private static AdminSupportTicketResponse ToAdminResponse(SupportTicket x) => new(x.Id, x.UserId, x.Name, x.Email,
        x.TicketNumber, x.Category, x.Subject, x.Description, x.Status, x.AdminNotes,
        x.ScreenshotPath is not null, x.CreatedAtUtc, x.UpdatedAtUtc, x.ResolvedAtUtc);
}
