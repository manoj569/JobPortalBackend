using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerOwnerLifecycleTests
{
    [Theory]
    [InlineData(ConsultantVerificationStatus.Draft)]
    [InlineData(ConsultantVerificationStatus.Pending)]
    [InlineData(ConsultantVerificationStatus.Rejected)]
    [InlineData(ConsultantVerificationStatus.Suspended)]
    public async Task OperationalAnalyticsRequiresVerified(ConsultantVerificationStatus status)
    {
        using var f = new CareerSchedulingTests.Fixture();
        var repo = new CareerAnalyticsRepository(f.Db, f.Clock);
        f.Profile.VerificationStatus = status;
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AppException>(() => repo.ConsultantAsync(f.Owner.Id, f.Clock.Utc.AddDays(-1), f.Clock.Utc, default));
        Assert.Equal(403, error.StatusCode);
        f.Profile.VerificationStatus = ConsultantVerificationStatus.Verified;
        await f.Db.SaveChangesAsync();
        Assert.NotNull(await repo.ConsultantAsync(f.Owner.Id, f.Clock.Utc.AddDays(-1), f.Clock.Utc, default));
    }

    [Theory]
    [InlineData(ConsultantVerificationStatus.Draft)]
    [InlineData(ConsultantVerificationStatus.Pending)]
    [InlineData(ConsultantVerificationStatus.Rejected)]
    [InlineData(ConsultantVerificationStatus.Suspended)]
    public async Task NonVerifiedCannotMarkLegacyBookingCompleteButCanReadHistory(ConsultantVerificationStatus status)
    {
        using var f = new CareerSchedulingTests.Fixture();
        var response = await f.Book();
        var booking = await f.Db.CareerGuidanceBookings.SingleAsync();
        booking.RequiresPayment = false;
        booking.Status = CareerBookingStatus.Confirmed;
        f.Profile.VerificationStatus = status;
        f.Clock.Utc = booking.EndUtc.AddMinutes(1);
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AppException>(() => f.Service.SetStatusAsync(f.Owner.Id, response.Id, new(booking.Revision, CareerBookingStatus.Completed), default));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(CareerBookingStatus.Confirmed, booking.Status);
        Assert.Equal(response.Id, (await f.Service.GetAsync(f.Owner.Id, response.Id, true, false, default)).Id);
        Assert.Single((await f.Service.BookingsAsync(f.Owner.Id, true, false, new(), default)).Items);
    }

    [Fact]
    public async Task SuspendedOwnerCanCancelAnExistingObligationButCannotEditAvailability()
    {
        using var f = new CareerSchedulingTests.Fixture();
        var booking = await f.Book();
        f.Profile.VerificationStatus = ConsultantVerificationStatus.Suspended;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<AppException>(() => f.Service.SaveAvailabilityAsync(f.Owner.Id,
            new("Asia/Kolkata", true, [], f.Profile.Revision), default));
        Assert.Empty((await f.Slots()).Slots);
        var result = await f.Service.CancelAsync(f.Owner.Id, booking.Id, true, new(booking.Revision), default);
        Assert.Equal(CareerBookingStatus.CancelledByConsultant, result.Status);
        Assert.Single(await f.Db.CareerGuidanceBookings.ToArrayAsync());
    }
}
