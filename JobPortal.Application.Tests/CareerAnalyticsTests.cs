using JobPortal.Application.Features.CareerGuidance;
using System.Reflection;
using System.Security.Claims;
using JobPortal.API.Controllers;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerAnalyticsTests
{
    [Fact]
    public void DefaultRangeIsUtcAndBounded()
    {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var (from, to) = new CareerAnalyticsQuery().Normalize(now);
        Assert.Equal(now, to); Assert.Equal(now.AddDays(-30), from);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void NonUtcOrInvalidRangesAreRejected(DateTimeKind kind)
    {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => new CareerAnalyticsQuery(DateTime.SpecifyKind(now.AddDays(-1), kind), now).Normalize(now));
        Assert.Throws<ArgumentException>(() => new CareerAnalyticsQuery(now, now).Normalize(now));
        Assert.Throws<ArgumentException>(() => new CareerAnalyticsQuery(now.AddDays(-367), now).Normalize(now));
    }

    [Fact]
    public void Exactly366DaysIsAcceptedAndDefaultUnderflowIsRejectedSafely()
    {
        var to = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(to.AddDays(-366), new CareerAnalyticsQuery(to.AddDays(-366), to).Normalize(to).From);
        Assert.Throws<ArgumentException>(() => new CareerAnalyticsQuery(to.AddDays(-366).AddTicks(-1), to).Normalize(to));
        Assert.Throws<ArgumentException>(() => new CareerAnalyticsQuery(ToUtc: DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)).Normalize(to));
    }

    [Fact]
    public async Task EmptyDatasetReturnsZeroAndNullRatingWithoutTracking()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repo = new CareerAnalyticsRepository(db, TimeProvider.System);
        var now = DateTime.UtcNow; var r = await repo.AdminAsync(now.AddDays(-1), now, default);
        Assert.Equal(0, r.TotalBookings); Assert.Equal(0m, r.GrossPaymentVolume); Assert.Null(r.AverageRating);
        Assert.Equal(0, r.RefundCount); Assert.Equal(0, r.OpenDisputes); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CreatedAtCohortUsesInclusiveFromExclusiveToAndExcludesDeletedBookings()
    {
        using var f = new CareerSchedulingTests.Fixture(); var booking = await f.Book();
        var entity = await f.Db.CareerGuidanceBookings.SingleAsync(); var at = entity.CreatedAtUtc;
        var repo = new CareerAnalyticsRepository(f.Db, f.Clock);
        Assert.Equal(1, (await repo.AdminAsync(at, at.AddDays(1), default)).TotalBookings);
        Assert.Equal(0, (await repo.AdminAsync(at.AddDays(-1), at, default)).TotalBookings);
        entity.IsDeleted = true; await f.Db.SaveChangesAsync();
        Assert.Equal(0, (await repo.AdminAsync(at, at.AddDays(1), default)).TotalBookings);
    }

    [Fact]
    public async Task CaptureRetriesAndProcessedRefundDoNotEraseGrossOrBecomeFailedPayments()
    {
        using var f = new CareerFinanceTests.Fixture(); var b = await f.Setup(); var order = await f.Order(b.Id);
        var repo = new CareerAnalyticsRepository(f.Db, f.Scheduling.Clock);
        var start = DateTime.UtcNow.AddDays(-1); var end = start.AddDays(2);
        Assert.Equal(0m, (await repo.AdminAsync(start, end, default)).GrossPaymentVolume);
        await f.Verify(b.Id, order); await f.Verify(b.Id, order);
        var captured = await repo.AdminAsync(start, end, default);
        Assert.Equal(1, captured.CapturedPaymentCount); Assert.Equal(999m, captured.GrossPaymentVolume);
        Assert.Equal(99.9m, captured.PlatformCommission); Assert.Equal(899.1m, captured.ConsultantNetEarnings);
        await f.Service.RefundAsync(f.Admin.Id, order.PaymentId, new(CareerRefundReason.AdminCorrection), default);
        var refunded = await repo.AdminAsync(start, end, default);
        Assert.Equal(999m, refunded.GrossPaymentVolume); Assert.Equal(1, refunded.CapturedPaymentCount);
        Assert.Equal(0, refunded.FailedPaymentCount); Assert.Equal(999m, refunded.RefundedAmount); Assert.Equal(1, refunded.RefundCount);
        Assert.Equal(0m, refunded.PlatformCommission); Assert.Equal(0m, refunded.ConsultantNetEarnings);
        var own = (await repo.ConsultantAsync(f.Scheduling.Owner.Id, start, end, default))!;
        Assert.Equal(0m, own.ConsultantEarnings); Assert.Equal(0m, own.HeldEarnings); Assert.Equal(1, own.RefundedBookings);
    }

    [Fact]
    public async Task FailedUncapturedAttemptIsCountedButPendingRefundIsNotProcessed()
    {
        using var f = new CareerFinanceTests.Fixture(); var b = await f.Setup(); var order = await f.Order(b.Id);
        var p = await f.Db.Set<CareerGuidancePayment>().SingleAsync(); p.FailureCode = "provider_attempt_failed"; await f.Db.SaveChangesAsync();
        var repo = new CareerAnalyticsRepository(f.Db, f.Scheduling.Clock); var start = DateTime.UtcNow.AddDays(-1); var end = start.AddDays(2);
        Assert.Equal(1, (await repo.AdminAsync(start, end, default)).FailedPaymentCount);
        await f.Verify(b.Id, order); f.Gateway.RefundState = "pending";
        await f.Service.RefundAsync(f.Admin.Id, order.PaymentId, new(CareerRefundReason.AdminCorrection), default);
        var r = await repo.AdminAsync(start, end, default);
        Assert.Equal(0, r.FailedPaymentCount); Assert.Equal(0, r.RefundCount); Assert.Equal(0m, r.RefundedAmount);
        Assert.Equal(999m, r.GrossPaymentVolume);
    }

    [Theory]
    [InlineData("booking")]
    [InlineData("payment")]
    [InlineData("earning")]
    [InlineData("refund")]
    public async Task UnsupportedCurrencyIsRejectedInsteadOfCombined(string record)
    {
        using var f = new CareerSchedulingTests.Fixture(); var b = await f.Book();
        var booking = await f.Db.CareerGuidanceBookings.SingleAsync();
        if (record == "booking") booking.CurrencySnapshot = "USD";
        var payment = new CareerGuidancePayment { BookingId = b.Id, Booking = booking, CandidateUserId = f.Candidate.Id,
            ConsultantId = f.Profile.Id, Consultant = f.Profile, Currency = record == "payment" ? "USD" : "INR" };
        f.Db.Add(payment);
        if (record == "earning") f.Db.Add(new CareerGuidanceEarning { PaymentId = payment.Id, Payment = payment, Currency = "USD" });
        if (record == "refund") f.Db.Add(new CareerGuidanceRefund { PaymentId = payment.Id, Payment = payment, Currency = "USD" });
        await f.Db.SaveChangesAsync();
        var repo = new CareerAnalyticsRepository(f.Db, f.Clock); var start = DateTime.UtcNow.AddDays(-1); var end = start.AddDays(2);
        Assert.Equal("analytics_currency", (await Assert.ThrowsAsync<ConflictException>(() => repo.AdminAsync(start, end, default))).Code);
        await Assert.ThrowsAsync<ConflictException>(() => repo.ConsultantAsync(f.Owner.Id, start, end, default));
    }

    [Fact]
    public async Task PendingAndHeldAreDisjointAndNullAvailabilityAloneIsNotADisputeHold()
    {
        using var f = new CareerTrustTests.Fixture(); var s = await f.Session.Setup();
        var repo = new CareerAnalyticsRepository(f.Db, f.Clock); var start = DateTime.UtcNow.AddDays(-1); var end = start.AddDays(2);
        var before = (await repo.ConsultantAsync(f.Owner, start, end, default))!;
        Assert.Equal(899.1m, before.PendingEarnings); Assert.Equal(0m, before.HeldEarnings);
        var dispute = await f.Service.OpenDisputeAsync(f.Candidate, s.BookingId, new(CareerDisputeCategory.BillingIssue, "Charge question"), default);
        var during = (await repo.ConsultantAsync(f.Owner, start, end, default))!;
        Assert.Equal(0m, during.PendingEarnings); Assert.Equal(899.1m, during.HeldEarnings); Assert.Equal(1, during.OpenDisputes);
        await f.Service.ResolveAsync(f.Admin, dispute.Id, new(dispute.Revision, CareerDisputeResolution.NoAction, "Reviewed"), default);
        var after = (await repo.ConsultantAsync(f.Owner, start, end, default))!;
        Assert.Equal(899.1m, after.PendingEarnings); Assert.Equal(0m, after.HeldEarnings); Assert.Equal(0, after.OpenDisputes);
    }

    [Fact]
    public async Task RatingsFollowModerationAndWithdrawalEligibility()
    {
        using var f = new CareerTrustTests.Fixture(); var s = await f.Completed(); var review = await f.Review(s.BookingId);
        var repo = new CareerAnalyticsRepository(f.Db, f.Clock); var start = DateTime.UtcNow.AddDays(-1); var end = start.AddDays(2);
        Assert.Null((await repo.AdminAsync(start, end, default)).AverageRating);
        review = await f.Approve(review);
        var approved = await repo.AdminAsync(start, end, default);
        Assert.Equal(5m, approved.AverageRating); Assert.Equal(1, approved.PublishedReviewCount);
        await f.Service.WithdrawReviewAsync(f.Candidate, s.BookingId, review.Revision, default);
        Assert.Null((await repo.AdminAsync(start, end, default)).AverageRating);
    }

    [Fact]
    public async Task UpcomingSessionIsNotAnUnprovisionedBookingAndIsIndependentOfCohort()
    {
        using var f = new CareerSessionTests.Fixture(); var s = await f.Setup();
        var repo = new CareerAnalyticsRepository(f.Db, f.Clock); var to = DateTime.UtcNow.AddDays(-10);
        var consultant = (await repo.ConsultantAsync(f.Owner, to.AddDays(-1), to, default))!;
        Assert.Equal(0, consultant.TotalBookings); Assert.Equal(s.ScheduledStartUtc, consultant.NextUpcomingSession);
        Assert.Equal(s.ScheduledStartUtc, (await repo.CandidateAsync(f.Candidate, default)).NextUpcomingSession);
        Assert.Null((await repo.CandidateAsync(f.Finance.Scheduling.Other.Id, default)).NextUpcomingSession);
        Assert.Null(await repo.ConsultantAsync(f.Candidate, to.AddDays(-1), to, default));
        using var unprovisioned = new CareerFinanceTests.Fixture(); await unprovisioned.Setup();
        Assert.Null((await new CareerAnalyticsRepository(unprovisioned.Db, unprovisioned.Scheduling.Clock).CandidateAsync(unprovisioned.Candidate, default)).NextUpcomingSession);
    }

    [Fact]
    public async Task AnalyticsControllersRecheckActiveAccountsAndCurrentRoles()
    {
        using var f = new CareerFinanceTests.Fixture();
        var repo = new CareerAnalyticsRepository(f.Db, f.Scheduling.Clock); var users = new UserRepository(f.Db);
        var admin = new AdminCareerAnalyticsController(repo, f.Scheduling.Clock, users) { ControllerContext = Context(f.Candidate) };
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => admin.Overview(new(), default))).StatusCode);
        var owner = new CareerOwnerAnalyticsController(repo, f.Scheduling.Clock, users) { ControllerContext = Context(f.Admin.Id) };
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => owner.Summary(default))).StatusCode);
        f.Scheduling.Candidate.Status = UserStatus.Suspended; await f.Db.SaveChangesAsync();
        owner.ControllerContext = Context(f.Candidate);
        await Assert.ThrowsAsync<UnauthorizedException>(() => owner.Summary(default));
        await Assert.ThrowsAsync<UnauthorizedException>(() => owner.Consultant(new(), default));
        f.Admin.Status = UserStatus.Suspended; await f.Db.SaveChangesAsync(); admin.ControllerContext = Context(f.Admin.Id);
        await Assert.ThrowsAsync<UnauthorizedException>(() => admin.Overview(new(), default));
    }

    [Fact]
    public void AnalyticsMetadataIsPrivateAndRoleRestricted()
    {
        Assert.Equal("Administrator", typeof(AdminCareerAnalyticsController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("Candidate", typeof(CareerOwnerAnalyticsController).GetMethod("Summary")!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.True(typeof(AdminCareerAnalyticsController).GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.True(typeof(CareerOwnerAnalyticsController).GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        foreach (var type in new[] { typeof(CareerGuidanceAdminAnalytics), typeof(CareerGuidanceConsultantAnalytics), typeof(CareerGuidanceCandidateSummary) })
            Assert.DoesNotContain(type.GetProperties(), p => new[] { "Email", "Phone", "AdminNotes", "ProviderPaymentId", "JoinUrl", "Questions", "Evidence" }.Contains(p.Name));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FinalNoShowsAreCountedWithoutPrivateSessionContent(bool consultantNoShow)
    {
        using var f = new CareerSessionTests.Fixture(); var session = await f.Setup(); f.Clock.Utc = session.ScheduledStartUtc.AddMinutes(15);
        await f.Service.NoShowAsync(consultantNoShow ? f.Finance.Admin.Id : f.Owner, session.Id, new(session.Revision), consultantNoShow, default);
        var start = DateTime.UtcNow.AddDays(-1);
        var report = await new CareerAnalyticsRepository(f.Db, f.Clock).AdminAsync(start, start.AddDays(2), default);
        Assert.Equal(1, report.SessionNoShows); Assert.Equal(0, report.CompletedSessions);
        Assert.Equal(consultantNoShow ? 1 : 0, report.ConsultantNoShows);
        Assert.Equal(consultantNoShow ? 0 : 1, report.CandidateNoShows);
    }

    [Fact]
    public void HeldAndPendingAggregatesTranslateToPostgresWithoutDatabaseAccess()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        var repo = new CareerAnalyticsRepository(db, TimeProvider.System);
        var payments = db.Set<CareerGuidancePayment>().Where(p => !p.IsDeleted);
        var earnings = (IQueryable<CareerGuidanceEarning>)typeof(CareerAnalyticsRepository).GetMethod("Earnings", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(repo, new object[] { payments })!;
        var held = (IQueryable<CareerGuidanceEarning>)typeof(CareerAnalyticsRepository).GetMethod("Held", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(repo, new object[] { earnings })!;
        var query = earnings.Where(e => e.Status == CareerEarningStatus.Pending && !held.Any(h => h.Id == e.Id))
            .GroupBy(e => e.Currency).Select(g => new { Currency = g.Key, Total = g.Sum(e => e.NetAmount) });
        var sql = query.ToQueryString();
        Assert.Contains("SUM", sql, StringComparison.OrdinalIgnoreCase); Assert.Contains("EXISTS", sql);
        Assert.Contains("CareerGuidanceDisputes", sql); Assert.Contains("IsDeleted", sql);
        Assert.DoesNotContain("ProtectedParticipantUrl", sql); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task DeletedLedgerRowsAreExcludedEvenWhenHoldQueryIgnoresCaseFilters()
    {
        using var f = new CareerSchedulingTests.Fixture(); var b = await f.Book(); var booking = await f.Db.CareerGuidanceBookings.SingleAsync();
        var p = new CareerGuidancePayment { BookingId = b.Id, Booking = booking, CandidateUserId = f.Candidate.Id,
            ConsultantId = f.Profile.Id, Consultant = f.Profile, PaidAtUtc = f.Clock.Utc, RequiresRefundReview = true, Status = CareerPaymentStatus.Captured };
        f.Db.Add(new CareerGuidanceEarning { PaymentId = p.Id, Payment = p, ConsultantId = f.Profile.Id, Consultant = f.Profile,
            NetAmount = 100m, IsDeleted = true });
        await f.Db.SaveChangesAsync();
        var start = DateTime.UtcNow.AddDays(-1);
        var report = (await new CareerAnalyticsRepository(f.Db, f.Clock).ConsultantAsync(f.Owner.Id, start, start.AddDays(2), default))!;
        Assert.Equal(0m, report.PendingEarnings); Assert.Equal(0m, report.HeldEarnings); Assert.Equal(0m, report.ConsultantEarnings);
    }

    private static ControllerContext Context(Guid id) => new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, "test")) }
    };
}
