using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class NotificationEligibilityTests
{
    [Theory]
    [InlineData("revision")]
    [InlineData("cancelled")]
    [InlineData("completed")]
    [InlineData("deleted")]
    [InlineData("disabled")]
    [InlineData("recipient")]
    [InlineData("lease")]
    [InlineData("expired")]
    public async Task ClaimedInterviewDeliveryRechecksSourceAndOwnership(string change)
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        var user = new User { Status = UserStatus.Active };
        var schedule = new CandidateInterviewSchedule { CandidateId = user.Id, InterviewAtUtc = now.AddHours(1), ReminderRequested = true };
        var row = new NotificationDelivery { Source = NotificationSource.InterviewReminder, SourceId = schedule.Id,
            SourceRevision = schedule.ReminderRevision, UserId = user.Id, Status = NotificationDeliveryStatus.Processing,
            LeaseOwner = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddMinutes(3) };
        db.AddRange(user, schedule, row); await db.SaveChangesAsync();
        var repository = new NotificationDeliveryRepository(db);
        Assert.True(await repository.IsEligibleAsync(row, now, default));
        switch (change)
        {
            case "revision": schedule.ReminderRevision = Guid.NewGuid(); break;
            case "cancelled": schedule.Status = InterviewScheduleStatus.Cancelled; break;
            case "completed": schedule.Status = InterviewScheduleStatus.InterviewCompleted; break;
            case "deleted": schedule.IsDeleted = true; break;
            case "disabled": schedule.ReminderRequested = false; break;
            case "recipient": schedule.CandidateId = Guid.NewGuid(); break;
            case "lease": row.LeaseExpiresAtUtc = now; break;
            case "expired": schedule.InterviewAtUtc = now; break;
        }
        await db.SaveChangesAsync();
        Assert.False(await repository.IsEligibleAsync(row, now, default));
    }

    [Fact]
    public async Task CareerReminderEligibilityDoesNotReusePreviouslyTrackedPayment()
    {
        using var f = new CareerSessionTests.Fixture();
        var session = await f.Setup(true); f.Clock.Utc = session.ScheduledStartUtc.AddMinutes(-60);
        var processor = new JobPortal.Application.Features.CareerGuidance.CareerSessionReminderProcessor(f.Repository,
            new DashboardRepository(f.Db, f.Clock), f.Clock, NotificationTestSupport.Outbox(f.Db, f.Clock));
        await processor.ProcessAsync(default);
        var row = await f.Db.NotificationDeliveries.FirstAsync(x => x.Source == NotificationSource.CareerReminder);
        row.Status = NotificationDeliveryStatus.Processing; row.LeaseOwner = Guid.NewGuid(); row.LeaseExpiresAtUtc = f.Clock.Utc.AddMinutes(3);
        await f.Db.SaveChangesAsync();
        var repository = new NotificationDeliveryRepository(f.Db);
        Assert.True(await repository.IsEligibleAsync(row, f.Clock.Utc, default));
        using var other = new JobPortalDbContext(f.Finance.Scheduling.Options);
        var payment = await other.Set<CareerGuidancePayment>().SingleAsync(); payment.RequiresRefundReview = true;
        await other.SaveChangesAsync();
        Assert.False(await repository.IsEligibleAsync(row, f.Clock.Utc, default));
    }

    [Theory]
    [InlineData(NotificationSource.ReferralApproved, JobReferralApprovalStatus.Approved)]
    [InlineData(NotificationSource.ReferralRejected, JobReferralApprovalStatus.Rejected)]
    public async Task ReferralDecisionAndRecipientAreRechecked(NotificationSource source, JobReferralApprovalStatus status)
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        var user = new User { Status = UserStatus.Active };
        var referral = new JobReferral { ReferrerUserId = user.Id, ApprovalStatus = status };
        var row = new NotificationDelivery { UserId = user.Id, Source = source, SourceId = referral.Id,
            Status = NotificationDeliveryStatus.Processing, LeaseOwner = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddMinutes(3) };
        db.AddRange(user, referral, row); await db.SaveChangesAsync();
        var repository = new NotificationDeliveryRepository(db);
        Assert.True(await repository.IsEligibleAsync(row, now, default));
        referral.ApprovalStatus = JobReferralApprovalStatus.Pending; await db.SaveChangesAsync();
        Assert.False(await repository.IsEligibleAsync(row, now, default));
    }
}
