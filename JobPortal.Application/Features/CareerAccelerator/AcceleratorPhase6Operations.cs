using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record AcceleratorCheckoutRequest(Guid RequestKey);

public sealed record AcceleratorOperationalOverview(int ActiveSubscriptions, int PendingPayments,
    int ExpiredSubscriptions, int NewPurchasesToday, decimal VerifiedRevenueTodayInr,
    int AssessmentsCompleted, int ProjectsCompleted, int InterviewsCompleted,
    int UnresolvedAiDispatches);
public sealed record AcceleratorSubscriptionOperational(Guid CycleId, Guid CandidateId, Guid PaymentId,
    string PlanVersion, DateTime StartsAtUtc, DateTime EndsAtUtc, string PaymentStatus);
public sealed record AcceleratorPaymentOperational(Guid PaymentId, Guid CandidateId, string? MerchantOrderId,
    string Status, decimal AmountInr, DateTime CreatedAtUtc, DateTime? PaidAtUtc,
    bool RefundReviewRequired);
public sealed record AcceleratorAiCostOperational(AcceleratorFeature Feature, string Provider, string Model,
    int Requests, int InputTokens, int OutputTokens, decimal ConfirmedCostInr,
    decimal ReservedExposureInr, decimal UncertainExposureInr);
public sealed record AcceleratorDispatchOperational(Guid DispatchId, Guid CandidateId, Guid ReservationId,
    string Operation, string Provider, string Model, string Status, DateTime CreatedAtUtc,
    int ReconciliationAttempts, bool RequiresManualReview, DateTime? NextReconciliationAtUtc,
    DateTime? ClaimExpiresAtUtc);
public sealed record AcceleratorModelOperational(bool Enabled, string Provider, string Model,
    int MaxInputTokens, int MaxOutputTokens, int MaxRequestBytes, int TimeoutSeconds);
public sealed record AcceleratorConfigurationOperational(bool Enabled, bool AiEnabled, string PlanVersion,
    IReadOnlyDictionary<AcceleratorFeature, int> Allowances, IReadOnlyDictionary<string, bool> OperationEnabled,
    IReadOnlyDictionary<AcceleratorFeature, AcceleratorModelOperational> Models);

public interface IAcceleratorPhase6OperationsStore
{
    Task<AcceleratorOperationalOverview> OverviewAsync(DateTime nowUtc, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorSubscriptionOperational>> SubscriptionsAsync(int skip, int take, CancellationToken ct);
    Task<AcceleratorSubscriptionOperational?> SubscriptionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorPaymentOperational>> PaymentsAsync(bool unresolvedOnly, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorAiCostOperational>> AiCostsAsync(DateTime sinceUtc, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorDispatchOperational>> DispatchesAsync(bool unresolvedOnly, int skip,
        int take, CancellationToken ct);
    Task<IReadOnlyList<string>> ReconcilablePhonePeOrdersAsync(DateTime cutoffUtc, int take, CancellationToken ct);
}

public sealed class AcceleratorPhase6OperationsService(IAcceleratorPhase6OperationsStore store,
    AcceleratorService access, IOptions<AcceleratorOptions> options, TimeProvider clock)
{
    public async Task<AcceleratorOperationalOverview> OverviewAsync(Guid adminId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        return await store.OverviewAsync(clock.GetUtcNow().UtcDateTime, ct);
    }

    public async Task<IReadOnlyList<AcceleratorSubscriptionOperational>> SubscriptionsAsync(Guid adminId,
        int page, int pageSize, CancellationToken ct)
    {
        await access.Administrator(adminId, ct); AcceleratorService.Page(page, pageSize);
        return await store.SubscriptionsAsync((page - 1) * pageSize, pageSize, ct);
    }

    public async Task<AcceleratorSubscriptionOperational?> SubscriptionAsync(Guid adminId, Guid id, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        return await store.SubscriptionAsync(id, ct);
    }

    public async Task<IReadOnlyList<AcceleratorPaymentOperational>> PaymentsAsync(Guid adminId,
        bool unresolvedOnly, int page, int pageSize, CancellationToken ct)
    {
        await access.Administrator(adminId, ct); AcceleratorService.Page(page, pageSize);
        return await store.PaymentsAsync(unresolvedOnly, (page - 1) * pageSize, pageSize, ct);
    }

    public async Task<IReadOnlyList<AcceleratorAiCostOperational>> AiCostsAsync(Guid adminId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        return await store.AiCostsAsync(clock.GetUtcNow().UtcDateTime.Date, ct);
    }

    public async Task<IReadOnlyList<AcceleratorDispatchOperational>> DispatchesAsync(Guid adminId,
        bool unresolvedOnly, int page, int pageSize, CancellationToken ct)
    {
        await access.Administrator(adminId, ct); AcceleratorService.Page(page, pageSize);
        return await store.DispatchesAsync(unresolvedOnly, (page - 1) * pageSize, pageSize, ct);
    }

    public async Task<AcceleratorConfigurationOperational> ConfigurationAsync(Guid adminId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        var value = options.Value;
        return new(value.Enabled, value.AIEnabled, value.PlanVersion,
            new Dictionary<AcceleratorFeature, int>(value.Allowances),
            new Dictionary<string, bool>(value.AiOperationEnabled),
            value.Policies.ToDictionary(x => x.Key, x => new AcceleratorModelOperational(x.Value.Enabled,
                x.Value.Provider, x.Value.Model, x.Value.MaxInputTokens, x.Value.MaxOutputTokens,
                x.Value.MaxRequestBytes, x.Value.TimeoutSeconds)));
    }
}
