using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerSessionReminderProcessor(ICareerSessionRepository repository, IDashboardRepository notifications, TimeProvider clock)
{
    public async Task<int> ProcessAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var count = 0;
        foreach (var reminder in await repository.DueAsync(now, ct))
        {
            var session = reminder.Session;
            var payment = await repository.PaymentAsync(session.BookingId, ct);
            if (session.IsDeleted || session.Status is not (CareerSessionStatus.Scheduled or CareerSessionStatus.Ready) ||
                now >= session.ScheduledStartUtc || payment is null || !CareerSessionService.Eligible(payment))
                reminder.Status = CareerReminderStatus.Cancelled;
            else
            {
                // Deterministic notification ID + reminder CAS + one transaction prevent duplicate delivery.
                await notifications.AddNotificationAsync(new Notification
                {
                    Id = reminder.Id, UserId = reminder.RecipientUserId, Title = "Career guidance session reminder",
                    Message = "Your career guidance session is approaching. Open your authenticated session details to prepare.",
                    Type = NotificationType.System
                }, ct);
                reminder.Status = CareerReminderStatus.Delivered; reminder.SentAtUtc = now; count++;
                CareerSessionService.TouchEligibility(payment);
            }
            session.Revision = Guid.NewGuid(); reminder.Revision = Guid.NewGuid();
        }
        await repository.SaveAsync(ct);
        return count;
    }
}
