using System.Security.Cryptography;
using System.Text;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.Notifications;

public interface INotificationOutbox
{
    // Enlist only: the caller owns SaveChanges and the business transaction.
    void Add(IReadOnlyList<NotificationDelivery> deliveries);
}

public sealed class NotificationOutbox(INotificationOutbox repository, TimeProvider clock)
{
    public void Enqueue(NotificationSource source, Guid sourceId, Guid revision, Guid userId,
        string eventKey, string title, string message, string? actionUrl = null,
        DateTime? scheduledForUtc = null, Guid? notificationId = null)
    {
        var deliveries = Create(source, sourceId, revision, userId, eventKey, title, message,
            actionUrl, scheduledForUtc ?? clock.GetUtcNow().UtcDateTime, notificationId);
        repository.Add(deliveries);
    }

    public static IReadOnlyList<NotificationDelivery> Create(NotificationSource source, Guid sourceId,
        Guid revision, Guid userId, string eventKey, string title, string message, string? actionUrl,
        DateTime due, Guid? notificationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        if (eventKey.Length > 180 || title.Length is 0 or > 250 || message.Length is 0 or > 4000)
            throw new ArgumentException("Invalid notification template length.");
        if (due.Kind != DateTimeKind.Utc) throw new ArgumentException("Notification timestamps must be UTC.", nameof(due));
        if (!IsSafeActionUrl(actionUrl)) throw new ArgumentException("Notification action must be an internal route.", nameof(actionUrl));
        var key = $"{eventKey}:{userId:D}";
        var id = notificationId ?? DeterministicId(key);
        return new[] { NotificationChannel.InApp, NotificationChannel.Email }.Select(channel => new NotificationDelivery
        {
            Id = DeterministicId($"{key}:{channel}"), NotificationId = id, BusinessKey = key,
            Source = source, SourceId = sourceId, SourceRevision = revision, UserId = userId,
            Channel = channel, Title = title, Message = message, ActionUrl = actionUrl,
            ScheduledForUtc = due, NextAttemptAtUtc = due
        }).ToArray();
    }

    public static bool IsSafeActionUrl(string? url) => url is null ||
        url is "/dashboard/interview-insights" or "/dashboard/referrals";

    private static Guid DeterministicId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}
