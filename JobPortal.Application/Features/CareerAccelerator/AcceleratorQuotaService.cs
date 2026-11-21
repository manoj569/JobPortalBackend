using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class AcceleratorQuotaService(IAcceleratorStore store, AcceleratorService access,
    IOptions<AcceleratorOptions> options, TimeProvider clock, IAuditWriter audit, IEnumerable<IAcceleratorAIProvider> providers)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private AcceleratorOptions Settings => options.Value;

    // Non-AI outreach shares the immutable Phase 1 reservation/event ledger and
    // cross-instance budget lock. Existing billing cycles without this allowance
    // correctly have zero outreach quota until a new cycle allocates it.
    public Task<AcceleratorReservationResponse> ReserveOutreachAsync(Guid actor, Guid requestKey,
        string requestHash, CancellationToken ct) => store.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        if (requestKey == Guid.Empty || requestHash is not { Length: 64 } ||
            !requestHash.All(char.IsAsciiHexDigit))
            throw new BadRequestException("Invalid outreach reservation.");
        var previous = await store.ReservationByKeyAsync(actor, requestKey, ct);
        if (previous is not null)
        {
            if (previous.Feature != AcceleratorFeature.EmployerOutreach ||
                !string.Equals(previous.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Idempotency key was used for a different operation.");
            return Dto(previous);
        }
        var now = Now;
        await Expire(now, ct);
        var cycle = await RequiredCycle(actor, now, ct);
        var allowance = cycle.Allowances.SingleOrDefault(x =>
            x.Feature == AcceleratorFeature.EmployerOutreach)?.Allocated ?? 0;
        var budget = await store.BudgetAsync(actor, cycle.Id, AcceleratorFeature.EmployerOutreach,
            now, ct);
        if (budget.FeatureUsed >= allowance)
            throw Limit("accelerator_outreach_quota_exhausted", "Outreach allowance is exhausted.");
        var reservation = new AcceleratorUsageReservation { UserId = actor, Cycle = cycle,
            CycleId = cycle.Id, RequestKey = requestKey, RequestHash = requestHash.ToUpperInvariant(),
            Feature = AcceleratorFeature.EmployerOutreach, State = AcceleratorUsageState.Reserved,
            Provider = "CandidateMailbox", Model = "Send", ReservedAtUtc = now,
            BudgetDayUtc = now.Date, LeaseExpiresAtUtc = now.AddSeconds(Settings.ReservationSeconds),
            ReservedCostInr = 0, ReservedInputTokens = 0, ReservedOutputTokens = 0 };
        store.Add(reservation);
        await Event(reservation, ct);
        return Dto(reservation);
    }, ct);

    public Task<AcceleratorReservationResponse> ReserveAsync(Guid actor, ReserveAcceleratorUsageRequest request, CancellationToken ct) =>
        store.WriteAsync(async () =>
        {
            await access.Candidate(actor, ct);
            if (request.RequestKey == Guid.Empty || !Enum.IsDefined(request.Feature) ||
                request.Feature == AcceleratorFeature.EmployerOutreach || request.RequestHash is not { Length: 64 } ||
                !request.RequestHash.All(char.IsAsciiHexDigit) || request.InputTokens < 0 || request.RequestBytes < 0)
                throw new BadRequestException("Invalid Accelerator usage reservation.");
            var previous = await store.ReservationByKeyAsync(actor, request.RequestKey, ct);
            if (previous is not null)
            {
                if (previous.Feature != request.Feature || !string.Equals(previous.RequestHash, request.RequestHash, StringComparison.OrdinalIgnoreCase) ||
                    previous.ReservedInputTokens != request.InputTokens)
                    throw new ConflictException("Idempotency key was used for a different operation.");
                return Dto(previous);
            }
            var o = Settings;
            if (!o.AIEnabled || !o.Policies.TryGetValue(request.Feature, out var policy) || !policy.Enabled ||
                !providers.Any(p => p.Name == policy.Provider && p.IsAvailable)) throw new AcceleratorProviderUnavailableException();
            if (request.InputTokens > policy.MaxInputTokens || request.RequestBytes > policy.MaxRequestBytes || request.RequestBytes == 0)
                throw new BadRequestException("Request exceeds the configured AI input limits.", "accelerator_input_limit");
            var now = Now;
            await Expire(now, ct);
            var cycle = await RequiredCycle(actor, now, ct);
            var allocation = cycle.Allowances.SingleOrDefault(x => x.Feature == request.Feature)?.Allocated ?? 0;
            var budget = await store.BudgetAsync(actor, cycle.Id, request.Feature, now, ct);
            if (budget.FeatureUsed >= allocation)
                throw Limit("accelerator_quota_exhausted", "The billing-cycle allowance is exhausted.");
            if (budget.RecentRequests >= o.RequestsPerMinute)
                throw Limit("accelerator_rate_limit", "Too many AI requests.");
            if (budget.GlobalInFlight >= o.GlobalConcurrency || budget.UserInFlight >= o.UserConcurrency)
                throw Limit("accelerator_concurrency_limit", "AI concurrency limit reached.");
            var cost = policy.MaximumCost(request.InputTokens);
            if (cost > o.PerRequestCostLimitInr || budget.CycleCost + cost > o.UserCycleCostLimitInr ||
                budget.UserDailyCost + cost > o.UserDailyCostLimitInr || budget.GlobalDailyCost + cost > o.GlobalDailyCostLimitInr ||
                budget.GlobalMonthlyCost + cost > o.GlobalMonthlyCostLimitInr)
                throw Limit("accelerator_cost_limit", "AI spending limit reached.");
            var reservation = new AcceleratorUsageReservation { UserId = actor, Cycle = cycle, CycleId = cycle.Id,
                RequestKey = request.RequestKey, RequestHash = request.RequestHash.ToUpperInvariant(), Feature = request.Feature,
                State = AcceleratorUsageState.Reserved, Provider = policy.Provider, Model = policy.Model,
                ReservedAtUtc = now, BudgetDayUtc = now.Date, LeaseExpiresAtUtc = now.AddSeconds(o.ReservationSeconds),
                ReservedCostInr = cost, ReservedInputTokens = request.InputTokens, ReservedOutputTokens = policy.MaxOutputTokens };
            store.Add(reservation);
            await Event(reservation, ct);
            return Dto(reservation);
        }, ct);

    // Must commit before the network call. This is not a provider retry/dispatch implementation.
    public Task<AcceleratorReservationResponse> MarkDispatchedAsync(Guid actor, Guid reservationId, CancellationToken ct) =>
        store.WriteAsync(async () =>
        {
            await access.Candidate(actor, ct);
            var row = await RequiredReservation(actor, reservationId, ct);
            if (row.State != AcceleratorUsageState.Reserved)
                throw new ConflictException("This operation cannot be dispatched again.", "accelerator_dispatch_conflict");
            var now = Now;
            var cycle = await RequiredCycle(actor, now, ct);
            if (cycle.Id != row.CycleId || now >= row.LeaseExpiresAtUtc || row.BudgetDayUtc != now.Date)
                throw new ConflictException("Reservation expired; no AI call may be made.", "accelerator_reservation_expired");
            row.State = AcceleratorUsageState.Dispatched;
            row.Revision = Guid.NewGuid();
            await Event(row, ct);
            return Dto(row);
        }, ct);

    public Task<AcceleratorReservationResponse> ReleaseUnusedAsync(Guid actor, Guid reservationId, CancellationToken ct) =>
        store.WriteAsync(async () =>
        {
            await access.Candidate(actor, ct);
            var row = await RequiredReservation(actor, reservationId, ct);
            if (row.State == AcceleratorUsageState.Released) return Dto(row);
            if (row.State != AcceleratorUsageState.Reserved) throw new ConflictException("Potentially chargeable calls require reconciliation, not release.");
            row.State = AcceleratorUsageState.Released; row.FinishedAtUtc = Now; row.Revision = Guid.NewGuid();
            await Event(row, ct);
            return Dto(row);
        }, ct);

    public Task<AcceleratorReservationResponse> MarkOutreachUncertainAsync(Guid actor,
        Guid reservationId, CancellationToken ct) => store.WriteAsync(async () =>
    {
        access.Enabled();
        var row = await RequiredReservation(actor, reservationId, ct);
        if (row.Feature != AcceleratorFeature.EmployerOutreach ||
            row.State is not (AcceleratorUsageState.Dispatched or AcceleratorUsageState.Uncertain))
            throw new ConflictException("Only an unresolved outreach dispatch may become uncertain.");
        if (row.State == AcceleratorUsageState.Uncertain) return Dto(row);
        row.State = AcceleratorUsageState.Uncertain;
        row.Revision = Guid.NewGuid();
        await Event(row, ct);
        return Dto(row);
    }, ct);

    // Internal reconciliation only: provider evidence must confirm non-execution or non-chargeability.
    public Task<AcceleratorReservationResponse> ResolveVerifiedNonChargeableAsync(Guid actor, Guid reservationId,
        string providerRequestId, string evidenceReference, CancellationToken ct) => store.WriteAsync(async () =>
    {
        access.Enabled();
        if (string.IsNullOrWhiteSpace(providerRequestId) || providerRequestId.Length > 200 ||
            string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Length > 500)
            throw new BadRequestException("Verified provider failure evidence is required.");
        var row = await RequiredReservation(actor, reservationId, ct);
        if (row.State == AcceleratorUsageState.Released) return Dto(row);
        if (row.State is not (AcceleratorUsageState.Dispatched or AcceleratorUsageState.Uncertain))
            throw new ConflictException("Only an unresolved dispatched reservation may be reconciled.");
        row.State = AcceleratorUsageState.Released;
        row.ActualCostInr = 0;
        row.FinishedAtUtc = Now;
        row.Revision = Guid.NewGuid();
        await Event(row, ct);
        await audit.AppendAsync(new AuditEvent(AuditAction.Confirm, "AcceleratorUsageVerifiedFailure", row.Id.ToString(),
            new Dictionary<string, string?> { ["providerRequestId"] = providerRequestId, ["evidenceReference"] = evidenceReference }), ct);
        return Dto(row);
    }, ct);

    // Trusted result accounting seam; no candidate-facing completion/billing endpoint exists.
    public Task<AcceleratorReservationResponse> CompleteAsync(Guid actor, Guid reservationId, int inputTokens,
        int outputTokens, decimal actualCostInr, CancellationToken ct) => store.WriteAsync(async () =>
    {
        access.Enabled();
        if (inputTokens is < 0 or > 1000000 || outputTokens is < 0 or > 1000000 || actualCostInr is < 0 or > 1000000 ||
            decimal.Round(actualCostInr, 6) != actualCostInr) throw new BadRequestException("Invalid provider accounting evidence.");
        var row = await RequiredReservation(actor, reservationId, ct);
        if (row.State == AcceleratorUsageState.Completed)
        {
            if (row.ActualCostInr != actualCostInr || row.InputTokens != inputTokens || row.OutputTokens != outputTokens)
                throw new ConflictException("Completed AI accounting is immutable.");
            return Dto(row);
        }
        if (row.State is not (AcceleratorUsageState.Dispatched or AcceleratorUsageState.Uncertain))
            throw new ConflictException("Only dispatched calls may be completed.");
        // Persist an actual charge even if a provider violated its estimate; never erase financial facts.
        row.PolicyViolation = inputTokens > row.ReservedInputTokens || outputTokens > row.ReservedOutputTokens || actualCostInr > row.ReservedCostInr;
        row.InputTokens = inputTokens; row.OutputTokens = outputTokens; row.ActualCostInr = actualCostInr;
        row.State = AcceleratorUsageState.Completed; row.FinishedAtUtc = Now; row.Revision = Guid.NewGuid();
        await Event(row, ct);
        return Dto(row);
    }, ct);

    public async Task<IReadOnlyList<AcceleratorUsageHistoryItem>> HistoryAsync(Guid actor, int page, int pageSize, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        AcceleratorService.Page(page, pageSize);
        return (await store.UsageHistoryAsync(actor, (page - 1) * pageSize, pageSize, ct))
            .Select(x => new AcceleratorUsageHistoryItem(x.Id, x.Feature, x.State, x.ReservedAtUtc, x.FinishedAtUtc)).ToArray();
    }

    public Task<int> MaintainAsync(CancellationToken ct) => store.WriteAsync(async () =>
    {
        access.Enabled();
        var changed = await Expire(Now, ct);
        foreach (var profile in await store.DeletedUserProfilesAsync(100, ct))
        {
            profile.EducationSummary = string.Empty; profile.LinkedResumeUploadedAtUtc = null;
            profile.ExperienceYears = 0; profile.WeeklyStudyMinutes = 15; profile.IsDeleted = true;
            foreach (var skill in profile.Skills) skill.IsDeleted = true;
            profile.Revision = Guid.NewGuid(); changed++;
        }
        return changed;
    }, ct);

    private async Task<int> Expire(DateTime now, CancellationToken ct)
    {
        var rows = await store.ExpiredReservationsAsync(now, 100, ct);
        foreach (var row in rows)
        {
            row.State = row.State == AcceleratorUsageState.Reserved ? AcceleratorUsageState.Released : AcceleratorUsageState.Uncertain;
            row.FinishedAtUtc = now; row.Revision = Guid.NewGuid();
            await Event(row, ct);
        }
        return rows.Count;
    }
    private async Task<AcceleratorBillingCycle> RequiredCycle(Guid actor, DateTime now, CancellationToken ct)
    {
        var cycle = await access.ResolveAccessCycleAsync(actor, ct);
        if (cycle is null || cycle.Payment.IsDeleted || cycle.Payment.Status != PaymentStatus.Paid)
            throw new AppException("An active Accelerator subscription is required.", 403, "accelerator_subscription_required");
        return cycle;
    }
    private async Task<AcceleratorUsageReservation> RequiredReservation(Guid actor, Guid id, CancellationToken ct) =>
        await store.ReservationAsync(actor, id, ct) ?? throw new NotFoundException("Usage reservation not found.");
    private async Task Event(AcceleratorUsageReservation row, CancellationToken ct)
    {
        store.Add(new AcceleratorUsageEvent { Reservation = row, ReservationId = row.Id, State = row.State,
            OccurredAtUtc = Now, CostInr = Cost(row) });
        await audit.AppendAsync(new(AuditAction.Update, "AcceleratorUsage", row.Id.ToString(),
            new Dictionary<string, string?> { ["status"] = row.State.ToString() }), ct);
    }
    private static decimal Cost(AcceleratorUsageReservation row) => row.ActualCostInr ?? row.ReservedCostInr;
    private static AppException Limit(string code, string message) => new(message, 429, code);
    private static AcceleratorReservationResponse Dto(AcceleratorUsageReservation r) =>
        new(r.Id, r.RequestKey, r.Feature, r.State, r.LeaseExpiresAtUtc, r.ReservedCostInr, r.ActualCostInr, r.PolicyViolation);
}

public sealed class UnavailableAcceleratorAIProvider : IAcceleratorAIProvider
{
    public string Name => "Unavailable";
    public bool IsAvailable => false;
    public Task<AcceleratorAIResult> ExecuteAsync(AcceleratorAIRequest request, CancellationToken ct) =>
        throw new AcceleratorProviderUnavailableException();
}
