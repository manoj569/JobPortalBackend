using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed class CareerFinanceRepository(JobPortalDbContext db) : ICareerFinanceRepository
{
    private IQueryable<CareerGuidancePayment> Payments => db.Set<CareerGuidancePayment>().IgnoreQueryFilters()
        .Include(p => p.Booking).ThenInclude(b => b.Consultant).ThenInclude(c => c.User)
        .Include(p => p.Earning).Include(p => p.Refund);
    public Task<CareerGuidanceBooking?> BookingAsync(Guid id, CancellationToken ct) => db.CareerGuidanceBookings.IgnoreQueryFilters()
        .Include(b => b.Consultant).ThenInclude(c => c.User).Include(b => b.Service).SingleOrDefaultAsync(b => b.Id == id, ct);
    public Task<CareerGuidancePayment?> PaymentAsync(Guid id, CancellationToken ct) => Payments.SingleOrDefaultAsync(p => p.Id == id, ct);
    public Task<CareerGuidancePayment?> ForBookingAsync(Guid bookingId, CancellationToken ct) => Payments.SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
    public Task<CareerGuidancePayment?> ForOrderAsync(string orderId, CancellationToken ct) => Payments.SingleOrDefaultAsync(p => p.ProviderOrderId == orderId, ct);
    public Task<CareerGuidancePayment?> ForProviderPaymentAsync(string paymentId, CancellationToken ct) => Payments.SingleOrDefaultAsync(p => p.ProviderPaymentId == paymentId, ct);
    public Task<bool> HasEventAsync(string key, CancellationToken ct) => db.Set<CareerGuidancePaymentEvent>().IgnoreQueryFilters().AnyAsync(e => e.EventKey == key, ct);
    public void Add(CareerGuidancePayment payment) => db.Add(payment);
    public void Add(CareerGuidancePaymentEvent item) => db.Add(item);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new ConflictException("Financial state changed. Reload and retry.", "finance_concurrency"); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" or "23P01" })
        { db.ChangeTracker.Clear(); throw new ConflictException("Financial operation conflicts with an existing record. Reload and retry.", "finance_conflict"); }
    }
    public Task<PagedResponse<CareerGuidancePayment>> PaymentsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct) =>
        Page(db.Set<CareerGuidancePayment>().IgnoreQueryFilters().Where(p => admin || p.CandidateUserId == actor), query, ct);
    public Task<PagedResponse<CareerGuidanceRefund>> RefundsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct) =>
        Page(db.Set<CareerGuidanceRefund>().IgnoreQueryFilters().Where(r => admin || r.CandidateUserId == actor), query, ct);
    public Task<CareerGuidanceRefund?> RefundAsync(Guid id, CancellationToken ct) => db.Set<CareerGuidanceRefund>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
    public Task<PagedResponse<CareerGuidanceEarning>> EarningsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct) =>
        Page(db.Set<CareerGuidanceEarning>().IgnoreQueryFilters().Where(e => admin || e.Consultant.UserId == actor), query, ct);
    public async Task<IReadOnlyCollection<CareerEarningSummary>> SummaryAsync(Guid actor, CancellationToken ct) =>
        await db.Set<CareerGuidanceEarning>().IgnoreQueryFilters().AsNoTracking().Where(e => e.Consultant.UserId == actor)
            .GroupBy(e => new { e.Currency, e.Status }).Select(g => new CareerEarningSummary(g.Key.Currency, g.Key.Status,
                g.Sum(e => e.GrossAmount), g.Sum(e => e.PlatformCommissionAmount), g.Sum(e => e.NetAmount), g.Count())).ToArrayAsync(ct);
    private static async Task<PagedResponse<T>> Page<T>(IQueryable<T> source, FinanceQuery query, CancellationToken ct) where T : BaseEntity =>
        new(await source.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize).ToArrayAsync(ct), query.PageNumber, query.PageSize, await source.CountAsync(ct));
}
