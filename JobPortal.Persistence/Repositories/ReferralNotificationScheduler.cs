using System.Globalization;
using JobPortal.Application.Features.Notifications;
using JobPortal.Application.Features.Referrals;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPortal.Persistence.Repositories;

public sealed class ReferralNotificationScheduler(JobPortalDbContext db, TimeProvider clock,
    IOptions<ReferralNotificationOptions> options) : IReferralNotificationScheduler
{
    public async Task EnqueueDueAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.IsValid()) throw new InvalidOperationException("Invalid referral notification settings.");
        var now = clock.GetUtcNow().UtcDateTime;
        var query = db.ReferralRequests.AsNoTracking().Include(r => r.JobReferral).ThenInclude(r => r.Job).ThenInclude(j => j.Company);
        var expired = await query.Where(r => (r.Status == ReferralRequestStatus.Requested && r.ExpiresAtUtc <= now ||
                r.Status == ReferralRequestStatus.Expired) &&
                !db.NotificationDeliveries.Any(d => d.Source == NotificationSource.ReferralExpired && d.SourceId == r.Id))
            .OrderBy(r => r.ExpiresAtUtc).ThenBy(r => r.Id).Take(settings.SweepBatchSize).ToArrayAsync(ct);
        var deliveries = new List<NotificationDelivery>();
        foreach (var request in expired)
        {
            Add(request, NotificationSource.ReferralExpired, request.CandidateUserId, "expired", now);
            Add(request, NotificationSource.ReferralExpired, request.ReferrerUserId, "expired", now);
        }
        if (settings.RemindersEnabled)
        {
            var cutoff = now.AddHours(-settings.PendingReminderAgeHours);
            var previousInterval = now.AddHours(-settings.ReminderIntervalHours);
            var pending = await query.Where(r => r.Status == ReferralRequestStatus.Requested && r.ExpiresAtUtc > now &&
                    r.RequestedAtUtc <= cutoff && !db.NotificationDeliveries.Any(d => d.Source == NotificationSource.ReferralRequestReminder &&
                        d.SourceId == r.Id && d.ScheduledForUtc > previousInterval))
                .OrderBy(r => r.RequestedAtUtc).ThenBy(r => r.Id).Take(settings.SweepBatchSize).ToArrayAsync(ct);
            foreach (var request in pending)
            {
                var start = request.RequestedAtUtc.AddHours(settings.PendingReminderAgeHours);
                var intervalTicks = TimeSpan.FromHours(settings.ReminderIntervalHours).Ticks;
                var slot = (now.Ticks - start.Ticks) / intervalTicks;
                Add(request, NotificationSource.ReferralRequestReminder, request.ReferrerUserId,
                    "reminder:" + slot.ToString(CultureInfo.InvariantCulture), start.AddTicks(slot * intervalTicks));
            }
        }
        if (deliveries.Count == 0) return;
        if (!db.Database.IsRelational())
        {
            foreach (var delivery in deliveries)
                if (!await db.NotificationDeliveries.AnyAsync(d => d.Id == delivery.Id, ct)) db.NotificationDeliveries.Add(delivery);
            await db.SaveChangesAsync(ct);
            return;
        }
        // Same durable table/keys as transactional producers. Concurrent workers/retries never duplicate an intent.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var d in deliveries)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "NotificationDeliveries" ("Id", "NotificationId", "UserId", "Channel", "Source", "SourceId", "SourceRevision",
                        "BusinessKey", "Title", "Message", "ActionUrl", "ScheduledForUtc", "NextAttemptAtUtc", "Status", "AttemptCount", "CreatedAtUtc", "IsDeleted")
                    VALUES ({d.Id}, {d.NotificationId}, {d.UserId}, {(int)d.Channel}, {(int)d.Source}, {d.SourceId}, {d.SourceRevision},
                        {d.BusinessKey}, {d.Title}, {d.Message}, {d.ActionUrl}, {d.ScheduledForUtc}, {d.NextAttemptAtUtc},
                        {(int)NotificationDeliveryStatus.Pending}, 0, {now}, FALSE)
                    ON CONFLICT DO NOTHING
                    """, ct);
            await transaction.CommitAsync(ct);
        });

        void Add(ReferralRequest request, NotificationSource source, Guid recipient, string name, DateTime due)
        {
            var content = ReferralNotifications.Request(request, source, recipient);
            deliveries.AddRange(NotificationOutbox.Create(source, request.Id, Guid.Empty, recipient,
                $"referral-request:{request.Id:D}:{name}", content.Title, content.Message, content.Route, due));
        }
    }
}
