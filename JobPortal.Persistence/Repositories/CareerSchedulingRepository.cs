using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed class CareerSchedulingRepository(JobPortalDbContext db) : ICareerSchedulingRepository
{
    public Task<CareerConsultant?> ProfileAsync(Guid id, CancellationToken ct) => db.CareerConsultants.Include(p => p.User)
        .Include(p => p.Services.Where(s => !s.IsDeleted)).SingleOrDefaultAsync(p => p.Id == id, ct);
    public Task<CareerConsultant?> OwnedProfileAsync(Guid userId, CancellationToken ct) => db.CareerConsultants.Include(p => p.User)
        .Include(p => p.Services.Where(s => !s.IsDeleted)).SingleOrDefaultAsync(p => p.UserId == userId, ct);
    public async Task<IReadOnlyList<CareerConsultantAvailability>> WindowsAsync(Guid consultantId, CancellationToken ct) =>
        await db.CareerConsultantAvailability.Where(x => x.ConsultantId == consultantId).OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime).ToArrayAsync(ct);
    public async Task<IReadOnlyList<CareerConsultantAvailabilityException>> ExceptionsAsync(Guid consultantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.CareerConsultantAvailabilityExceptions.Where(x => x.ConsultantId == consultantId && x.LocalDate >= from && x.LocalDate <= to)
            .OrderBy(x => x.LocalDate).ThenBy(x => x.StartTime).ToArrayAsync(ct);
    public Task<CareerConsultantAvailabilityException?> ExceptionAsync(Guid consultantId, Guid id, CancellationToken ct) =>
        db.CareerConsultantAvailabilityExceptions.SingleOrDefaultAsync(x => x.ConsultantId == consultantId && x.Id == id, ct);
    public async Task<IReadOnlyList<CareerGuidanceBooking>> OccupiedAsync(Guid consultantId, DateTime from, DateTime to, CancellationToken ct) =>
        await db.CareerGuidanceBookings.AsNoTracking().Where(x => x.ConsultantId == consultantId &&
            (x.Status == CareerBookingStatus.Pending || x.Status == CareerBookingStatus.Confirmed) && x.StartUtc < to && x.EndUtc > from).ToArrayAsync(ct);
    private IQueryable<CareerGuidanceBooking> Scoped(Guid actor, bool consultant, bool admin) =>
        db.CareerGuidanceBookings.IgnoreQueryFilters().Where(b => !b.IsDeleted &&
            (admin || (consultant ? b.Consultant.UserId == actor : b.CandidateUserId == actor)));
    public Task<CareerGuidanceBooking?> BookingAsync(Guid id, Guid actor, bool consultant, bool admin, CancellationToken ct) =>
        Scoped(actor, consultant, admin).SingleOrDefaultAsync(b => b.Id == id, ct);
    public async Task<PagedResponse<CareerGuidanceBooking>> BookingsAsync(Guid actor, bool consultant, bool admin, BookingQuery query, CancellationToken ct)
    {
        var rows = Scoped(actor, consultant, admin).AsNoTracking();
        var total = await rows.CountAsync(ct);
        return new(await rows.OrderByDescending(b => b.StartUtc).ThenBy(b => b.Id).Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct),
            query.PageNumber, query.PageSize, total);
    }
    public void AddWindows(IEnumerable<CareerConsultantAvailability> windows) => db.CareerConsultantAvailability.AddRange(windows);
    public void AddException(CareerConsultantAvailabilityException exception) => db.CareerConsultantAvailabilityExceptions.Add(exception);
    public void AddBooking(CareerGuidanceBooking booking) => db.CareerGuidanceBookings.Add(booking);
    public static bool IsOverlapConflict(Exception exception) => exception is DbUpdateException
        { InnerException: PostgresException { SqlState: "23P01", ConstraintName: "EX_CareerGuidanceBookings_NoOverlap" } };
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await CareerSessionConsistency.SynchronizeAsync(db, ct); await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsOverlapConflict(ex))
        { db.ChangeTracker.Clear(); throw new ConflictException("This time slot is no longer available.", "booking_overlap"); }
        catch (DbUpdateConcurrencyException)
        { db.ChangeTracker.Clear(); throw new ConflictException("Scheduling data changed. Reload available slots and retry.", "concurrency_conflict"); }
    }
}
