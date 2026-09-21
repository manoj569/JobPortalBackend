using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed class CareerSessionRepository(JobPortalDbContext db) : ICareerSessionRepository
{
    private IQueryable<CareerGuidanceSession> Sessions => db.Set<CareerGuidanceSession>().IgnoreQueryFilters()
        .Where(s => !s.IsDeleted).Include(s => s.Booking).ThenInclude(b => b.Consultant).ThenInclude(c => c.User)
        .Include(s => s.Booking).ThenInclude(b => b.Candidate).Include(s => s.Reminders);
    public Task<CareerGuidanceSession?> GetAsync(Guid id, CancellationToken ct) => Sessions.SingleOrDefaultAsync(s => s.Id == id, ct);
    public Task<CareerGuidanceSession?> ForBookingAsync(Guid bookingId, CancellationToken ct) => Sessions.SingleOrDefaultAsync(s => s.BookingId == bookingId, ct);
    public Task<CareerGuidancePayment?> PaymentAsync(Guid bookingId, CancellationToken ct) => db.Set<CareerGuidancePayment>().IgnoreQueryFilters()
        .Include(p => p.Booking).ThenInclude(b => b.Candidate).Include(p => p.Booking).ThenInclude(b => b.Consultant).ThenInclude(c => c.User)
        .Include(p => p.Earning).Include(p => p.Refund).SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
    public async Task<PagedResponse<CareerGuidanceSession>> ListAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct)
    {
        var rows = Sessions.AsNoTracking().Where(s => !s.Booking.IsDeleted && (admin || s.Booking.Consultant.UserId == actor));
        return new(await rows.OrderByDescending(s => s.ScheduledStartUtc).ThenBy(s => s.Id).Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize).ToArrayAsync(ct), query.PageNumber, query.PageSize, await rows.CountAsync(ct));
    }
    public async Task<IReadOnlyList<CareerGuidanceSessionReminder>> DueAsync(DateTime now, CancellationToken ct) =>
        await db.Set<CareerGuidanceSessionReminder>().IgnoreQueryFilters()
            .Where(r => !r.IsDeleted && r.Status == CareerReminderStatus.Pending && r.ScheduledForUtc <= now)
            .Include(r => r.Session).ThenInclude(s => s.Booking).ThenInclude(b => b.Consultant).ThenInclude(c => c.User)
            .OrderBy(r => r.ScheduledForUtc).ThenBy(r => r.Id).Take(50).ToArrayAsync(ct);
    public void Add(CareerGuidanceSession session) => db.Add(session);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new ConflictException("Session state changed. Reload and retry.", "session_concurrency"); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" or "23P01" })
        { db.ChangeTracker.Clear(); throw new ConflictException("Session operation conflicts with an existing record. Reload and retry.", "session_conflict"); }
    }
}

// Called inside the existing booking/finance unit of work, before its single atomic SaveChanges.
internal static class CareerSessionConsistency
{
    public static async Task SynchronizeAsync(JobPortalDbContext db, CancellationToken ct)
    {
        var cancelled = db.ChangeTracker.Entries<CareerGuidanceBooking>()
            .Where(e => e.State == EntityState.Modified && e.Entity.Status is CareerBookingStatus.CancelledByCandidate or CareerBookingStatus.CancelledByConsultant)
            .Select(e => e.Entity).ToArray();
        foreach (var booking in cancelled)
        {
            var session = await db.Set<CareerGuidanceSession>().Include(s => s.Reminders).SingleOrDefaultAsync(s => s.BookingId == booking.Id, ct);
            if (session is null || session.Status is CareerSessionStatus.Completed or CareerSessionStatus.CandidateNoShow or CareerSessionStatus.ConsultantNoShow or CareerSessionStatus.Cancelled) continue;
            session.Status = CareerSessionStatus.Cancelled; session.Revision = Guid.NewGuid();
            foreach (var reminder in session.Reminders.Where(r => r.Status == CareerReminderStatus.Pending))
            { reminder.Status = CareerReminderStatus.Cancelled; reminder.Revision = Guid.NewGuid(); }
            db.Set<AuditLog>().Add(new AuditLog
            {
                Action = AuditAction.Update, EntityName = "CareerGuidanceSession", EntityId = session.Id.ToString(),
                ChangesJson = "{\"result\":\"meeting_cancelled\"}", ActorRole = "System", UserId = booking.CancelledByUserId,
                CorrelationId = $"session-{session.Id:N}"
            });
        }
    }
}
