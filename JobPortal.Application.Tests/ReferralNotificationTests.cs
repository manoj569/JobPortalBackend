using JobPortal.Application.Features.Referrals;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ReferralNotificationTests
{
    [Fact]
    public void Phase3aUsesExistingSchemaWithoutMigration()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only", o => o.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var prior = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        Assert.Empty(db.GetService<IMigrationsModelDiffer>().GetDifferences(prior.GetRelationalModel(), current.GetRelationalModel()));
    }
    [Theory]
    [InlineData(NotificationSource.ReferralRequested, "/dashboard/referral-requests", "Sign in")]
    [InlineData(NotificationSource.ReferralAccepted, "/dashboard/my-referral-requests", "not yet")]
    [InlineData(NotificationSource.ReferralRequestRejected, "/dashboard/my-referral-requests", "declined")]
    [InlineData(NotificationSource.ReferralSubmitted, "/dashboard/my-referral-requests", "does not confirm")]
    [InlineData(NotificationSource.ReferralConfirmed, "/dashboard/referral-requests", "candidate confirmation")]
    public async Task LifecycleIntentsUseAccurateStatusCorrectRecipientAndNoPrivatePayload(NotificationSource source, string route, string wording)
    {
        await using var f = await ReferralFixture.CreateAsync();
        var request = await f.Service.CreateAsync(f.CandidateId, f.ReferralId, new("private candidate message"), default);
        if (source == NotificationSource.ReferralRequestRejected)
            await f.Service.RejectAsync(f.ReferrerId, request.Id, new("private rejection note"), default);
        else if (source != NotificationSource.ReferralRequested)
            await f.Service.AcceptAsync(f.ReferrerId, request.Id, default);
        if (source is NotificationSource.ReferralSubmitted or NotificationSource.ReferralConfirmed)
            await f.Service.SubmitAsync(f.ReferrerId, request.Id, new("private employer reference"), default);
        if (source == NotificationSource.ReferralConfirmed) await f.Service.ConfirmAsync(f.CandidateId, request.Id, default);
        var deliveries = await f.Db.NotificationDeliveries.Where(d => d.Source == source).ToArrayAsync();
        Assert.Equal(2, deliveries.Length);
        Assert.All(deliveries, d =>
        {
            Assert.Equal(source is NotificationSource.ReferralRequested or NotificationSource.ReferralConfirmed ? f.ReferrerId : f.CandidateId, d.UserId);
            Assert.Equal(route, d.ActionUrl);
            Assert.Contains("Employer", d.Message);
            Assert.Contains(wording, d.Message);
            Assert.DoesNotContain("private", d.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@", d.Message);
            Assert.DoesNotContain("http", d.Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(NotificationType.System, ReferralNotifications.Type(source));
        });
    }

    [Fact]
    public async Task ExistingEffectiveExpiryNotifiesBothParticipantsOnceWithoutChangingStatus()
    {
        await using var f = await ReferralFixture.CreateAsync();
        var request = await f.Request();
        f.Clock.Utc = request.ExpiresAtUtc;
        var scheduler = new ReferralNotificationScheduler(f.Db, f.Clock, Options.Create(new ReferralNotificationOptions()));
        await scheduler.EnqueueDueAsync(default);
        await scheduler.EnqueueDueAsync(default);
        var expiry = await f.Db.NotificationDeliveries.Where(d => d.Source == NotificationSource.ReferralExpired).ToArrayAsync();
        Assert.Equal(4, expiry.Length);
        Assert.Equal(2, expiry.Select(d => d.UserId).Distinct().Count());
        var row = await f.Db.ReferralRequests.AsNoTracking().SingleAsync();
        Assert.Equal(ReferralRequestStatus.Requested, row.Status);
        Assert.Equal(ReferralRequestStatus.Expired, row.EffectiveStatus(f.Clock.Utc));
        Assert.False(await f.Db.NotificationDeliveries.AnyAsync(d => d.Source == NotificationSource.ReferralRequestReminder));
    }

    [Fact]
    public async Task ConfiguredRemindersAreIntervalDeduplicatedAndStopOnAcceptance()
    {
        await using var f = await ReferralFixture.CreateAsync();
        var request = await f.Request();
        var scheduler = new ReferralNotificationScheduler(f.Db, f.Clock,
            Options.Create(new ReferralNotificationOptions { PendingReminderAgeHours = 12, ReminderIntervalHours = 8 }));
        f.Clock.Utc = request.RequestedAtUtc.AddHours(12);
        await scheduler.EnqueueDueAsync(default);
        await scheduler.EnqueueDueAsync(default);
        Assert.Equal(2, await f.Db.NotificationDeliveries.CountAsync(d => d.Source == NotificationSource.ReferralRequestReminder));
        f.Clock.Utc = request.RequestedAtUtc.AddHours(20);
        await scheduler.EnqueueDueAsync(default);
        Assert.Equal(4, await f.Db.NotificationDeliveries.CountAsync(d => d.Source == NotificationSource.ReferralRequestReminder));
        await f.Service.AcceptAsync(f.ReferrerId, request.Id, default);
        f.Clock.Utc = request.RequestedAtUtc.AddHours(28);
        await scheduler.EnqueueDueAsync(default);
        Assert.Equal(4, await f.Db.NotificationDeliveries.CountAsync(d => d.Source == NotificationSource.ReferralRequestReminder));
        var delivery = await f.Db.NotificationDeliveries.FirstAsync(d => d.Source == NotificationSource.ReferralRequestReminder);
        delivery.Status = NotificationDeliveryStatus.Processing; delivery.LeaseOwner = Guid.NewGuid(); delivery.LeaseExpiresAtUtc = f.Clock.Utc.AddMinutes(3);
        await f.Db.SaveChangesAsync();
        Assert.False(await new NotificationDeliveryRepository(f.Db).IsEligibleAsync(delivery, f.Clock.Utc, default));
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("expired")]
    public async Task DefaultReminderStartsAt24HoursAndStopsWhenNoLongerPending(string outcome)
    {
        await using var f = await ReferralFixture.CreateAsync();
        var request = await f.Request();
        Assert.Equal(request.RequestedAtUtc.AddHours(48), request.ExpiresAtUtc);
        var scheduler = new ReferralNotificationScheduler(f.Db, f.Clock, Options.Create(new ReferralNotificationOptions()));
        f.Clock.Utc = request.RequestedAtUtc.AddHours(24).AddTicks(-1);
        await scheduler.EnqueueDueAsync(default);
        Assert.False(await f.Db.NotificationDeliveries.AnyAsync(d => d.Source == NotificationSource.ReferralRequestReminder));
        f.Clock.Utc = request.RequestedAtUtc.AddHours(24);
        await scheduler.EnqueueDueAsync(default);
        await scheduler.EnqueueDueAsync(default);
        var reminders = await f.Db.NotificationDeliveries.Where(d => d.Source == NotificationSource.ReferralRequestReminder).ToArrayAsync();
        Assert.Equal(2, reminders.Length);
        Assert.All(reminders, d => { Assert.Equal(f.ReferrerId, d.UserId); Assert.Equal(f.Clock.Utc, d.ScheduledForUtc); });
        if (outcome == "accepted") await f.Service.AcceptAsync(f.ReferrerId, request.Id, default);
        if (outcome == "rejected") await f.Service.RejectAsync(f.ReferrerId, request.Id, new("private note"), default);
        f.Clock.Utc = request.ExpiresAtUtc;
        await scheduler.EnqueueDueAsync(default);
        Assert.Equal(2, await f.Db.NotificationDeliveries.CountAsync(d => d.Source == NotificationSource.ReferralRequestReminder));
        var delivery = reminders[0];
        delivery.Status = NotificationDeliveryStatus.Processing;
        delivery.LeaseOwner = Guid.NewGuid();
        delivery.LeaseExpiresAtUtc = f.Clock.Utc.AddMinutes(3);
        await f.Db.SaveChangesAsync();
        Assert.False(await new NotificationDeliveryRepository(f.Db).IsEligibleAsync(delivery, f.Clock.Utc, default));
    }

    [Fact]
    public async Task FailedBusinessSaveCannotMakeEmailIntentsVisible()
    {
        var interceptor = new FailSave();
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor).Options;
        await using var f = await ReferralFixture.CreateAsync(db: new JobPortalDbContext(options));
        interceptor.Fail = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Request());
        await using var committed = new JobPortalDbContext(options);
        Assert.Empty(await committed.ReferralRequests.ToArrayAsync());
        Assert.Empty(await committed.NotificationDeliveries.ToArrayAsync());
    }

    [Fact]
    public async Task AdministratorRecipientsExcludeInactiveDeletedAndNonAdminAccounts()
    {
        await using var f = await ReferralFixture.CreateAsync();
        var role = new Role { Name = "Administrator" };
        var active = new User { Role = role, Email = "admin@example.test", Status = UserStatus.Active };
        f.Db.AddRange(role, active, new User { Role = role, Status = UserStatus.Suspended }, new User { Role = role, Status = UserStatus.Active, IsDeleted = true });
        await f.Db.SaveChangesAsync();
        Assert.Equal(active.Id, Assert.Single(await new JobReferralRepository(f.Db).ActiveAdministratorIdsAsync(default)));
        var referral = await f.Db.JobReferrals.SingleAsync(); referral.ApprovalStatus = JobReferralApprovalStatus.Pending;
        var delivery = new NotificationDelivery { Source = NotificationSource.ReferralJobSubmitted, SourceId = referral.Id, UserId = active.Id,
            Status = NotificationDeliveryStatus.Processing, LeaseOwner = Guid.NewGuid(), LeaseExpiresAtUtc = f.Clock.Utc.AddMinutes(3) };
        f.Db.Add(delivery); await f.Db.SaveChangesAsync();
        var repository = new NotificationDeliveryRepository(f.Db);
        Assert.True(await repository.IsEligibleAsync(delivery, f.Clock.Utc, default));
        active.RoleId = (await f.Db.Users.AsNoTracking().SingleAsync(u => u.Id == f.CandidateId)).RoleId;
        await f.Db.SaveChangesAsync();
        Assert.False(await repository.IsEligibleAsync(delivery, f.Clock.Utc, default));
    }

    [Theory]
    [InlineData("Duplicate posting", true)]
    [InlineData("Contact private@example.test", false)]
    [InlineData("<script>secret</script>", false)]
    public void OnlySafeExistingAdministratorReasonIsIncluded(string reason, bool included)
    {
        var referral = new JobReferral { RejectionReason = reason };
        var job = new Job { Title = "Engineer", Company = new Company { Name = "Employer" } };
        var content = ReferralNotifications.Job(referral, job, NotificationSource.ReferralRejected);
        Assert.Equal(included, content.Message.Contains(reason, StringComparison.Ordinal));
        Assert.Contains("not approved", content.Message);
    }

    [Theory]
    [InlineData("/admin/referrals", true)]
    [InlineData("/dashboard/admin/referrals", true)]
    [InlineData("//evil.test", false)]
    [InlineData("/admin/referrals?token=secret", false)]
    [InlineData("/admin/../public", false)]
    public void AdministratorRouteConfigurationRejectsTokensHostsAndTraversal(string path, bool valid) =>
        Assert.Equal(valid, new ReferralNotificationOptions { AdminApprovalPath = path }.IsValid());

    private sealed class FailSave : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Fail ? throw new DbUpdateException("simulated failure") : ValueTask.FromResult(result);
    }
}
