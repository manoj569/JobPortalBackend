using System.Security.Claims;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using Microsoft.AspNetCore.SignalR;

namespace JobPortal.API.Hubs;

public sealed class NotificationRealtime(IHubContext<ExternalSessionCaptureHub> hub) : INotificationRealtime
{
    public Task PublishAsync(Notification notification, CancellationToken ct) => hub.Clients
        .User(notification.UserId.ToString("D")).SendAsync("NotificationReceived",
            new NotificationResponse(notification.Id, notification.Title, notification.Message, notification.Type,
                notification.ActionUrl, notification.IsRead, notification.ReadAtUtc, notification.CreatedAtUtc), ct);
}

public sealed class NotificationUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.Identity?.IsAuthenticated == true &&
        Guid.TryParse(connection.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id.ToString("D") : null;
}
