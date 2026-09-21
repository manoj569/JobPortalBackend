using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed class CareerTrustRepository(JobPortalDbContext db) : ICareerTrustRepository
{
    public Task<CareerGuidancePayment?> PaymentAsync(Guid bookingId, CancellationToken ct) => new CareerSessionRepository(db).PaymentAsync(bookingId, ct);
    public Task<CareerGuidanceSession?> SessionAsync(Guid bookingId, CancellationToken ct) =>
        db.Set<CareerGuidanceSession>().IgnoreQueryFilters().SingleOrDefaultAsync(s => s.BookingId == bookingId, ct);
    public Task<CareerGuidanceReview?> ReviewAsync(Guid id, bool byBooking, CancellationToken ct) =>
        db.Set<CareerGuidanceReview>().IgnoreQueryFilters().SingleOrDefaultAsync(r => byBooking ? r.BookingId == id : r.Id == id, ct);
    private IQueryable<CareerGuidanceDispute> Disputes => db.Set<CareerGuidanceDispute>().IgnoreQueryFilters()
        .Include(d => d.Payment).ThenInclude(p => p.Booking).ThenInclude(b => b.Consultant).ThenInclude(c => c.User)
        .Include(d => d.Payment).ThenInclude(p => p.Booking).ThenInclude(b => b.Candidate)
        .Include(d => d.Payment).ThenInclude(p => p.Earning).Include(d => d.Payment).ThenInclude(p => p.Refund)
        .Include(d => d.Evidence);
    public Task<CareerGuidanceDispute?> DisputeAsync(Guid id, bool byBooking, CancellationToken ct) =>
        Disputes.SingleOrDefaultAsync(d => byBooking ? d.BookingId == id : d.Id == id, ct);
    public Task<PagedResponse<CareerGuidanceReview>> ReviewsAsync(CareerTrustQuery query, CancellationToken ct) =>
        Page(db.Set<CareerGuidanceReview>().IgnoreQueryFilters().Where(r => !query.ModerationStatus.HasValue || r.ModerationStatus == query.ModerationStatus), query, ct);
    public Task<PagedResponse<CareerGuidanceDispute>> DisputesAsync(Guid actor, CareerSessionAudience audience, CareerTrustQuery query, CancellationToken ct) =>
        Page(Disputes.Where(d => !d.IsDeleted && (!query.Status.HasValue || d.Status == query.Status) &&
            (audience == CareerSessionAudience.Administrator || (audience == CareerSessionAudience.Candidate ? d.CandidateUserId == actor : d.Payment.Consultant.UserId == actor))), query, ct);
    public async Task<PagedResponse<CareerPublicReview>> PublicReviewsAsync(Guid consultantId, CareerTrustQuery query, CancellationToken ct)
    {
        var rows = Published(db).Where(r => r.ConsultantId == consultantId);
        return new(await rows.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id).Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(r => new CareerPublicReview(r.Id, r.Rating, r.Title, r.Comment, "Verified candidate", r.CreatedAtUtc)).ToArrayAsync(ct),
            query.PageNumber, query.PageSize, await rows.CountAsync(ct));
    }
    internal static IQueryable<CareerGuidanceReview> Published(JobPortalDbContext db) => db.Set<CareerGuidanceReview>().IgnoreQueryFilters().AsNoTracking()
        .Where(r => !r.IsDeleted && r.IsPublished && r.ModerationStatus == CareerReviewStatus.Approved &&
            !r.Booking.IsDeleted && r.Booking.Status == CareerBookingStatus.Completed &&
            !r.Session.IsDeleted && r.Session.Status == CareerSessionStatus.Completed &&
            !r.Payment.IsDeleted && r.Payment.Status == CareerPaymentStatus.Captured && r.Payment.PaidAtUtc != null && r.Payment.Refund == null &&
            !r.Booking.Candidate.IsDeleted && r.Booking.Candidate.Status == UserStatus.Active);
    public void Add(CareerGuidanceReview review) => db.Add(review);
    public void Add(CareerGuidanceDispute dispute) => db.Add(dispute);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await CareerTrustConsistency.SynchronizeAsync(db, ct); await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new ConflictException("Trust data changed. Reload and retry.", "trust_concurrency"); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" })
        { db.ChangeTracker.Clear(); throw new ConflictException("Trust record already exists. Reload and retry.", "trust_duplicate"); }
    }
    private static async Task<PagedResponse<T>> Page<T>(IQueryable<T> rows, CareerTrustQuery query, CancellationToken ct) where T : BaseEntity =>
        new(await rows.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id).Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize).ToArrayAsync(ct), query.PageNumber, query.PageSize, await rows.CountAsync(ct));
}

// Executed BEFORE the single SaveChanges transaction in trust, finance and session repositories.
// The shared payment revision serializes completion, dispute, no-show and refund decisions.
internal static class CareerTrustConsistency
{
    public static async Task SynchronizeAsync(JobPortalDbContext db, CancellationToken ct)
    {
        var payments = db.ChangeTracker.Entries<CareerGuidancePayment>().Where(e => e.State == EntityState.Modified).Select(e => e.Entity).ToArray();
        foreach (var payment in payments)
        {
            // Materialize tracked entities, rather than database Any(): pending resolution changes
            // must override the old persisted status, and newly added disputes must be included.
            var cases = await db.Set<CareerGuidanceDispute>().IgnoreQueryFilters().Where(d => d.PaymentId == payment.Id).ToArrayAsync(ct);
            var local = db.ChangeTracker.Entries<CareerGuidanceDispute>().Where(e => e.Entity.PaymentId == payment.Id).Select(e => e.Entity);
            var held = cases.Concat(local).DistinctBy(d => d.Id).Any(d => CareerTrustService.Active(d) || d.Resolution == CareerDisputeResolution.RefundApproved);
            if (payment.Earning is { Status: CareerEarningStatus.Pending } earning &&
                (held || payment.Status != CareerPaymentStatus.Captured || payment.RequiresRefundReview || payment.Refund is not null))
            {
                earning.AvailableAtUtc = null; earning.Revision = Guid.NewGuid();
            }
            if (payment.Status != CareerPaymentStatus.Refunded && payment.Refund?.Status != CareerRefundStatus.Processed) continue;
            var review = await db.Set<CareerGuidanceReview>().IgnoreQueryFilters().SingleOrDefaultAsync(r => r.PaymentId == payment.Id, ct);
            if (review is null || review.ModerationStatus == CareerReviewStatus.Hidden && !review.IsPublished) continue;
            review.IsPublished = false; review.ModerationStatus = CareerReviewStatus.Hidden;
            review.ModerationReason = "Full refund processed."; review.Revision = Guid.NewGuid();
            db.Set<AuditLog>().Add(new() { Action = AuditAction.Update, EntityName = "CareerGuidanceTrust", EntityId = review.Id.ToString(),
                ActorRole = "System", CorrelationId = $"trust-{review.Id:N}", ChangesJson = "{\"result\":\"refunded_review_hidden\"}" });
        }
    }
}
