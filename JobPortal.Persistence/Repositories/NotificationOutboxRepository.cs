using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;

namespace JobPortal.Persistence.Repositories;

public sealed class NotificationOutboxRepository(JobPortalDbContext db) : INotificationOutbox
{
    public void Add(IReadOnlyList<NotificationDelivery> deliveries)
    {
        foreach (var delivery in deliveries)
            if (!db.NotificationDeliveries.Local.Any(x => x.Id == delivery.Id))
                db.NotificationDeliveries.Add(delivery);
    }
}
