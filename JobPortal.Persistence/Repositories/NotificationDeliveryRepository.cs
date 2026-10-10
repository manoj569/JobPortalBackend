using JobPortal.Application.Features.Notifications;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class NotificationDeliveryRepository(JobPortalDbContext db) : INotificationDeliveryRepository
{
    public async Task<NotificationDelivery?> ClaimAsync(DateTime now, NotificationDeliveryOptions options, CancellationToken ct)
    {
        // Bounded optimistic compare-and-set, atomic on PostgreSQL. No process-local locks.
        var candidates = await db.NotificationDeliveries.AsNoTracking()
            .Where(x => x.ScheduledForUtc <= now && x.NextAttemptAtUtc <= now &&
                (x.Status == NotificationDeliveryStatus.Pending ||
                 x.Status == NotificationDeliveryStatus.Processing && x.LeaseExpiresAtUtc <= now) &&
                (x.Channel == NotificationChannel.InApp || db.Notifications.Any(n => n.Id == x.NotificationId && n.UserId == x.UserId) ||
                 db.NotificationDeliveries.Any(i => i.NotificationId == x.NotificationId && i.Channel == NotificationChannel.InApp &&
                    (i.Status == NotificationDeliveryStatus.Failed || i.Status == NotificationDeliveryStatus.Cancelled || i.Status == NotificationDeliveryStatus.Sent))))
            .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Channel).ThenBy(x => x.Id)
            .Take(options.BatchSize).ToArrayAsync(ct);
        foreach (var row in candidates)
        {
            var claim = db.NotificationDeliveries.Where(x => x.Id == row.Id &&
                x.AttemptCount == row.AttemptCount &&
                (x.Status == NotificationDeliveryStatus.Pending ||
                 x.Status == NotificationDeliveryStatus.Processing && x.LeaseExpiresAtUtc <= now));
            if (row.AttemptCount >= options.MaxAttempts)
            {
                await claim.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, NotificationDeliveryStatus.Failed)
                    .SetProperty(x => x.FailureCode, "attempts_exhausted").SetProperty(x => x.CompletedAtUtc, now)
                    .SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), ct);
                continue;
            }
            var owner = Guid.NewGuid();
            var expiry = now.AddSeconds(options.LeaseSeconds);
            if (await claim.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, NotificationDeliveryStatus.Processing)
                .SetProperty(x => x.LeaseOwner, owner).SetProperty(x => x.LeaseExpiresAtUtc, expiry)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct) == 0) continue;
            row.LeaseOwner = owner; row.LeaseExpiresAtUtc = expiry; row.AttemptCount++;
            row.Status = NotificationDeliveryStatus.Processing;
            return row;
        }
        return null;
    }

    private IQueryable<NotificationDelivery> Owned(NotificationDelivery row, DateTime now) => db.NotificationDeliveries
        .Where(x => x.Id == row.Id && x.Status == NotificationDeliveryStatus.Processing &&
            x.LeaseOwner == row.LeaseOwner && x.LeaseExpiresAtUtc > now);

    public async Task<bool> IsEligibleAsync(
        NotificationDelivery delivery,
        DateTime now,
        CancellationToken ct)
    {
        if (!await Owned(delivery, now).AnyAsync(ct) ||
            !await db.Users.AnyAsync(
                x => x.Id == delivery.UserId && x.Status == UserStatus.Active, ct))
            return false;

        switch (delivery.Source)
        {
            case NotificationSource.ReferralJobSubmitted:
                return await db.Users.AnyAsync(u => u.Id == delivery.UserId && u.Role.Name == "Administrator", ct) &&
                    await db.JobReferrals.AnyAsync(r => r.Id == delivery.SourceId && r.ApprovalStatus == JobReferralApprovalStatus.Pending, ct);
            case NotificationSource.ReferralRequestReminder:
                return await db.ReferralRequests.AnyAsync(r => r.Id == delivery.SourceId &&
                    r.ReferrerUserId == delivery.UserId && r.JobReferral.ReferrerUserId == delivery.UserId &&
                    r.Status == ReferralRequestStatus.Requested && r.ExpiresAtUtc > now, ct) &&
                    !await db.NotificationDeliveries.AnyAsync(d => d.Source == NotificationSource.ReferralRequestReminder &&
                        d.SourceId == delivery.SourceId && d.ScheduledForUtc > delivery.ScheduledForUtc, ct);
            case NotificationSource.ReferralExpired:
                return await db.ReferralRequests.AnyAsync(r => r.Id == delivery.SourceId &&
                    (r.CandidateUserId == delivery.UserId || r.ReferrerUserId == delivery.UserId && r.JobReferral.ReferrerUserId == delivery.UserId) &&
                    (r.Status == ReferralRequestStatus.Expired || r.Status == ReferralRequestStatus.Requested && r.ExpiresAtUtc <= now), ct);
            case NotificationSource.ReferralRequested:
            case NotificationSource.ReferralAccepted:
            case NotificationSource.ReferralRequestRejected:
            case NotificationSource.ReferralSubmitted:
            case NotificationSource.ReferralConfirmed:
            case NotificationSource.ReferralNotReceived:
                return await db.ReferralRequests.AnyAsync(r => r.Id == delivery.SourceId &&
                    ((delivery.Source == NotificationSource.ReferralRequested && r.ReferrerUserId == delivery.UserId && r.JobReferral.ReferrerUserId == delivery.UserId && r.Status == ReferralRequestStatus.Requested && r.ExpiresAtUtc > now) ||
                     (delivery.Source == NotificationSource.ReferralAccepted && r.CandidateUserId == delivery.UserId && r.AcceptedAtUtc != null) ||
                     (delivery.Source == NotificationSource.ReferralRequestRejected && r.CandidateUserId == delivery.UserId && r.RejectedAtUtc != null) ||
                     (delivery.Source == NotificationSource.ReferralSubmitted && r.CandidateUserId == delivery.UserId && r.ReferralSubmittedAtUtc != null) ||
                     (delivery.Source == NotificationSource.ReferralConfirmed && r.ReferrerUserId == delivery.UserId && r.CandidateConfirmedAtUtc != null) ||
                     (delivery.Source == NotificationSource.ReferralNotReceived && r.ReferrerUserId == delivery.UserId && r.NotReceivedAtUtc != null)), ct);
            case NotificationSource.MembershipPurchase:
                return await db.Payments.AnyAsync(p => p.Id == delivery.SourceId &&
                    p.UserId == delivery.UserId && p.Status == PaymentStatus.Paid && p.PaidAtUtc != null &&
                    p.MembershipId != null, ct);
            case NotificationSource.InterviewReminder:
                return await db.CandidateInterviewSchedules.AnyAsync(x =>
                    x.Id == delivery.SourceId &&
                    x.ReminderRevision == delivery.SourceRevision &&
                    x.ReminderRequested &&
                    x.CandidateId == delivery.UserId &&
                    x.Status == InterviewScheduleStatus.Scheduled &&
                    x.InterviewAtUtc > now, ct);

            case NotificationSource.CareerReminder:
                var reminder = await db.Set<CareerGuidanceSessionReminder>()
                    .AsNoTracking()
                    .Include(x => x.Session)
                    .SingleOrDefaultAsync(x => x.Id == delivery.SourceId, ct);

                if (reminder is null ||
                    reminder.RecipientUserId != delivery.UserId ||
                    reminder.Status != CareerReminderStatus.Delivered ||
                    reminder.Session.IsDeleted ||
                    reminder.Session.ScheduledStartUtc <= now ||
                    reminder.Session.Status is not
                        (CareerSessionStatus.Scheduled or CareerSessionStatus.Ready))
                    return false;

                // Every eligibility check must see fresh committed payment/account state, never a tracked prior read.
                var payment = await db.Set<CareerGuidancePayment>().IgnoreQueryFilters().AsNoTracking()
                    .Include(p => p.Booking).ThenInclude(b => b.Candidate)
                    .Include(p => p.Booking).ThenInclude(b => b.Consultant).ThenInclude(c => c.User)
                    .Include(p => p.Earning).Include(p => p.Refund).SingleOrDefaultAsync(p => p.BookingId == reminder.Session.BookingId, ct);
                return payment is not null && CareerSessionService.Eligible(payment);

            case NotificationSource.CareerConfirmation:
                return await db.Set<CareerGuidancePayment>().AnyAsync(p =>
                    p.BookingId == delivery.SourceId &&
                    p.PaidAtUtc != null &&
                    !p.RequiresRefundReview &&
                    p.Status == CareerPaymentStatus.Captured &&
                    !p.Booking.IsDeleted &&
                    p.Booking.Status == CareerBookingStatus.Confirmed &&
                    (p.Booking.CandidateUserId == delivery.UserId ||
                     p.Booking.Consultant.UserId == delivery.UserId), ct);

            case NotificationSource.ReferralApproved:
            case NotificationSource.ReferralRejected:
                var decision =
                    delivery.Source == NotificationSource.ReferralApproved
                        ? JobReferralApprovalStatus.Approved
                        : JobReferralApprovalStatus.Rejected;

                return await db.JobReferrals.AnyAsync(r =>
                    r.Id == delivery.SourceId &&
                    r.ReferrerUserId == delivery.UserId &&
                    r.ApprovalStatus == decision, ct);

            default:
                return false;
        }
    }
    public Task<User?> RecipientAsync(Guid userId, CancellationToken ct) => db.Users.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == userId && x.Status == UserStatus.Active, ct);

    public Task<Notification?> InboxAsync(Guid id, Guid userId, CancellationToken ct) => db.Notifications.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

    public async Task<Notification?> MaterializeAsync(
        NotificationDelivery delivery,
        DateTime now,
        CancellationToken ct)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Claim completion locks this delivery row; expired/stolen leases cannot publish.
            if (await Owned(delivery, now).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseOwner, delivery.LeaseOwner), ct) == 0)
                return null;
            if (!await IsEligibleAsync(delivery, now, ct))
            {
                await CompleteAsync(delivery, NotificationDeliveryStatus.Cancelled, now, now, "source_ineligible", ct);
                await transaction.CommitAsync(ct);
                return null;
            }
            var type = JobPortal.Application.Features.Referrals.ReferralNotifications.Type(delivery.Source);
            Guid? referralId = delivery.Source is NotificationSource.ReferralApproved or NotificationSource.ReferralRejected or NotificationSource.ReferralJobSubmitted ? delivery.SourceId : null;
            // PK/business-key constraints arbitrate retries, including unknown commit outcomes.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Notifications" ("Id", "UserId", "BusinessKey", "Title", "Message", "Type", "ActionUrl", "ReferralId", "IsRead", "CreatedAtUtc", "IsDeleted")
                VALUES ({delivery.NotificationId}, {delivery.UserId}, {delivery.BusinessKey}, {delivery.Title}, {delivery.Message}, {(int)type}, {delivery.ActionUrl}, {referralId}, FALSE, {now}, FALSE)
                ON CONFLICT DO NOTHING
                """, ct);
            await db.Notifications.Where(n => n.Id == delivery.NotificationId && n.UserId == delivery.UserId && n.BusinessKey == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.BusinessKey, delivery.BusinessKey), ct);
            await CompleteAsync(delivery, NotificationDeliveryStatus.Sent, now, now, null, ct);
            await transaction.CommitAsync(ct);
            return await InboxAsync(delivery.NotificationId, delivery.UserId, ct);
        });
    }

    public async Task CompleteAsync(NotificationDelivery delivery, NotificationDeliveryStatus status, DateTime now,
        DateTime nextAttempt, string? failureCode, CancellationToken ct) =>
        await Owned(delivery, now).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
            .SetProperty(x => x.NextAttemptAtUtc, nextAttempt).SetProperty(x => x.FailureCode, failureCode)
            .SetProperty(x => x.CompletedAtUtc, status == NotificationDeliveryStatus.Pending ? (DateTime?)null : now)
            .SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), ct);
}
