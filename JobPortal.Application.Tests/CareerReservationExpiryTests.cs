using System.Reflection;
using System.Text.Json;
using JobPortal.API.Controllers;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerReservationExpiryTests
{
    [Fact]
    public void MigrationChangesOnlyStatusConstraintAndKeepsOverlapProtection()
    {
        var migration = new AddCareerGuidanceReservationExpiry();
        foreach (var (operations, range) in new[] { (migration.UpOperations, "1 AND 8"), (migration.DownOperations, "1 AND 7") })
        {
            Assert.Equal(2, operations.Count);
            var drop = Assert.IsType<DropCheckConstraintOperation>(operations[0]);
            var add = Assert.IsType<AddCheckConstraintOperation>(operations[1]);
            Assert.Equal("CareerGuidanceBookings", drop.Table);
            Assert.Equal(drop.Table, add.Table);
            Assert.Equal("CK_CareerBooking_Status", drop.Name);
            Assert.Equal(drop.Name, add.Name);
            Assert.Equal("\"Status\" BETWEEN " + range, add.Sql);
        }
        Assert.Equal(8, (int)CareerBookingStatus.Expired);
        var exclusion = Assert.Single(new AddCareerGuidanceSchedulingAndBookings().UpOperations.OfType<SqlOperation>()).Sql;
        Assert.Contains("\"Status\" IN (1, 2)", exclusion);
        Assert.Contains("EXCLUDE USING gist", exclusion);
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only", o => o.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        Assert.False(db.Database.HasPendingModelChanges()); // Offline model comparison; no connection is opened.
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Recruiter")]
    public async Task BookingCreationRejectsNonCandidatesAtServiceLayer(string roleName)
    {
        using var f = new CareerSchedulingTests.Fixture();
        var role = new Role { Name = roleName };
        f.Candidate.Role = role; f.Candidate.RoleId = role.Id;
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AppException>(() => f.Book());
        Assert.Equal(403, error.StatusCode);
        Assert.Empty(await f.Db.CareerGuidanceBookings.ToArrayAsync());
    }

    [Fact]
    public async Task CandidateAuthorizationRetainsAnonymousSelfBookingAndIdorProtection()
    {
        using var f = new CareerSchedulingTests.Fixture();
        Assert.Equal("Candidate", typeof(CareerBookingsController).GetMethod("Create")!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Null(typeof(CareerBookingsController).GetCustomAttribute<AllowAnonymousAttribute>());
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.CreateAsync(Guid.Empty, f.Request(), default));
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.CreateAsync(f.Owner.Id, f.Request(), default));
        var booking = await f.Book();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.Other.Id, booking.Id, false, false, default));
    }

    [Theory]
    [InlineData(599, false)]
    [InlineData(600, true)]
    [InlineData(601, true)]
    public async Task SlotReadPersistsExpiryAtExactUtcBoundary(int elapsedSeconds, bool expired)
    {
        using var f = new CareerSchedulingTests.Fixture();
        var response = await f.Book();
        var booking = await Timestamp(f.Db, response.Id, f.Clock.Utc);
        var originalRevision = booking.Revision;
        f.Clock.Utc = f.Clock.Utc.AddSeconds(elapsedSeconds);
        var slots = await f.Slots();
        Assert.Equal(expired, slots.Slots.Any(x => x.StartUtc == response.StartUtc));
        Assert.Equal(expired ? CareerBookingStatus.Expired : CareerBookingStatus.Pending, booking.Status);
        Assert.False(booking.IsDeleted);
        Assert.Null(booking.CancelledByUserId);
        Assert.Null(booking.CancelledAtUtc);
        if (expired) Assert.NotEqual(originalRevision, booking.Revision);
        var revision = booking.Revision;
        await f.Slots();
        Assert.Equal(revision, booking.Revision);
        Assert.Equal(expired ? 1 : 0, await f.Db.Set<AuditLog>().CountAsync(x => x.EntityId == booking.Id.ToString()));
        Assert.Equal(booking.Status, (await f.Service.GetAsync(f.Candidate.Id, booking.Id, false, false, default)).Status);
        if (expired) Assert.NotEqual(booking.Id, (await f.Book()).Id);
        else await Assert.ThrowsAsync<ConflictException>(() => f.Book());
    }

    [Theory]
    [InlineData(CareerBookingStatus.Confirmed, true)]
    [InlineData(CareerBookingStatus.Completed, false)]
    [InlineData(CareerBookingStatus.CancelledByCandidate, false)]
    [InlineData(CareerBookingStatus.CancelledByConsultant, false)]
    [InlineData(CareerBookingStatus.NoShowCandidate, false)]
    [InlineData(CareerBookingStatus.NoShowConsultant, false)]
    public async Task OtherStatesAreNotExpired(CareerBookingStatus status, bool blocked)
    {
        using var f = new CareerSchedulingTests.Fixture();
        var response = await f.Book();
        var booking = await Timestamp(f.Db, response.Id, f.Clock.Utc.AddMinutes(-11));
        booking.Status = status; await f.Db.SaveChangesAsync();
        Assert.Equal(!blocked, (await f.Slots()).Slots.Any(x => x.StartUtc == response.StartUtc));
        Assert.Equal(status, booking.Status);
    }

    [Fact]
    public async Task LegacyNonPaymentPendingBookingIsNotExpired()
    {
        using var f = new CareerSchedulingTests.Fixture();
        var response = await f.Book();
        var booking = await Timestamp(f.Db, response.Id, f.Clock.Utc.AddHours(-1));
        booking.RequiresPayment = false; await f.Db.SaveChangesAsync();
        Assert.DoesNotContain((await f.Slots()).Slots, x => x.StartUtc == response.StartUtc);
        Assert.Equal(CareerBookingStatus.Pending, booking.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckoutPersistsExpiryWithoutCreatingProviderOrder(bool alreadyHasOrder)
    {
        using var f = new CareerFinanceTests.Fixture();
        var response = await f.Setup();
        var booking = await Timestamp(f.Db, response.Id, f.Scheduling.Clock.Utc);
        if (alreadyHasOrder) await f.Order(response.Id);
        f.Scheduling.Clock.Utc = f.Scheduling.Clock.Utc.AddMinutes(10);
        await Assert.ThrowsAsync<ConflictException>(() => f.Order(response.Id));
        Assert.Equal(CareerBookingStatus.Expired, booking.Status);
        Assert.Equal(alreadyHasOrder ? 1 : 0, f.Gateway.OrderCalls);
        await Assert.ThrowsAsync<ConflictException>(() => f.Order(response.Id));
    }

    [Theory]
    [InlineData("verify", false)]
    [InlineData("verify", true)]
    [InlineData("reconcile", true)]
    [InlineData("webhook", true)]
    public async Task LateCaptureRetainsEvidenceAndRequiresReviewWithoutReopening(string path, bool releaseFirst)
    {
        using var f = new CareerFinanceTests.Fixture();
        var response = await f.Setup();
        var booking = await Timestamp(f.Db, response.Id, f.Scheduling.Clock.Utc);
        var order = await f.Order(response.Id);
        f.Scheduling.Clock.Utc = f.Scheduling.Clock.Utc.AddMinutes(10);
        if (releaseFirst) { await f.Scheduling.Slots(); await f.Scheduling.Book(); }
        async Task Capture()
        {
            if (path == "verify") await f.Verify(response.Id, order);
            else if (path == "reconcile") await f.Service.ReconcileAsync(f.Candidate, response.Id, default);
            else Assert.True(await f.Service.TryWebhookAsync(JsonSerializer.SerializeToUtf8Bytes(new
            { @event = "payment.captured", payload = new { payment = new { entity = new { id = "pay_test", order_id = order.OrderId } } } }), "accepted", default));
        }
        await Capture(); await Capture();
        Assert.Equal(CareerBookingStatus.Expired, booking.Status);
        var payment = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
        Assert.NotNull(payment.PaidAtUtc);
        Assert.Equal("pay_test", payment.ProviderPaymentId);
        Assert.Equal(CareerPaymentStatus.Captured, payment.Status);
        Assert.True(payment.RequiresRefundReview);
        Assert.Single(await f.Db.Set<CareerGuidanceEarning>().ToArrayAsync());
        Assert.Empty(await f.Db.Set<CareerGuidanceRefund>().ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedCaptureCannotExpireEvenIfBookingIsPending(bool pending)
    {
        using var f = new CareerFinanceTests.Fixture();
        var response = await f.Setup();
        var booking = await Timestamp(f.Db, response.Id, f.Scheduling.Clock.Utc);
        var order = await f.Order(response.Id);
        f.Scheduling.Clock.Utc = f.Scheduling.Clock.Utc.AddSeconds(599);
        await f.Verify(response.Id, order);
        Assert.Equal(CareerBookingStatus.Confirmed, booking.Status);
        if (pending) { booking.Status = CareerBookingStatus.Pending; await f.Db.SaveChangesAsync(); }
        f.Scheduling.Clock.Utc = f.Scheduling.Clock.Utc.AddMinutes(1);
        Assert.DoesNotContain((await f.Scheduling.Slots()).Slots, x => x.StartUtc == response.StartUtc);
        Assert.Equal(pending ? CareerBookingStatus.Pending : CareerBookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task StaleExpiryCannotOverwriteCommittedCapture()
    {
        using var f = new CareerFinanceTests.Fixture();
        var response = await f.Setup();
        await Timestamp(f.Db, response.Id, f.Scheduling.Clock.Utc);
        var order = await f.Order(response.Id);
        using var staleDb = new JobPortalDbContext(f.Scheduling.Options);
        var stale = await staleDb.CareerGuidanceBookings.SingleAsync();
        await f.Verify(response.Id, order);
        Assert.True(CareerReservationPolicy.ExpireIfDue(stale, null, f.Scheduling.Clock.Utc.AddMinutes(10)));
        await Assert.ThrowsAsync<ConflictException>(() => new CareerSchedulingRepository(staleDb).SaveAsync(default));
        using var check = new JobPortalDbContext(f.Scheduling.Options);
        Assert.Equal(CareerBookingStatus.Confirmed, (await check.CareerGuidanceBookings.SingleAsync()).Status);
    }

    [Fact]
    public async Task StaleCaptureCannotOverwriteCommittedExpiry()
    {
        using var f = new CareerFinanceTests.Fixture();
        var response = await f.Setup();
        await Timestamp(f.Db, response.Id, f.Scheduling.Clock.Utc);
        var order = await f.Order(response.Id);
        using var staleDb = new JobPortalDbContext(f.Scheduling.Options);
        var staleRepository = new CareerFinanceRepository(staleDb);
        await staleRepository.ForBookingAsync(response.Id, default);
        await new CareerSchedulingRepository(f.Db).OccupiedAsync(f.Scheduling.Profile.Id, response.StartUtc, response.EndUtc,
            f.Scheduling.Clock.Utc.AddMinutes(10), default);
        var service = new CareerFinanceService(staleRepository, new UserRepository(staleDb), f.Gateway, new AuditWriterTestDouble(),
            f.Scheduling.Clock, Microsoft.Extensions.Options.Options.Create(f.Options));
        await Assert.ThrowsAsync<ConflictException>(() => service.VerifyAsync(f.Candidate, response.Id,
            new(order.OrderId, "pay_test", "accepted"), default));
        using var check = new JobPortalDbContext(f.Scheduling.Options);
        Assert.Equal(CareerBookingStatus.Expired, (await check.CareerGuidanceBookings.SingleAsync()).Status);
    }

    private static async Task<CareerGuidanceBooking> Timestamp(JobPortalDbContext db, Guid id, DateTime created)
    {
        // DbContext audit timestamps use wall time; align this stored timestamp with the fixture clock.
        var booking = await db.CareerGuidanceBookings.SingleAsync(x => x.Id == id);
        booking.CreatedAtUtc = created;
        await db.SaveChangesAsync();
        return booking;
    }
}
