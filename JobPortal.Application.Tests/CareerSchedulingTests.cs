using System.Reflection;
using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerSchedulingTests
{
    [Fact]
    public async Task OwnerSavesWindowsAndAnotherUserCannotEditOrDeleteExceptions()
    {
        using var f = new Fixture();
        var saved = await f.Service.SaveAvailabilityAsync(f.Owner.Id, new("Asia/Kolkata", true,
            [new(DayOfWeek.Monday, new(9, 0), new(12, 0))], f.Profile.Revision), default);
        Assert.True(saved.IsAcceptingBookings);
        Assert.Single(saved.Windows);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.SaveAvailabilityAsync(f.Other.Id,
            new("Asia/Kolkata", true, [], saved.Revision), default));
        var block = await f.Service.AddExceptionAsync(f.Owner.Id, new(f.Day, null, null, saved.Revision), default);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.DeleteExceptionAsync(f.Other.Id, block.Exception!.Id, block.Revision, default));
        await f.Service.DeleteExceptionAsync(f.Owner.Id, block.Exception!.Id, block.Revision, default);
        Assert.Empty(await f.Service.ExceptionsAsync(f.Owner.Id, new(f.Day, f.Day), default));
    }

    [Theory]
    [InlineData(9, 9)]
    [InlineData(12, 9)]
    [InlineData(0, 23)]
    public void InvalidWindowBounds(int start, int end) => Assert.False(new SaveAvailabilityRequestValidator().Validate(
        new SaveAvailabilityRequest("Asia/Kolkata", true, [new(DayOfWeek.Monday, new(start, 0), new(end, 0))], Guid.NewGuid())).IsValid);

    [Fact]
    public void OverlappingAndDuplicateWindowsFail()
    {
        var validator = new SaveAvailabilityRequestValidator();
        Assert.False(validator.Validate(new SaveAvailabilityRequest("Asia/Kolkata", true,
            [new(DayOfWeek.Monday, new(9, 0), new(11, 0)), new(DayOfWeek.Monday, new(10, 0), new(12, 0))], Guid.NewGuid())).IsValid);
        Assert.False(validator.Validate(new SaveAvailabilityRequest("Asia/Kolkata", true,
            [new(DayOfWeek.Monday, new(9, 0), new(11, 0)), new(DayOfWeek.Monday, new(9, 0), new(11, 0))], Guid.NewGuid())).IsValid);
    }

    [Theory]
    [InlineData("+05:30")]
    [InlineData("Eastern Standard Time")]
    [InlineData("Invalid/Zone")]
    public void InvalidTimezoneRejected(string zone) => Assert.Throws<BadRequestException>(() => CareerSlotGenerator.TimeZone(zone));

    [Fact]
    public async Task KolkataSlotsAreUtcAndHaveNoPrivateData()
    {
        using var f = new Fixture();
        var response = await f.Slots();
        Assert.Equal("Asia/Kolkata", response.TimeZoneId);
        Assert.Equal(new DateTime(2026, 9, 21, 3, 30, 0, DateTimeKind.Utc), response.Slots.First().StartUtc);
        Assert.All(response.Slots, s => Assert.Equal(TimeSpan.FromHours(1), s.EndUtc - s.StartUtc));
        Assert.Equal(2, typeof(CareerSlot).GetProperties().Length);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("suspended")]
    [InlineData("service")]
    [InlineData("accepting")]
    [InlineData("user")]
    public async Task UnbookableStateHasNoSlotsAndCannotBeBooked(string condition)
    {
        using var f = new Fixture();
        if (condition == "pending") f.Profile.VerificationStatus = ConsultantVerificationStatus.Pending;
        if (condition == "suspended") f.Profile.VerificationStatus = ConsultantVerificationStatus.Suspended;
        if (condition == "service") f.Offering.IsActive = false;
        if (condition == "accepting") f.Profile.IsAcceptingBookings = false;
        if (condition == "user") f.Owner.Status = UserStatus.Suspended;
        await f.Db.SaveChangesAsync();
        Assert.Empty((await f.Slots()).Slots);
        await Assert.ThrowsAsync<ConflictException>(() => f.Book());
    }

    [Fact]
    public async Task FullDayAndPartialBlocksFilterOverlappingSlots()
    {
        using var f = new Fixture();
        await f.Service.AddExceptionAsync(f.Owner.Id, new(f.Day, new(10, 0), new(11, 0), f.Profile.Revision), default);
        var remaining = (await f.Slots()).Slots;
        Assert.Equal(2, remaining.Count); // 09–10 and 11–12 only; touching endpoints allowed.
        await Assert.ThrowsAsync<ConflictException>(() => f.Book(new DateTimeOffset(2026, 9, 21, 4, 0, 0, TimeSpan.Zero)));
        var blocks = await f.Service.ExceptionsAsync(f.Owner.Id, new(f.Day, f.Day), default);
        await f.Service.DeleteExceptionAsync(f.Owner.Id, blocks.Single().Id, f.Profile.Revision, default);
        await f.Service.AddExceptionAsync(f.Owner.Id, new(f.Day, null, null, f.Profile.Revision), default);
        Assert.Empty((await f.Slots()).Slots);
    }

    [Theory]
    [InlineData(3, 8)]
    [InlineData(11, 1)]
    public void DstGapsAndFoldsAreSkippedIncludingCrossingIntervals(int month, int day)
    {
        var date = new DateOnly(2026, month, day);
        var zone = CareerSlotGenerator.TimeZone("America/New_York");
        var windows = new[] { new CareerConsultantAvailability { DayOfWeek = date.DayOfWeek, StartTime = new(0, 0), EndTime = new(5, 0) } };
        var slots = CareerSlotGenerator.Generate(zone, date, date, 60, windows, [], [], new(), date.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        Assert.NotEmpty(slots);
        Assert.Equal(slots.Count, slots.Select(s => s.StartUtc).Distinct().Count());
        foreach (var slot in slots)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(slot.StartUtc, zone);
            Assert.False(zone.IsInvalidTime(local)); Assert.False(zone.IsAmbiguousTime(local));
            Assert.Equal(TimeSpan.FromHours(1), slot.EndUtc - slot.StartUtc);
            if (month == 3) Assert.False(local.Hour is 1 or 2);
            else Assert.False(local.Hour is 0 or 1);
        }
    }

    [Fact]
    public async Task BookingStoresTrimmedQuestionnaireAndImmutableSnapshots()
    {
        using var f = new Fixture();
        var booking = await f.Book();
        Assert.Equal(CareerBookingStatus.Pending, booking.Status);
        Assert.Equal("Prepare", booking.Questionnaire.SessionGoal);
        Assert.Equal("EY", booking.Questionnaire.TargetCompany);
        Assert.Equal(60, booking.DurationMinutes); Assert.Equal(999, booking.Price);
        Assert.Equal("INR", booking.Currency); Assert.Equal("Mock interview", booking.ServiceTitle);
        f.Offering.Price = 1500; f.Offering.DurationMinutes = 30; f.Offering.Title = "Changed";
        await f.Db.SaveChangesAsync();
        var reloaded = await f.Service.GetAsync(f.Candidate.Id, booking.Id, false, false, default);
        Assert.Equal(booking, reloaded);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.SaveAvailabilityAsync(f.Owner.Id,
            new("Europe/London", true, [], f.Profile.Revision), default));
    }

    [Fact]
    public async Task SelfBookingAndInactiveCandidateRejected()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.CreateAsync(f.Owner.Id, f.Request(), default));
        f.Candidate.Status = UserStatus.Suspended; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Book());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(1500)]
    public async Task PastNoticeOutsideAvailabilityAndHorizonRejected(int hours)
    {
        using var f = new Fixture();
        await Assert.ThrowsAnyAsync<AppException>(() => f.Book(new DateTimeOffset(f.Clock.Utc.AddHours(hours), TimeSpan.Zero)));
    }

    [Fact]
    public async Task ParticipantScopesEnforceIdorAndCancellationReleasesSlot()
    {
        using var f = new Fixture();
        var b = await f.Book();
        Assert.Equal(b.Id, (await f.Service.GetAsync(f.Owner.Id, b.Id, true, false, default)).Id);
        Assert.Single((await f.Service.BookingsAsync(f.Candidate.Id, false, false, new(), default)).Items);
        Assert.Empty((await f.Service.BookingsAsync(f.Other.Id, false, false, new(), default)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.Other.Id, b.Id, false, false, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.Other.Id, b.Id, true, false, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.CancelAsync(f.Other.Id, b.Id, true, new(b.Revision), default));
        await Assert.ThrowsAsync<ConflictException>(() => f.Book());
        b = await f.Service.CancelAsync(f.Candidate.Id, b.Id, false, new(b.Revision, "  Changed plans  "), default);
        Assert.Equal(CareerBookingStatus.CancelledByCandidate, b.Status);
        Assert.Equal("Changed plans", b.CancellationReason);
        Assert.Contains((await f.Slots()).Slots, s => s.StartUtc == b.StartUtc);
        Assert.NotEqual(b.Id, (await f.Book()).Id);
    }

    [Fact]
    public async Task OnlyConsultantCanConfirmAndCompleteAfterEndAndCompletedCannotCancel()
    {
        using var f = new Fixture();
        var b = await f.Book();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.SetStatusAsync(f.Candidate.Id, b.Id, new(b.Revision, CareerBookingStatus.Completed), default));
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.SetStatusAsync(f.Owner.Id, b.Id, new(b.Revision, CareerBookingStatus.Completed), default));
        b = await f.Service.SetStatusAsync(f.Owner.Id, b.Id, new(b.Revision, CareerBookingStatus.Confirmed), default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.SetStatusAsync(f.Owner.Id, b.Id, new(b.Revision, CareerBookingStatus.Completed), default));
        f.Clock.Utc = b.EndUtc.AddMinutes(1);
        b = await f.Service.SetStatusAsync(f.Owner.Id, b.Id, new(b.Revision, CareerBookingStatus.Completed), default);
        Assert.NotNull(b.CompletedAtUtc);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.CancelAsync(f.Owner.Id, b.Id, true, new(b.Revision), default));
    }

    [Fact]
    public void ValidatorsPrivacyAndControllerAuthorization()
    {
        using var f = new Fixture();
        Assert.False(new CreateCareerBookingRequestValidator().Validate(f.Request() with { Questionnaire = f.Request().Questionnaire with { SessionGoal = "  " } }).IsValid);
        foreach (var type in new[] { typeof(CareerBookingsController), typeof(CareerConsultantBookingsController), typeof(CareerAvailabilityController) })
        {
            Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        }
        Assert.NotNull(typeof(CareerSlotsController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("Administrator", typeof(AdminCareerBookingsController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Null(typeof(SlotResponse).GetProperty("Questionnaire"));
    }

    [Fact]
    public async Task DatabaseConflictIsTranslatedToSafe409()
    {
        var error = new DbUpdateException("private", new PostgresException("private", "ERROR", "ERROR", "23P01", constraintName: "EX_CareerGuidanceBookings_NoOverlap"));
        using var db = new FailingContextFactory(error).Create();
        // Interceptor exercises the real repository catch without a live database.
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => new CareerSchedulingRepository(db).SaveAsync(default));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("booking_overlap", conflict.Code);
        Assert.DoesNotContain("private", conflict.Message);
    }

    [Fact]
    public async Task CompetingProfileRevisionsCannotBothCommit()
    {
        using var f = new Fixture();
        using var first = new JobPortalDbContext(f.Options);
        using var second = new JobPortalDbContext(f.Options);
        var a = (await new CareerSchedulingRepository(first).ProfileAsync(f.Profile.Id, default))!;
        var b = (await new CareerSchedulingRepository(second).ProfileAsync(f.Profile.Id, default))!;
        Assert.Equal(a.Revision, b.Revision);
        a.Revision = Guid.NewGuid(); b.Revision = Guid.NewGuid();
        // Both requests read the same revision; the second cannot commit stale scheduling data.
        await new CareerSchedulingRepository(first).SaveAsync(default);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => new CareerSchedulingRepository(second).SaveAsync(default));
        Assert.Equal("concurrency_conflict", conflict.Code);
        Assert.Empty(second.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CancellationNoticeRevisionAndAdminAuthorizationAreEnforced()
    {
        using var f = new Fixture();
        var b = await f.Book();
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.CancelAsync(f.Candidate.Id, b.Id, false, new(Guid.NewGuid()), default));
        var forbidden = await Assert.ThrowsAsync<AppException>(() => f.Service.GetAsync(f.Other.Id, b.Id, false, true, default));
        Assert.Equal(403, forbidden.StatusCode);
        f.Clock.Utc = b.StartUtc.AddMinutes(-30);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.CancelAsync(f.Candidate.Id, b.Id, false, new(b.Revision), default));
        var cancelled = await f.Service.CancelAsync(f.Owner.Id, b.Id, true, new(b.Revision), default);
        Assert.Equal(CareerBookingStatus.CancelledByConsultant, cancelled.Status);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.SetStatusAsync(f.Owner.Id, b.Id, new(cancelled.Revision, CareerBookingStatus.Confirmed), default));
    }

    [Fact]
    public async Task UnverifiedOwnerCannotManageAvailabilityAndWrongServiceCannotBeBooked()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.CreateAsync(f.Candidate.Id, f.Request() with { ServiceId = Guid.NewGuid() }, default));
        f.Profile.VerificationStatus = ConsultantVerificationStatus.Pending;
        await f.Db.SaveChangesAsync();
        var forbidden = await Assert.ThrowsAsync<AppException>(() => f.Service.SaveAvailabilityAsync(f.Owner.Id,
            new("Asia/Kolkata", true, [], f.Profile.Revision), default));
        Assert.Equal(403, forbidden.StatusCode);
    }

    private sealed class FailingContextFactory(Exception error)
    {
        public JobPortalDbContext Create() => new(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new Failure(error)).Options);
        private sealed class Failure(Exception failure) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
        {
            public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw failure;
        }
    }

    internal sealed class Fixture : IDisposable
    {
        public DbContextOptions<JobPortalDbContext> Options { get; } = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public JobPortalDbContext Db { get; }
        public User Owner { get; } = new() { Status = UserStatus.Active };
        public User Candidate { get; } = new() { Status = UserStatus.Active };
        public User Other { get; } = new() { Status = UserStatus.Active };
        public CareerConsultant Profile { get; }
        public CareerConsultantService Offering { get; }
        public TestClock Clock { get; } = new();
        public DateOnly Day => DateOnly.FromDateTime(Clock.Utc);
        public CareerSchedulingService Service { get; }
        public Fixture()
        {
            Db = new(Options);
            var role = new Role { Name = "Candidate" };
            Owner.Role = Candidate.Role = Other.Role = role;
            Owner.RoleId = Candidate.RoleId = Other.RoleId = role.Id;
            Profile = new() { UserId = Owner.Id, User = Owner, VerificationStatus = ConsultantVerificationStatus.Verified, TimeZoneId = "Asia/Kolkata", IsAcceptingBookings = true };
            Offering = new() { ConsultantId = Profile.Id, Consultant = Profile, Title = "Mock interview", ServiceType = "INTERVIEW", Price = 999, Currency = "INR", DurationMinutes = 60, IsActive = true };
            Profile.Services.Add(Offering);
            Db.AddRange(role, Owner, Candidate, Other, Profile);
            Db.Add(new CareerConsultantAvailability { ConsultantId = Profile.Id, DayOfWeek = DayOfWeek.Monday, StartTime = new(9, 0), EndTime = new(12, 0) });
            Db.SaveChanges();
            Service = new(new CareerSchedulingRepository(Db), new UserRepository(Db), new AuditWriterTestDouble(), Clock,
                Microsoft.Extensions.Options.Options.Create(new CareerGuidanceSchedulingOptions()), new SaveAvailabilityRequestValidator(), new AvailabilityExceptionRequestValidator(), new CreateCareerBookingRequestValidator());
        }
        public CreateCareerBookingRequest Request(DateTimeOffset? start = null) => new(Profile.Id, Offering.Id,
            start ?? new DateTimeOffset(2026, 9, 21, 3, 30, 0, TimeSpan.Zero), new(" EY ", "Consultant", 3, "Engineer", " Prepare ", "Questions", "Notes"));
        public Task<CareerBookingResponse> Book(DateTimeOffset? start = null) => Service.CreateAsync(Candidate.Id, Request(start), default);
        public Task<SlotResponse> Slots() => Service.SlotsAsync(Profile.Id, Offering.Id, new(Day, Day), default);
        public void Dispose() => Db.Dispose();
    }
    internal sealed class TestClock : TimeProvider
    {
        public DateTime Utc { get; set; } = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Utc);
    }
}
