using System.Reflection;
using System.Text;
using System.Text.Json;
using JobPortal.API.Controllers;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Infrastructure.CareerGuidance;
using JobPortal.Infrastructure;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerSessionTests
{
    [Fact]
    public async Task UncertainProviderOutcomeIsSanitizedAndReconciledWithoutDuplicateCreate()
    {
        var provider = new UncertainProvider(); using var f = new Fixture(provider);
        var error = await Assert.ThrowsAsync<ConflictException>(() => f.Setup());
        Assert.DoesNotContain("https", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
        var entity = await f.Db.Set<CareerGuidanceSession>().SingleAsync();
        Assert.Null(entity.ProviderMeetingId);
        var s = await f.Service.ProvisionAsync(f.Candidate, entity.BookingId, CareerSessionAudience.Candidate, default);
        Assert.Equal(1, provider.Creates);
        s = await f.Service.ReconcileAsync(f.Finance.Admin.Id, s.Id, new(s.Revision), default);
        Assert.Equal(1, provider.Creates); Assert.Equal(1, provider.Lookups); Assert.NotNull(s.ProviderMeetingId);
        Assert.DoesNotContain("https", JsonSerializer.Serialize(f.Audit.Events), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PendingRemindersDoNotSendForRefundReviewOrSuspendedConsultant()
    {
        using var f = new Fixture(); var s = await f.Setup(true); f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(-60);
        var payment = await f.Db.Set<CareerGuidancePayment>().SingleAsync(); payment.RequiresRefundReview = true;
        await f.Db.SaveChangesAsync();
        var processor = new CareerSessionReminderProcessor(f.Repository, new DashboardRepository(f.Db, f.Clock), f.Clock);
        Assert.Equal(0, await processor.ProcessAsync(default)); Assert.Empty(await f.Db.Notifications.ToArrayAsync());
        Assert.Null((await f.Service.JoinAsync(f.Candidate, s.BookingId, CareerSessionAudience.Candidate, true, default)).JoinUrl);
    }

    [Fact]
    public async Task RefundAfterCompletionClearsEarningReleaseTimestamp()
    {
        using var f = new Fixture(); var s = await f.Setup(true); f.Clock.Utc = s.ScheduledStartUtc;
        s = await f.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default); f.Clock.Utc = s.ScheduledEndUtc;
        await f.Service.CompleteAsync(f.Owner, s.Id, new(s.Revision), default);
        var payment = await f.Db.Set<CareerGuidancePayment>().SingleAsync(); Assert.NotNull(payment.Earning!.AvailableAtUtc);
        f.Finance.Gateway.RefundState = "pending";
        await f.Finance.Service.RefundAsync(f.Finance.Admin.Id, payment.Id, new(CareerRefundReason.AdminCorrection), default);
        Assert.Null(payment.Earning.AvailableAtUtc); Assert.Equal(CareerEarningStatus.Pending, payment.Earning.Status);
    }

    [Fact]
    public void ConfiguredArrayValuesReplaceRatherThanAppendDefaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CareerGuidance:ReminderOffsetsMinutes:0"] = "20",
            ["CareerGuidance:AllowedMeetingHosts:0"] = "meet.example.test"
        }).Build();
        var services = new ServiceCollection(); services.AddInfrastructure(configuration);
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<CareerSessionOptions>>().Value;
        Assert.Equal(new[] { 20 }, options.ReminderOffsetsMinutes);
        Assert.Equal(new[] { "meet.example.test" }, options.AllowedMeetingHosts);
    }

    [Fact]
    public async Task ProvisioningIsIdempotentAndRemindersAreUniqueFutureOnly()
    {
        using var f = new Fixture(); var session = await f.Setup();
        var repeat = await f.Service.ProvisionAsync(f.Candidate, session.BookingId, CareerSessionAudience.Candidate, default);
        Assert.Equal(session.Id, repeat.Id); Assert.Single(await f.Db.Set<CareerGuidanceSession>().ToArrayAsync());
        var reminders = await f.Db.Set<CareerGuidanceSessionReminder>().ToArrayAsync();
        Assert.Equal(4, reminders.Length); Assert.All(reminders, r => Assert.True(r.ScheduledForUtc >= f.Clock.Utc));
        Assert.Equal(4, reminders.Select(r => (r.SessionId, r.RecipientUserId, r.OffsetMinutes)).Distinct().Count());
        Assert.All(reminders, r => Assert.Contains(r.OffsetMinutes, new[] { 60, 10 }));
    }

    [Fact]
    public async Task UnpaidAndLegacyBookingsCannotProvision()
    {
        using var f = new Fixture(); var booking = await f.Finance.Setup();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ProvisionAsync(f.Candidate, booking.Id, CareerSessionAudience.Candidate, default));
        await f.Finance.Order(booking.Id);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ProvisionAsync(f.Candidate, booking.Id, CareerSessionAudience.Candidate, default));
        Assert.Empty(await f.Db.Set<CareerGuidanceSession>().ToArrayAsync());
    }

    [Theory]
    [InlineData(-11, false)]
    [InlineData(-10, true)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(60, false)]
    public async Task JoinWindowUsesUtcAndNeverReturnsCandidateHostUrl(int minutes, bool allowed)
    {
        using var f = new Fixture(); var s = await f.Setup(true); f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(minutes);
        var candidate = await f.Service.JoinAsync(f.Candidate, s.BookingId, CareerSessionAudience.Candidate, true, default);
        var consultant = await f.Service.JoinAsync(f.Owner, s.Id, CareerSessionAudience.Consultant, false, default);
        Assert.Equal(allowed ? Fixture.ParticipantUrl : null, candidate.JoinUrl);
        Assert.Equal(allowed ? Fixture.HostUrl : null, consultant.JoinUrl);
        var admin = await f.Service.JoinAsync(f.Finance.Admin.Id, s.Id, CareerSessionAudience.Administrator, false, default);
        Assert.Null(admin.JoinUrl);
        Assert.DoesNotContain("https", JsonSerializer.Serialize(s), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LinksAreEncryptedAndBoundToSessionAndRole()
    {
        using var f = new Fixture(); var s = await f.Setup(true);
        var entity = await f.Db.Set<CareerGuidanceSession>().SingleAsync();
        Assert.DoesNotContain(Fixture.ParticipantUrl, Encoding.UTF8.GetString(entity.ProtectedParticipantUrl!), StringComparison.Ordinal);
        Assert.Throws<ConflictException>(() => f.Protector.Unprotect(Guid.NewGuid(), false, entity.ProtectedParticipantUrl!));
        Assert.Throws<ConflictException>(() => f.Protector.Unprotect(s.Id, true, entity.ProtectedParticipantUrl!));
        Assert.Null(entity.CandidateJoinedAtUtc); Assert.Null(entity.ConsultantJoinedAtUtc);
    }

    [Theory]
    [InlineData("http://meet.google.com/test")]
    [InlineData("https://meet.google.com.evil.test/token")]
    [InlineData("https://user:secret@meet.google.com/test")]
    [InlineData("https://127.0.0.1/test")]
    [InlineData("https://meet.google.com:444/test")]
    public async Task ManualLinksRejectUnsafeHostsWithoutEchoingInput(string url)
    {
        using var f = new Fixture(); var s = await f.Setup();
        var error = await Assert.ThrowsAsync<BadRequestException>(() => f.Service.ConfigureAsync(f.Finance.Admin.Id, s.Id, new(s.Revision, url, null), default));
        Assert.DoesNotContain(url, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnershipAndAdminAuthorizationAreEnforcedInService()
    {
        using var f = new Fixture(); var s = await f.Setup(true);
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.GetAsync(Guid.NewGuid(), s.Id, CareerSessionAudience.Candidate, false, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.Owner, s.Id, CareerSessionAudience.Candidate, false, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.Candidate, s.Id, CareerSessionAudience.Consultant, false, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.StartAsync(f.Candidate, s.Id, new(s.Revision), default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.CompleteAsync(f.Finance.Scheduling.Other.Id, s.Id, new(s.Revision), default));
        await Assert.ThrowsAsync<AppException>(() => f.Service.ConfigureAsync(f.Candidate, s.Id, new(s.Revision, Fixture.ParticipantUrl, null), default));
        Assert.Empty((await f.Service.ListAsync(f.Finance.Scheduling.Other.Id, false, new(), default)).Items);
        Assert.Single((await f.Service.ListAsync(f.Owner, false, new(), default)).Items);
        Assert.Single((await f.Service.ListAsync(f.Finance.Admin.Id, true, new(), default)).Items);
    }

    [Fact]
    public async Task CompletionUpdatesBookingAndSchedulesPendingEarningWithoutPayout()
    {
        using var f = new Fixture(); var s = await f.Setup(true);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default));
        f.Clock.Utc = s.ScheduledStartUtc;
        s = await f.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default);
        var duplicateStart = await f.Service.StartAsync(f.Owner, s.Id, new(Guid.Empty), default);
        Assert.Equal(s.StartedAtUtc, duplicateStart.StartedAtUtc);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.CompleteAsync(f.Owner, s.Id, new(s.Revision), default));
        f.Clock.Utc = s.ScheduledEndUtc;
        s = await f.Service.CompleteAsync(f.Owner, s.Id, new(s.Revision), default);
        Assert.Equal(CareerSessionStatus.Completed, s.Status); Assert.Equal(f.Clock.Utc, s.CompletedAtUtc);
        Assert.Equal(CareerBookingStatus.Completed, (await f.Db.CareerGuidanceBookings.SingleAsync()).Status);
        var earning = await f.Db.Set<CareerGuidanceEarning>().SingleAsync();
        Assert.Equal(CareerEarningStatus.Pending, earning.Status); Assert.Null(earning.SettledAtUtc);
        Assert.Equal(f.Clock.Utc.AddHours(48), earning.AvailableAtUtc);
        var again = await f.Service.CompleteAsync(f.Owner, s.Id, new(Guid.Empty), default);
        Assert.Equal(s.CompletedAtUtc, again.CompletedAtUtc);
        Assert.Null((await f.Service.JoinAsync(f.Candidate, s.BookingId, CareerSessionAudience.Candidate, true, default)).JoinUrl);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default));
        Assert.All(await f.Db.Set<CareerGuidanceSessionReminder>().ToArrayAsync(), r => Assert.Equal(CareerReminderStatus.Cancelled, r.Status));
    }

    [Fact]
    public async Task InProgressJoinUsesEndGraceButReadyLateStartIsRejected()
    {
        using var f = new Fixture(); var s = await f.Setup(true);
        f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(31);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default));
        f.Clock.Utc = s.ScheduledStartUtc; await f.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default);
        f.Clock.Utc = s.ScheduledEndUtc.AddMinutes(15);
        Assert.NotNull((await f.Service.JoinAsync(f.Candidate, s.BookingId, CareerSessionAudience.Candidate, true, default)).JoinUrl);
        f.Clock.Utc = s.ScheduledEndUtc.AddMinutes(16);
        Assert.Null((await f.Service.JoinAsync(f.Candidate, s.BookingId, CareerSessionAudience.Candidate, true, default)).JoinUrl);
    }

    [Fact]
    public async Task CandidateNoShowRequiresGraceAndIsTerminalWithoutRefundOrRelease()
    {
        using var f = new Fixture(); var s = await f.Setup(true);
        f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(14);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.NoShowAsync(f.Owner, s.Id, new(s.Revision), false, default));
        f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(15);
        s = await f.Service.NoShowAsync(f.Owner, s.Id, new(s.Revision), false, default);
        Assert.Equal(CareerSessionStatus.CandidateNoShow, s.Status);
        Assert.Equal(CareerBookingStatus.NoShowCandidate, (await f.Db.CareerGuidanceBookings.SingleAsync()).Status);
        Assert.Null((await f.Db.Set<CareerGuidanceEarning>().SingleAsync()).AvailableAtUtc);
        Assert.False((await f.Db.Set<CareerGuidancePayment>().SingleAsync()).RequiresRefundReview);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.CompleteAsync(f.Owner, s.Id, new(s.Revision), default));
    }

    [Fact]
    public async Task CandidateReportHasNoFinancialEffectUntilAdminConfirms()
    {
        using var f = new Fixture(); var s = await f.Setup();
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ReportAsync(f.Candidate, s.BookingId, new(s.Revision), default));
        f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(15);
        s = await f.Service.ReportAsync(f.Candidate, s.BookingId, new(s.Revision), default);
        Assert.Equal(CareerSessionStatus.Scheduled, s.Status); Assert.NotNull(s.ConsultantNoShowReportedAtUtc);
        var payment = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
        Assert.False(payment.RequiresRefundReview); Assert.Equal(CareerPaymentStatus.Captured, payment.Status);
        await Assert.ThrowsAsync<AppException>(() => f.Service.NoShowAsync(f.Candidate, s.Id, new(s.Revision), true, default));
        s = await f.Service.NoShowAsync(f.Finance.Admin.Id, s.Id, new(s.Revision), true, default);
        Assert.Equal(CareerSessionStatus.ConsultantNoShow, s.Status); Assert.True(payment.RequiresRefundReview);
        Assert.Empty(await f.Db.Set<CareerGuidanceRefund>().ToArrayAsync());
        Assert.Equal(CareerEarningStatus.Pending, payment.Earning!.Status); Assert.Null(payment.Earning.AvailableAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrRefundDisablesSessionAndReminders(bool refund)
    {
        using var f = new Fixture(); var s = await f.Setup(true);
        var booking = await f.Db.CareerGuidanceBookings.SingleAsync();
        if (refund)
        {
            var payment = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
            await f.Finance.Service.RefundAsync(f.Finance.Admin.Id, payment.Id, new(CareerRefundReason.AdminCorrection), default);
        }
        else await f.Finance.Scheduling.Service.CancelAsync(f.Candidate, booking.Id, false, new(booking.Revision, null), default);
        Assert.Equal(CareerSessionStatus.Cancelled, (await f.Db.Set<CareerGuidanceSession>().SingleAsync()).Status);
        Assert.All(await f.Db.Set<CareerGuidanceSessionReminder>().ToArrayAsync(), r => Assert.Equal(CareerReminderStatus.Cancelled, r.Status));
        f.Clock.Utc = s.ScheduledStartUtc;
        Assert.Null((await f.Service.JoinAsync(f.Candidate, s.BookingId, CareerSessionAudience.Candidate, true, default)).JoinUrl);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.CompleteAsync(f.Owner, s.Id, new(s.Revision), default));
    }

    [Fact]
    public async Task PaidBookingCannotBypassSessionLifecycleThroughLegacyStatusEndpoint()
    {
        using var f = new Fixture(); var s = await f.Setup(); f.Clock.Utc = s.ScheduledEndUtc;
        var b = await f.Db.CareerGuidanceBookings.SingleAsync();
        await Assert.ThrowsAsync<ConflictException>(() => f.Finance.Scheduling.Service.SetStatusAsync(f.Owner, b.Id, new(b.Revision, CareerBookingStatus.Completed), default));
    }

    [Fact]
    public async Task RemindersDeliverOnceToExistingInboxWithoutMeetingSecrets()
    {
        using var f = new Fixture(); var s = await f.Setup(true); f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(-60);
        var processor = new CareerSessionReminderProcessor(f.Repository, new DashboardRepository(f.Db, f.Clock), f.Clock);
        Assert.Equal(2, await processor.ProcessAsync(default)); Assert.Equal(0, await processor.ProcessAsync(default));
        var notifications = await f.Db.Set<Notification>().ToArrayAsync(); Assert.Equal(2, notifications.Length);
        Assert.DoesNotContain("https", JsonSerializer.Serialize(notifications), StringComparison.OrdinalIgnoreCase);
        Assert.All(await f.Db.Set<CareerGuidanceSessionReminder>().Where(r => r.OffsetMinutes == 60).ToArrayAsync(), r =>
        { Assert.Equal(CareerReminderStatus.Delivered, r.Status); Assert.NotNull(r.SentAtUtc); });
        f.Clock.Utc = s.ScheduledStartUtc;
        Assert.Equal(0, await processor.ProcessAsync(default)); Assert.Equal(2, await f.Db.Set<Notification>().CountAsync());
    }

    [Fact]
    public async Task CompetingSessionRevisionsCannotBothCommit()
    {
        using var f = new Fixture(); var s = await f.Setup();
        using var a = new JobPortalDbContext(f.Finance.Scheduling.Options); using var b = new JobPortalDbContext(f.Finance.Scheduling.Options);
        var ra = new CareerSessionRepository(a); var rb = new CareerSessionRepository(b);
        var first = (await ra.GetAsync(s.Id, default))!; var second = (await rb.GetAsync(s.Id, default))!;
        first.Revision = Guid.NewGuid(); second.Revision = Guid.NewGuid(); await ra.SaveAsync(default);
        await Assert.ThrowsAsync<ConflictException>(() => rb.SaveAsync(default));
    }

    [Fact]
    public void AuthorizationMetadataAndDefaultsAreSafe()
    {
        Assert.Equal("Candidate", typeof(CareerCandidateSessionsController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("Administrator", typeof(AdminCareerSessionsController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.NotNull(typeof(CareerConsultantSessionsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.True(typeof(CareerCandidateSessionsController).GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.DoesNotContain(typeof(CareerSessionResponse).GetProperties(), p => p.Name.Contains("Url", StringComparison.OrdinalIgnoreCase));
        Assert.False(new CareerSessionOptions().SessionRemindersEnabled); Assert.True(new CareerSessionOptions().IsValid());
        Assert.False(new CareerSessionOptions { EarningReleaseDelayHours = 0 }.IsValid());
        Assert.False(new CareerSessionOptions { ReminderOffsetsMinutes = [10, 10] }.IsValid());
    }

    internal sealed class Fixture : IDisposable
    {
        internal const string ParticipantUrl = "https://meet.google.com/test?token=participant-private";
        internal const string HostUrl = "https://meet.google.com/test?token=host-private";
        public CareerFinanceTests.Fixture Finance { get; } = new();
        public JobPortalDbContext Db => Finance.Db;
        public Guid Candidate => Finance.Candidate;
        public Guid Owner => Finance.Scheduling.Owner.Id;
        public CareerSchedulingTests.TestClock Clock => Finance.Scheduling.Clock;
        public CareerSessionRepository Repository { get; }
        public CareerMeetingProtector Protector { get; } = new(new EphemeralDataProtectionProvider());
        public AuditWriterTestDouble Audit { get; } = new();
        public CareerSessionService Service { get; }
        public Fixture(ICareerMeetingProvider? provider = null)
        {
            Repository = new(Db);
            Service = new(Repository, new UserRepository(Db), Audit, provider ?? new ManualCareerMeetingProvider(), Protector, Clock, Options.Create(new CareerSessionOptions()));
        }
        public async Task<CareerSessionResponse> Setup(bool links = false)
        {
            var b = await Finance.Setup(); var order = await Finance.Order(b.Id); await Finance.Verify(b.Id, order);
            var s = await Service.ProvisionAsync(Candidate, b.Id, CareerSessionAudience.Candidate, default);
            return links ? await Service.ConfigureAsync(Finance.Admin.Id, s.Id, new(s.Revision, ParticipantUrl, HostUrl), default) : s;
        }
        public void Dispose() => Finance.Dispose();
    }

    private sealed class UncertainProvider : ICareerMeetingProvider
    {
        public string Name => "Manual";
        public int Creates { get; private set; }
        public int Lookups { get; private set; }
        public Task<CareerMeeting> CreateAsync(Guid sessionId, DateTime startUtc, DateTime endUtc, CancellationToken ct)
        { Creates++; throw new InvalidOperationException("Simulated private URL https://meet.google.com/token=secret"); }
        public Task<CareerMeeting?> GetAsync(Guid sessionId, CancellationToken ct)
        { Lookups++; return Task.FromResult<CareerMeeting?>(new("Manual", $"manual_{sessionId:N}", null, null)); }
    }
}
