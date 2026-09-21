using JobPortal.API.HostedServices;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.CareerGuidance;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerSessionReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuspendedOrDeletedConsultantDoesNotBlockNoShowReportAndAdminReview(bool deleted)
    {
        using var f = new CareerSessionTests.Fixture(); var s = await f.Setup(true);
        var consultant = await f.Db.CareerConsultants.SingleAsync();
        consultant.VerificationStatus = ConsultantVerificationStatus.Suspended;
        consultant.IsDeleted = deleted; consultant.User.IsDeleted = deleted;
        await f.Db.SaveChangesAsync();
        f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(15);
        Assert.Null((await f.Service.JoinAsync(f.Candidate, s.BookingId, CareerSessionAudience.Candidate, true, default)).JoinUrl);
        s = await f.Service.ReportAsync(f.Candidate, s.BookingId, new(s.Revision), default);
        var payment = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
        Assert.False(payment.RequiresRefundReview);
        // An independent financial review flag also must not prevent objective admin confirmation.
        payment.RequiresRefundReview = true; payment.Revision = Guid.NewGuid(); await f.Db.SaveChangesAsync();
        s = await f.Service.NoShowAsync(f.Finance.Admin.Id, s.Id, new(s.Revision), true, default);
        Assert.Equal(CareerSessionStatus.ConsultantNoShow, s.Status);
        Assert.Equal(CareerBookingStatus.NoShowConsultant, payment.Booking.Status);
        Assert.True(payment.RequiresRefundReview); Assert.Null(payment.Earning!.AvailableAtUtc);
        Assert.Equal(CareerEarningStatus.Pending, payment.Earning.Status);
        Assert.Empty(await f.Db.Set<CareerGuidanceRefund>().ToArrayAsync());
    }

    [Fact]
    public async Task ProviderMeetingIdentifierCannotLeakAJoinUrlThroughMetadata()
    {
        var provider = new MutableProvider { UnsafeIdentifier = true };
        using var f = new CareerSessionTests.Fixture(provider);
        var error = await Assert.ThrowsAsync<ConflictException>(() => f.Setup());
        Assert.DoesNotContain("https", error.Message, StringComparison.OrdinalIgnoreCase);
        using var read = new JobPortalDbContext(f.Finance.Scheduling.Options);
        var intent = await read.Set<CareerGuidanceSession>().SingleAsync();
        Assert.Null(intent.ProviderMeetingId); Assert.Null(intent.MeetingCreatedAtUtc);
    }

    [Fact]
    public async Task ProviderRefreshWithoutHostLinkClearsOldHostCredential()
    {
        var provider = new MutableProvider(); using var f = new CareerSessionTests.Fixture(provider);
        var s = await f.Setup(); Assert.NotNull((await f.Db.Set<CareerGuidanceSession>().SingleAsync()).ProtectedHostUrl);
        provider.HostUrl = null;
        s = await f.Service.ReconcileAsync(f.Finance.Admin.Id, s.Id, new(s.Revision), default);
        Assert.Null((await f.Db.Set<CareerGuidanceSession>().SingleAsync()).ProtectedHostUrl);
        f.Clock.Utc = s.ScheduledStartUtc;
        Assert.Equal(CareerSessionTests.Fixture.ParticipantUrl,
            (await f.Service.JoinAsync(f.Owner, s.Id, CareerSessionAudience.Consultant, false, default)).JoinUrl);
    }

    [Fact]
    public async Task PaginationRejectsOverflowBeforeRepositoryQuery()
    {
        using var f = new CareerSessionTests.Fixture();
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.ListAsync(f.Owner, false, new(int.MaxValue, 100), default));
    }

    [Theory]
    [InlineData("consultant")]
    [InlineData("candidate")]
    [InlineData("session")]
    public async Task SoftDeletedDependenciesDoNotStrandPendingReminders(string deleted)
    {
        using var f = new CareerSessionTests.Fixture(); var s = await f.Setup();
        if (deleted == "consultant") (await f.Db.CareerConsultants.SingleAsync()).IsDeleted = true;
        if (deleted == "candidate") (await f.Db.Users.SingleAsync(u => u.Id == f.Candidate)).IsDeleted = true;
        if (deleted == "session") (await f.Db.Set<CareerGuidanceSession>().SingleAsync()).IsDeleted = true;
        await f.Db.SaveChangesAsync();
        f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(-10);
        using var scope = new JobPortalDbContext(f.Finance.Scheduling.Options);
        var processor = new CareerSessionReminderProcessor(new CareerSessionRepository(scope), new DashboardRepository(scope, f.Clock), f.Clock);
        Assert.Equal(0, await processor.ProcessAsync(default));
        Assert.Empty(await scope.Notifications.ToArrayAsync());
        Assert.All(await scope.Set<CareerGuidanceSessionReminder>().IgnoreQueryFilters().ToArrayAsync(), r => Assert.Equal(CareerReminderStatus.Cancelled, r.Status));
    }

    [Fact]
    public async Task DisabledReminderWorkerNeverCreatesADatabaseScope()
    {
        using var worker = new CareerSessionReminderHostedService(new ForbiddenScopeFactory(), Options.Create(new CareerSessionOptions()),
            NullLogger<CareerSessionReminderHostedService>.Instance, TimeProvider.System);
        await worker.StartAsync(default);
        // Wait for the disabled branch itself; stopping immediately can cancel BackgroundService
        // before its scheduled delegate runs under a busy full-suite thread pool.
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(default);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleCompletionCannotOverrideRefundOrConfirmedNoShow(bool refund)
    {
        using var f = new CareerSessionTests.Fixture(); var s = await f.Setup(true);
        f.Clock.Utc = s.ScheduledStartUtc;
        s = await f.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default);
        f.Clock.Utc = s.ScheduledEndUtc;
        using var stale = new JobPortalDbContext(f.Finance.Scheduling.Options);
        var repository = new CareerSessionRepository(stale);
        await repository.GetAsync(s.Id, default); await repository.PaymentAsync(s.BookingId, default);
        if (refund)
        {
            var payment = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
            await f.Finance.Service.RefundAsync(f.Finance.Admin.Id, payment.Id, new(CareerRefundReason.AdminCorrection), default);
        }
        else await f.Service.NoShowAsync(f.Finance.Admin.Id, s.Id, new(s.Revision), true, default);
        var service = Service(f, stale, repository);
        await Assert.ThrowsAsync<ConflictException>(() => service.CompleteAsync(f.Owner, s.Id, new(s.Revision), default));
        using var read = new JobPortalDbContext(f.Finance.Scheduling.Options);
        Assert.Equal(refund ? CareerSessionStatus.Cancelled : CareerSessionStatus.ConsultantNoShow,
            (await read.Set<CareerGuidanceSession>().SingleAsync()).Status);
        Assert.Null((await read.Set<CareerGuidanceEarning>().SingleAsync()).AvailableAtUtc);
    }

    [Fact]
    public async Task CompetingReminderBatchesCannotCreateDuplicateInboxRecords()
    {
        using var f = new CareerSessionTests.Fixture(); var s = await f.Setup();
        f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(-60);
        using var winner = new JobPortalDbContext(f.Finance.Scheduling.Options);
        var first = new CareerSessionReminderProcessor(new CareerSessionRepository(winner), new DashboardRepository(winner, f.Clock), f.Clock);
        var interceptor = new BeforeSaveInterceptor(async () => Assert.Equal(2, await first.ProcessAsync(default)));
        using var loser = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>(f.Finance.Scheduling.Options)
            .AddInterceptors(interceptor).Options);
        var second = new CareerSessionReminderProcessor(new CareerSessionRepository(loser), new DashboardRepository(loser, f.Clock), f.Clock);
        // Both workers have prepared the same pending rows before the winning commit.
        // InMemory can surface its duplicate primary key as ArgumentException; PostgreSQL
        // maps that uniqueness failure to ConflictException and rolls back the whole batch.
        var failure = await Record.ExceptionAsync(() => second.ProcessAsync(default));
        Assert.True(failure is ConflictException or ArgumentException, "The stale batch must not commit.");
        using var read = new JobPortalDbContext(f.Finance.Scheduling.Options);
        Assert.Equal(2, await read.Notifications.CountAsync());
        Assert.Equal(2, await read.Set<CareerGuidanceSessionReminder>().CountAsync(r => r.Status == CareerReminderStatus.Delivered));
        var retry = new CareerSessionReminderProcessor(new CareerSessionRepository(read), new DashboardRepository(read, f.Clock), f.Clock);
        Assert.Equal(0, await retry.ProcessAsync(default));
    }

    [Fact]
    public async Task ProviderResultAfterConcurrentCancellationCannotResurrectSession()
    {
        var provider = new PausedProvider(); using var f = new CareerSessionTests.Fixture(provider);
        var provision = f.Setup();
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancel = new JobPortalDbContext(f.Finance.Scheduling.Options);
        var booking = await cancel.CareerGuidanceBookings.SingleAsync();
        booking.Status = CareerBookingStatus.CancelledByCandidate; booking.Revision = Guid.NewGuid();
        await new CareerSchedulingRepository(cancel).SaveAsync(default);
        provider.Release.SetResult();
        await Assert.ThrowsAsync<ConflictException>(() => provision);
        using var read = new JobPortalDbContext(f.Finance.Scheduling.Options);
        var s = await read.Set<CareerGuidanceSession>().SingleAsync();
        Assert.Equal(CareerSessionStatus.Cancelled, s.Status); Assert.Null(s.ProtectedParticipantUrl);
        Assert.Equal(1, provider.Creates);
    }

    private static CareerSessionService Service(CareerSessionTests.Fixture f, JobPortalDbContext db, ICareerSessionRepository repository) =>
        new(repository, new UserRepository(db), new AuditWriterTestDouble(), new ManualCareerMeetingProvider(), f.Protector, f.Clock, Options.Create(new CareerSessionOptions()));

    private sealed class ForbiddenScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Disabled worker must not resolve database services.");
    }
    private sealed class BeforeSaveInterceptor(Func<Task> beforeSave) : SaveChangesInterceptor
    {
        private bool called;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!called) { called = true; await beforeSave(); }
            return result;
        }
    }
    private sealed class MutableProvider : ICareerMeetingProvider
    {
        public string Name => "Manual";
        public bool UnsafeIdentifier { get; set; }
        public string? HostUrl { get; set; } = CareerSessionTests.Fixture.HostUrl;
        private CareerMeeting Meeting(Guid id) => new(Name, UnsafeIdentifier ? CareerSessionTests.Fixture.HostUrl : $"manual_{id:N}",
            CareerSessionTests.Fixture.ParticipantUrl, HostUrl);
        public Task<CareerMeeting> CreateAsync(Guid sessionId, DateTime startUtc, DateTime endUtc, CancellationToken ct) => Task.FromResult(Meeting(sessionId));
        public Task<CareerMeeting?> GetAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<CareerMeeting?>(Meeting(sessionId));
    }
    private sealed class PausedProvider : ICareerMeetingProvider
    {
        public string Name => "Manual";
        public int Creates { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CareerMeeting> CreateAsync(Guid sessionId, DateTime startUtc, DateTime endUtc, CancellationToken ct)
        {
            Creates++; Entered.SetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            return new(Name, $"manual_{sessionId:N}", CareerSessionTests.Fixture.ParticipantUrl, null);
        }
        public Task<CareerMeeting?> GetAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<CareerMeeting?>(null);
    }
}
