using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;

namespace JobPortal.Application.Tests;

internal static class NotificationTestSupport
{
    public static NotificationOutbox Outbox(JobPortalDbContext db, TimeProvider clock) => new(new NotificationOutboxRepository(db), clock);
}

internal sealed class RecordingNotificationOutbox : INotificationOutbox
{
    public List<NotificationDelivery> Deliveries { get; } = [];
    public void Add(IReadOnlyList<NotificationDelivery> deliveries) => Deliveries.AddRange(deliveries);
}
