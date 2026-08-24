using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class MembershipRepository(
    JobPortalDbContext context,
    TimeProvider timeProvider) : IMembershipRepository
{
    public Task<AvailableJobAccess?> GetAvailableJobAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return context.Jobs.AsNoTracking()
            .Where(x => x.Slug == slug && x.Status == JobStatus.Published && !x.IsHidden &&
                !x.IsDeleted && x.PublishedAtUtc.HasValue &&
                (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > utcNow))
            .Select(x => new AvailableJobAccess(x.Id, x.ApplicationUrl))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Membership?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return context.Memberships.AsNoTracking().SingleOrDefaultAsync(x =>
            x.UserId == userId &&
            x.Status == MembershipStatus.Active &&
            x.StartsAtUtc <= utcNow &&
            (!x.EndsAtUtc.HasValue || x.EndsAtUtc > utcNow), cancellationToken);
    }

    public Task<Membership?> GetPortalMembershipForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Memberships.Include(x => x.History)
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    public Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Memberships.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task AddAsync(Membership membership, CancellationToken cancellationToken = default) =>
        context.Memberships.AddAsync(membership, cancellationToken).AsTask();

    public async Task<IReadOnlyCollection<MembershipResponse>> GetMembershipsForUserAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        await context.Memberships.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MembershipResponse(x.Id, x.PlanName, x.Status, x.StartsAtUtc,
                x.EndsAtUtc, x.AutoRenew))
            .ToArrayAsync(cancellationToken);

    public async Task<(IReadOnlyCollection<MembershipHistoryResponse> Items, int TotalCount)> GetHistoryAsync(
        Guid userId, HistoryQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.MembershipHistories.AsNoTracking().Where(x => x.UserId == userId);
        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new MembershipHistoryResponse(x.Id, x.MembershipId, x.PreviousStatus,
                x.CurrentStatus, x.OccurredAtUtc, x.Reason)).ToArrayAsync(cancellationToken);
        return (items, count);
    }

    public async Task RecordApplicationAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (await context.UserJobHistories.AnyAsync(
                x => x.UserId == userId && x.JobId == jobId && x.Action == JobHistoryAction.Applied,
                cancellationToken)) return;
        await context.UserJobHistories.AddAsync(new UserJobHistory
        {
            UserId = userId,
            JobId = jobId,
            Action = JobHistoryAction.Applied,
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Notes = "External application URL accessed."
        }, cancellationToken);
    }
}

public sealed class PaymentRepository(JobPortalDbContext context) : IPaymentRepository
{
    public Task AddAsync(Payment payment, CancellationToken cancellationToken = default) =>
        context.Payments.AddAsync(payment, cancellationToken).AsTask();

    public Task<Payment?> GetOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        context.Payments.Include(x => x.Membership!).ThenInclude(x => x.History)
            .Include(x => x.History)
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

    public Task<Payment?> GetByProviderOrderIdAsync(
        string providerOrderId, CancellationToken cancellationToken = default) =>
        context.Payments.Include(x => x.Membership!).ThenInclude(x => x.History)
            .Include(x => x.History)
            .SingleOrDefaultAsync(x => x.ProviderOrderId == providerOrderId, cancellationToken);

    public Task<Payment?> GetOwnedByProviderOrderIdAsync(
        string providerOrderId, Guid userId, CancellationToken cancellationToken = default) =>
        context.Payments.Include(x => x.Membership!).ThenInclude(x => x.History)
            .Include(x => x.History).SingleOrDefaultAsync(
                x => x.ProviderOrderId == providerOrderId && x.UserId == userId, cancellationToken);

    public Task<Payment?> GetLatestUnresolvedMembershipAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        context.Payments.Include(x => x.Membership!).ThenInclude(x => x.History)
            .Include(x => x.History)
            .Where(x => x.UserId == userId && x.MembershipId != null &&
                x.ProviderOrderId != null &&
                (x.Status == PaymentStatus.Created || x.Status == PaymentStatus.Pending ||
                 x.Status == PaymentStatus.Authorized))
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Payment?> GetLatestForUserAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        context.Payments.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasProcessedProviderEventAsync(
        string providerEventId, CancellationToken cancellationToken = default) =>
        context.PaymentHistories.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.ProviderEventId == providerEventId, cancellationToken);

    public async Task<(IReadOnlyCollection<PaymentResponse> Items, int TotalCount)> GetForUserAsync(
        Guid userId, HistoryQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.Payments.AsNoTracking().Where(x => x.UserId == userId);
        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new PaymentResponse(x.Id, x.Amount, x.CurrencyCode, x.Status, x.Provider,
                x.ProviderOrderId, x.ProviderPaymentId, x.PaidAtUtc, x.MembershipId, x.CreatedAtUtc,
                x.ProviderOrderCreatedAtUtc, x.LastReconciledAtUtc))
            .ToArrayAsync(cancellationToken);
        return (items, count);
    }

    public async Task<(IReadOnlyCollection<PaymentHistoryResponse> Items, int TotalCount)> GetHistoryAsync(
        Guid userId, HistoryQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.PaymentHistories.AsNoTracking().Where(x => x.UserId == userId);
        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new PaymentHistoryResponse(x.Id, x.PaymentId, x.PreviousStatus,
                x.CurrentStatus, x.OccurredAtUtc, x.ProviderEventId, x.Reason))
            .ToArrayAsync(cancellationToken);
        return (items, count);
    }
}
