using System.Data;
using System.Linq.Expressions;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class CareerAnalyticsRepository(JobPortalDbContext db, TimeProvider clock)
{
    // Counts and currency checks must see one committed snapshot, not parts of a refund transition.
    // Retrying the whole transaction keeps the configured execution strategy supported.
    private async Task<T> Snapshot<T>(Func<Task<T>> read, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return await read();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var result = await read();
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    public Task<CareerGuidanceAdminAnalytics> AdminAsync(DateTime from, DateTime to, CancellationToken ct) => Snapshot(async () =>
    {
        var consultants = db.Set<CareerConsultant>().Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var bookings = db.Set<CareerGuidanceBooking>().Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var payments = db.Set<CareerGuidancePayment>().Where(x => !x.IsDeleted && x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var sessions = db.Set<CareerGuidanceSession>().Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var reviews = CareerTrustRepository.Published(db).Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var allReviews = db.Set<CareerGuidanceReview>().Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var disputes = db.Set<CareerGuidanceDispute>().Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        await RequireInr(payments, bookings, ct);

        var c = await Counts(consultants, x => (int)x.VerificationStatus, ct);
        var b = await Counts(bookings, x => (int)x.Status, ct);
        var s = await Counts(sessions, x => (int)x.Status, ct);
        // PaidAtUtc is the durable capture fact: a subsequent refund must not erase gross volume.
        var captured = payments.Where(x => x.PaidAtUtc != null);
        var refunds = db.Set<CareerGuidanceRefund>().Where(x => x.Status == CareerRefundStatus.Processed &&
            x.ProcessedAtUtc != null && !x.Payment.IsDeleted && payments.Any(p => p.Id == x.PaymentId));
        var earnings = Earnings(payments).Where(x => x.Status != CareerEarningStatus.Reversed);
        return new CareerGuidanceAdminAnalytics(from, to,
            c.Values.Sum(), c.GetValueOrDefault(1), c.GetValueOrDefault(2), c.GetValueOrDefault(3), c.GetValueOrDefault(4),
            await consultants.CountAsync(x => x.VerificationStatus == ConsultantVerificationStatus.Verified && x.IsAcceptingBookings &&
                !x.User.IsDeleted && x.User.Status == UserStatus.Active, ct),
            b.Values.Sum(), b.GetValueOrDefault(1), b.GetValueOrDefault(2), b.GetValueOrDefault(5),
            b.GetValueOrDefault(3) + b.GetValueOrDefault(4), b.GetValueOrDefault(6), b.GetValueOrDefault(7),
            await captured.SumAsync(x => (decimal?)x.AmountGross, ct) ?? 0m,
            await captured.CountAsync(ct),
            await payments.CountAsync(x => x.PaidAtUtc == null && x.FailureCode != null, ct),
            await refunds.SumAsync(x => (decimal?)x.Amount, ct) ?? 0m, await refunds.CountAsync(ct),
            await earnings.SumAsync(x => (decimal?)x.PlatformCommissionAmount, ct) ?? 0m,
            await earnings.SumAsync(x => (decimal?)x.NetAmount, ct) ?? 0m,
            s.GetValueOrDefault(1), s.GetValueOrDefault(2), s.GetValueOrDefault(3), s.GetValueOrDefault(4), s.GetValueOrDefault(7),
            s.GetValueOrDefault(5) + s.GetValueOrDefault(6), await allReviews.CountAsync(ct), await reviews.CountAsync(ct),
            Round(await reviews.Select(x => (decimal?)x.Rating).AverageAsync(ct)),
            await disputes.CountAsync(x => x.Status >= CareerDisputeStatus.Open && x.Status <= CareerDisputeStatus.AwaitingConsultant, ct),
            await disputes.CountAsync(x => x.Status == CareerDisputeStatus.Resolved, ct),
            await disputes.CountAsync(x => x.Resolution == CareerDisputeResolution.RefundApproved, ct));
    }, ct);

    public Task<CareerGuidanceConsultantAnalytics?> ConsultantAsync(Guid userId, DateTime from, DateTime to, CancellationToken ct) => Snapshot<CareerGuidanceConsultantAnalytics?>(async () =>
    {
        var consultantId = await db.Set<CareerConsultant>().Where(x => x.UserId == userId && !x.User.IsDeleted && x.User.Status == UserStatus.Active)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (!consultantId.HasValue) return null;
        var id = consultantId.Value;
        var bookings = db.Set<CareerGuidanceBooking>().Where(x => x.ConsultantId == id && x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var payments = db.Set<CareerGuidancePayment>().Where(x => !x.IsDeleted && x.ConsultantId == id && x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var sessions = db.Set<CareerGuidanceSession>().Where(x => x.ConsultantId == id && x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var disputes = db.Set<CareerGuidanceDispute>().Where(x => x.ConsultantId == id && x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        var reviews = CareerTrustRepository.Published(db).Where(x => x.ConsultantId == id && x.CreatedAtUtc >= from && x.CreatedAtUtc < to);
        await RequireInr(payments, bookings, ct);
        var b = await Counts(bookings, x => (int)x.Status, ct);
        var earnings = Earnings(payments);
        var held = Held(earnings);
        var now = clock.GetUtcNow().UtcDateTime;
        return new(id, from, to, b.Values.Sum(), b.GetValueOrDefault(2),
            await sessions.CountAsync(x => x.Status == CareerSessionStatus.Completed, ct), b.GetValueOrDefault(3) + b.GetValueOrDefault(4),
            b.GetValueOrDefault(6), b.GetValueOrDefault(7), Round(await reviews.Select(x => (decimal?)x.Rating).AverageAsync(ct)),
            await reviews.CountAsync(ct), await bookings.SumAsync(x => (decimal?)x.PriceSnapshot, ct) ?? 0m,
            await earnings.Where(x => x.Status != CareerEarningStatus.Reversed).SumAsync(x => (decimal?)x.NetAmount, ct) ?? 0m,
            await earnings.Where(x => x.Status == CareerEarningStatus.Pending && !held.Any(h => h.Id == x.Id)).SumAsync(x => (decimal?)x.NetAmount, ct) ?? 0m,
            await held.SumAsync(x => (decimal?)x.NetAmount, ct) ?? 0m,
            await payments.CountAsync(x => x.Refund != null && !x.Refund.IsDeleted && x.Refund.Status == CareerRefundStatus.Processed && x.Refund.ProcessedAtUtc != null, ct),
            await disputes.CountAsync(x => x.Status >= CareerDisputeStatus.Open && x.Status <= CareerDisputeStatus.AwaitingConsultant, ct),
            await Upcoming().Where(x => x.ConsultantId == id && x.ScheduledStartUtc >= now).OrderBy(x => x.ScheduledStartUtc).ThenBy(x => x.Id)
                .Select(x => (DateTime?)x.ScheduledStartUtc).FirstOrDefaultAsync(ct));
    }, ct);

    public Task<CareerGuidanceCandidateSummary> CandidateAsync(Guid userId, CancellationToken ct) => Snapshot(async () =>
    {
        var bookings = db.Set<CareerGuidanceBooking>().Where(x => x.CandidateUserId == userId);
        var now = clock.GetUtcNow().UtcDateTime;
        return new CareerGuidanceCandidateSummary(userId,
            await bookings.CountAsync(x => x.StartUtc >= now && (x.Status == CareerBookingStatus.Pending || x.Status == CareerBookingStatus.Confirmed), ct),
            await bookings.CountAsync(x => x.Status == CareerBookingStatus.Completed, ct),
            await bookings.CountAsync(x => x.Status == CareerBookingStatus.CancelledByCandidate || x.Status == CareerBookingStatus.CancelledByConsultant, ct),
            await db.Set<CareerGuidanceReview>().CountAsync(x => x.CandidateUserId == userId && x.ModerationStatus == CareerReviewStatus.Pending, ct),
            await db.Set<CareerGuidanceDispute>().CountAsync(x => x.CandidateUserId == userId && x.Status >= CareerDisputeStatus.Open && x.Status <= CareerDisputeStatus.AwaitingConsultant, ct),
            await Upcoming().Where(x => x.CandidateUserId == userId && x.ScheduledStartUtc >= now).OrderBy(x => x.ScheduledStartUtc).ThenBy(x => x.Id)
                .Select(x => (DateTime?)x.ScheduledStartUtc).FirstOrDefaultAsync(ct));
    }, ct);

    private IQueryable<CareerGuidanceSession> Upcoming() => db.Set<CareerGuidanceSession>().Where(x =>
        x.Status >= CareerSessionStatus.Scheduled && x.Status <= CareerSessionStatus.InProgress && !x.Booking.IsDeleted &&
        x.Booking.Status == CareerBookingStatus.Confirmed);

    private IQueryable<CareerGuidanceEarning> Earnings(IQueryable<CareerGuidancePayment> payments) => db.Set<CareerGuidanceEarning>()
        .Where(x => !x.IsDeleted && !x.Payment.IsDeleted && x.Payment.PaidAtUtc != null && payments.Any(p => p.Id == x.PaymentId));

    private IQueryable<CareerGuidanceEarning> Held(IQueryable<CareerGuidanceEarning> earnings) => earnings.Where(x =>
        x.Status == CareerEarningStatus.Pending && (x.Payment.RequiresRefundReview ||
            db.Set<CareerGuidanceRefund>().IgnoreQueryFilters().Any(r => r.PaymentId == x.PaymentId) ||
            db.Set<CareerGuidanceDispute>().IgnoreQueryFilters().Any(d => d.PaymentId == x.PaymentId &&
                ((d.Status >= CareerDisputeStatus.Open && d.Status <= CareerDisputeStatus.AwaitingConsultant) || d.Resolution == CareerDisputeResolution.RefundApproved))));

    private async Task RequireInr(IQueryable<CareerGuidancePayment> payments, IQueryable<CareerGuidanceBooking> bookings, CancellationToken ct)
    {
        // A scalar-money contract cannot silently sum currencies. Fail closed until it supports currency groups.
        if (await payments.AnyAsync(x => x.Currency != "INR", ct) || await bookings.AnyAsync(x => x.CurrencySnapshot != "INR", ct) ||
            await db.Set<CareerGuidanceEarning>().AnyAsync(x => payments.Any(p => p.Id == x.PaymentId) && x.Currency != "INR", ct) ||
            await db.Set<CareerGuidanceRefund>().AnyAsync(x => payments.Any(p => p.Id == x.PaymentId) && x.Currency != "INR", ct))
            throw new ConflictException("Analytics requires INR records; this period contains an unsupported currency.", "analytics_currency");
    }

    private static Task<Dictionary<int, int>> Counts<T>(IQueryable<T> rows, Expression<Func<T, int>> state, CancellationToken ct) =>
        rows.GroupBy(state).Select(g => new { State = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.State, x => x.Count, ct);
    private static decimal? Round(decimal? value) => value.HasValue ? decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero) : null;
}
