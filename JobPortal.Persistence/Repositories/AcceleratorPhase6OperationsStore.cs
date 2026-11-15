using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorPhase6OperationsStore(JobPortalDbContext db) : IAcceleratorPhase6OperationsStore
{
    public async Task<AcceleratorOperationalOverview> OverviewAsync(DateTime nowUtc, CancellationToken ct)
    {
        var todayUtc = nowUtc.Date;
        var tomorrow = todayUtc.AddDays(1);
        var active = await db.AcceleratorBillingCycles.AsNoTracking().CountAsync(x =>
            x.StartsAtUtc <= nowUtc && x.EndsAtUtc > nowUtc && x.Payment.Status == PaymentStatus.Paid, ct);
        var expired = await db.AcceleratorBillingCycles.AsNoTracking().CountAsync(x => x.EndsAtUtc <= nowUtc, ct);
        var pending = await AcceleratorPayments().CountAsync(x =>
            x.Status == PaymentStatus.Created || x.Status == PaymentStatus.Pending ||
            x.Status == PaymentStatus.Authorized, ct);
        var purchases = await AcceleratorPayments().CountAsync(x => x.Status == PaymentStatus.Paid &&
            x.PaidAtUtc >= todayUtc && x.PaidAtUtc < tomorrow, ct);
        var revenue = await AcceleratorPayments().Where(x => x.Status == PaymentStatus.Paid &&
            x.PaidAtUtc >= todayUtc && x.PaidAtUtc < tomorrow)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var assessments = await db.AcceleratorAssessmentAttempts.AsNoTracking()
            .CountAsync(x => x.Status == AcceleratorAssessmentStatus.Completed, ct);
        var projects = await db.AcceleratorCandidateProjects.AsNoTracking()
            .CountAsync(x => x.Status == AcceleratorCandidateProjectStatus.Completed, ct);
        var interviews = await db.AcceleratorInterviewSessions.AsNoTracking()
            .CountAsync(x => x.Status == AcceleratorInterviewSessionStatus.Completed, ct);
        var unresolved = await db.AcceleratorAiDispatchRecords.AsNoTracking().CountAsync(x =>
            x.Status == AcceleratorAiDispatchStatus.Uncertain ||
            x.Status == AcceleratorAiDispatchStatus.Dispatched, ct);
        return new(active, pending, expired, purchases, revenue, assessments, projects, interviews, unresolved);
    }

    public async Task<IReadOnlyList<AcceleratorSubscriptionOperational>> SubscriptionsAsync(int skip, int take,
        CancellationToken ct) => await db.AcceleratorBillingCycles.AsNoTracking()
        .OrderByDescending(x => x.StartsAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take)
        .Select(x => new AcceleratorSubscriptionOperational(x.Id, x.UserId, x.PaymentId, x.PlanVersion,
            x.StartsAtUtc, x.EndsAtUtc, x.Payment.Status.ToString())).ToArrayAsync(ct);

    public Task<AcceleratorSubscriptionOperational?> SubscriptionAsync(Guid id, CancellationToken ct) =>
        db.AcceleratorBillingCycles.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new AcceleratorSubscriptionOperational(x.Id, x.UserId, x.PaymentId, x.PlanVersion,
                x.StartsAtUtc, x.EndsAtUtc, x.Payment.Status.ToString())).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<AcceleratorPaymentOperational>> PaymentsAsync(bool unresolvedOnly, int skip,
        int take, CancellationToken ct)
    {
        var query = AcceleratorPayments();
        if (unresolvedOnly) query = query.Where(x =>
            x.Status == PaymentStatus.Created || x.Status == PaymentStatus.Pending ||
            x.Status == PaymentStatus.Authorized);
        return await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take)
            .Select(x => new AcceleratorPaymentOperational(x.Id, x.UserId, x.ProviderOrderId,
                x.Status.ToString(), x.Amount, x.CreatedAtUtc, x.PaidAtUtc,
                x.AcceleratorRefundReviewRequired)).ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<AcceleratorAiCostOperational>> AiCostsAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var groups = await db.AcceleratorUsageReservations.AsNoTracking().Where(x =>
                x.ReservedAtUtc >= sinceUtc || x.FinishedAtUtc >= sinceUtc ||
                x.State == AcceleratorUsageState.Reserved || x.State == AcceleratorUsageState.Dispatched ||
                x.State == AcceleratorUsageState.Uncertain)
            .GroupBy(x => new { x.Feature, x.Provider, x.Model })
            .Select(g => new AcceleratorAiCostOperational(g.Key.Feature, g.Key.Provider, g.Key.Model,
                g.Count(x => x.ReservedAtUtc >= sinceUtc),
                g.Sum(x => x.State == AcceleratorUsageState.Completed && x.FinishedAtUtc >= sinceUtc ? x.InputTokens : 0),
                g.Sum(x => x.State == AcceleratorUsageState.Completed && x.FinishedAtUtc >= sinceUtc ? x.OutputTokens : 0),
                g.Sum(x => x.State == AcceleratorUsageState.Completed && x.FinishedAtUtc >= sinceUtc
                    ? x.ActualCostInr ?? 0m : 0m),
                g.Sum(x => x.State == AcceleratorUsageState.Reserved || x.State == AcceleratorUsageState.Dispatched
                    ? x.ReservedCostInr : 0m),
                g.Sum(x => x.State == AcceleratorUsageState.Uncertain ? x.ReservedCostInr : 0m)))
            .ToArrayAsync(ct);
        var uncertainDispatches = await (from reservation in db.AcceleratorUsageReservations.AsNoTracking()
            join dispatch in db.AcceleratorAiDispatchRecords.AsNoTracking()
                on reservation.Id equals dispatch.ReservationId
            where reservation.State == AcceleratorUsageState.Dispatched &&
                dispatch.Status == AcceleratorAiDispatchStatus.Uncertain
            group reservation by new { reservation.Feature, reservation.Provider, reservation.Model } into groupRows
            select new { groupRows.Key.Feature, groupRows.Key.Provider, groupRows.Key.Model,
                Exposure = groupRows.Sum(x => x.ReservedCostInr) }).ToArrayAsync(ct);
        return groups.Select(x =>
        {
            var uncertain = uncertainDispatches.FirstOrDefault(y => y.Feature == x.Feature &&
                y.Provider == x.Provider && y.Model == x.Model)?.Exposure ?? 0m;
            return x with { ReservedExposureInr = x.ReservedExposureInr - uncertain,
                UncertainExposureInr = x.UncertainExposureInr + uncertain };
        }).OrderBy(x => x.Feature).ThenBy(x => x.Provider).ThenBy(x => x.Model).ToArray();
    }

    public async Task<IReadOnlyList<AcceleratorDispatchOperational>> DispatchesAsync(bool unresolvedOnly,
        int skip, int take, CancellationToken ct)
    {
        var query = db.AcceleratorAiDispatchRecords.AsNoTracking();
        if (unresolvedOnly) query = query.Where(x => x.Status == AcceleratorAiDispatchStatus.Uncertain ||
            x.Status == AcceleratorAiDispatchStatus.Dispatched || x.RequiresManualReview);
        return await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take)
            .Select(x => new AcceleratorDispatchOperational(x.Id, x.UserId, x.ReservationId, x.Operation,
                x.Provider, x.Model, x.Status.ToString(), x.CreatedAtUtc,
                x.ReconciliationAttemptCount, x.RequiresManualReview, x.NextReconciliationAtUtc,
                x.ReconciliationClaimExpiresAtUtc)).ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ReconcilablePhonePeOrdersAsync(DateTime cutoffUtc, int take,
        CancellationToken ct)
    {
        if (take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(take));
        return await AcceleratorPayments().Where(x => x.ProviderOrderId != null &&
            ((x.Status == PaymentStatus.Created || x.Status == PaymentStatus.Pending ||
              x.Status == PaymentStatus.Authorized) &&
             (x.LastReconciledAtUtc == null || x.LastReconciledAtUtc <= cutoffUtc) ||
             x.Status == PaymentStatus.Paid &&
             !db.AcceleratorBillingCycles.Any(cycle => cycle.PaymentId == x.Id)))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(take)
            .Select(x => x.ProviderOrderId!).ToArrayAsync(ct);
    }

    private IQueryable<JobPortal.Domain.Entities.Payment> AcceleratorPayments() => db.Payments.AsNoTracking()
        .Where(x => x.PlanCode == AcceleratorOptions.PlanCode && x.Provider == PaymentProvider.PhonePe && !x.IsDeleted);
}
