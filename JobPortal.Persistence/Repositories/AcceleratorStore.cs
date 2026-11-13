using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorStore(JobPortalDbContext context) : IAcceleratorStore
{
    private static readonly SemaphoreSlim NonrelationalTestGate = new(1, 1);
    private const long FeatureLock = 0x414343454C455241;
    public async Task<T> WriteAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        if (!context.Database.IsRelational())
        {
            await NonrelationalTestGate.WaitAsync(ct);
            try { var value = await action(); await context.SaveChangesAsync(ct); return value; }
            catch { context.ChangeTracker.Clear(); throw; }
            finally { NonrelationalTestGate.Release(); }
        }
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            // One cross-instance budget lock, consistently acquired first; no network/provider calls in this scope.
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({FeatureLock})", ct);
            try
            {
                var value = await action();
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return value;
            }
            catch (DbUpdateConcurrencyException)
            { throw new ConflictException("Accelerator data changed; reload and retry.", "accelerator_concurrency"); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            { throw new ConflictException("Concurrent Accelerator write; retry the same operation.", "accelerator_write_conflict"); }
        });
    }
    public Task<User?> UserAsync(Guid id, CancellationToken ct) => context.Users.AsNoTracking().Include(x => x.Role)
        .SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Skill>> SkillsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await context.Skills.Where(x => ids.Contains(x.Id)).ToArrayAsync(ct);
    public Task<Skill?> SkillByNameAsync(string normalizedName, CancellationToken ct) =>
        context.Skills.SingleOrDefaultAsync(x => x.NormalizedName == normalizedName, ct);
    public async Task<IReadOnlyList<Skill>> SearchSkillsAsync(string normalizedQuery, int skip, int take, CancellationToken ct) =>
        await context.Skills.AsNoTracking().Where(x => x.NormalizedName.Contains(normalizedQuery))
            .OrderBy(x => x.NormalizedName).ThenBy(x => x.Id).Skip(skip).Take(take).ToArrayAsync(ct);
    public Task<AcceleratorTaxonomy?> TaxonomyAsync(Guid? id, string? version, CancellationToken ct) =>
        context.Set<AcceleratorTaxonomy>().AsSplitQuery()
            .Include(x => x.Competencies)
            .Include(x => x.Competencies).ThenInclude(x => x.Parent)
            .Include(x => x.Competencies).ThenInclude(x => x.Prerequisites).ThenInclude(x => x.RequiredCompetency)
            .Include(x => x.Roles).ThenInclude(x => x.Requirements).ThenInclude(x => x.Competency)
            .Where(x => (!id.HasValue || x.Id == id) && (version == null || x.Version == version))
            .OrderByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
    public Task<AcceleratorCareerRole?> RoleAsync(Guid id, CancellationToken ct) =>
        context.Set<AcceleratorCareerRole>().Include(x => x.Taxonomy).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<AcceleratorProfile?> ProfileAsync(Guid userId, CancellationToken ct) =>
        context.Set<AcceleratorProfile>().Include(x => x.Skills).SingleOrDefaultAsync(x => x.UserId == userId, ct);
    public Task<AcceleratorCareerGoal?> GoalByKeyAsync(Guid userId, Guid key, CancellationToken ct) =>
        Goals().SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == key, ct);
    public Task<AcceleratorCareerGoal?> GoalAsync(Guid userId, Guid id, CancellationToken ct) =>
        Goals().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public async Task<IReadOnlyList<AcceleratorCareerGoal>> GoalsAsync(Guid userId, int skip, int take, CancellationToken ct) =>
        await Goals().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToArrayAsync(ct);
    private IQueryable<AcceleratorCareerGoal> Goals() => context.Set<AcceleratorCareerGoal>().Include(x => x.Role).ThenInclude(x => x.Taxonomy);
    public Task<Payment?> PaymentAsync(Guid id, CancellationToken ct) => context.Payments.SingleOrDefaultAsync(x => x.Id == id, ct);
    private IQueryable<AcceleratorBillingCycle> Cycles() => context.Set<AcceleratorBillingCycle>().Include(x => x.Payment).Include(x => x.Allowances);
    public Task<AcceleratorBillingCycle?> CycleForPaymentAsync(Guid paymentId, CancellationToken ct) =>
        Cycles().SingleOrDefaultAsync(x => x.PaymentId == paymentId, ct);
    public Task<AcceleratorBillingCycle?> ActiveCycleAsync(Guid userId, DateTime now, CancellationToken ct) =>
        Cycles().SingleOrDefaultAsync(x => x.UserId == userId && x.StartsAtUtc <= now && x.EndsAtUtc > now, ct);
    public Task<AcceleratorBillingCycle?> LatestCycleAsync(Guid userId, CancellationToken ct) =>
        Cycles().Where(x => x.UserId == userId).OrderByDescending(x => x.StartsAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
    public Task<bool> CycleOverlapsAsync(Guid userId, DateTime start, DateTime end, CancellationToken ct) =>
        context.Set<AcceleratorBillingCycle>().AnyAsync(x => x.UserId == userId && x.StartsAtUtc < end && x.EndsAtUtc > start, ct);
    public Task<AcceleratorUsageReservation?> ReservationByKeyAsync(Guid userId, Guid key, CancellationToken ct) =>
        context.Set<AcceleratorUsageReservation>().SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == key, ct);
    public Task<AcceleratorUsageReservation?> ReservationAsync(Guid userId, Guid id, CancellationToken ct) =>
        context.Set<AcceleratorUsageReservation>().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public async Task<AcceleratorBudgetSnapshot> BudgetAsync(Guid userId, Guid cycleId, AcceleratorFeature feature, DateTime now, CancellationToken ct)
    {
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var from = month.AddMinutes(-5);
        var recent = now.AddMinutes(-1);
        var day = now.Date;
        // Server-side aggregates: no global monthly ledger materialization on each reservation.
        var result = await context.Set<AcceleratorUsageReservation>().Where(x => x.BudgetDayUtc >= from || x.CycleId == cycleId)
            .GroupBy(x => 1).Select(g => new AcceleratorBudgetSnapshot(
                g.Count(x => x.CycleId == cycleId && x.Feature == feature && x.State != AcceleratorUsageState.Released &&
                    !(x.State == AcceleratorUsageState.Reserved && x.LeaseExpiresAtUtc <= now)),
                g.Count(x => x.UserId == userId && x.ReservedAtUtc > recent),
                g.Count(x => x.UserId == userId && (x.State == AcceleratorUsageState.Reserved || x.State == AcceleratorUsageState.Dispatched) && x.LeaseExpiresAtUtc > now),
                g.Count(x => (x.State == AcceleratorUsageState.Reserved || x.State == AcceleratorUsageState.Dispatched) && x.LeaseExpiresAtUtc > now),
                g.Sum(x => x.CycleId == cycleId && x.State != AcceleratorUsageState.Released && !(x.State == AcceleratorUsageState.Reserved && x.LeaseExpiresAtUtc <= now) ? x.ActualCostInr ?? x.ReservedCostInr : 0m),
                g.Sum(x => x.UserId == userId && x.BudgetDayUtc == day && x.State != AcceleratorUsageState.Released && !(x.State == AcceleratorUsageState.Reserved && x.LeaseExpiresAtUtc <= now) ? x.ActualCostInr ?? x.ReservedCostInr : 0m),
                g.Sum(x => x.BudgetDayUtc == day && x.State != AcceleratorUsageState.Released && !(x.State == AcceleratorUsageState.Reserved && x.LeaseExpiresAtUtc <= now) ? x.ActualCostInr ?? x.ReservedCostInr : 0m),
                g.Sum(x => x.BudgetDayUtc >= month && x.State != AcceleratorUsageState.Released && !(x.State == AcceleratorUsageState.Reserved && x.LeaseExpiresAtUtc <= now) ? x.ActualCostInr ?? x.ReservedCostInr : 0m)))
            .SingleOrDefaultAsync(ct);
        return result ?? new(0, 0, 0, 0, 0, 0, 0, 0);
    }
    public async Task<IReadOnlyList<AcceleratorUsageReservation>> CycleUsageAsync(Guid cycleId, CancellationToken ct) =>
        await context.Set<AcceleratorUsageReservation>().AsNoTracking().Where(x => x.CycleId == cycleId).ToArrayAsync(ct);
    public async Task<IReadOnlyList<AcceleratorUsageReservation>> UsageHistoryAsync(Guid userId, int skip, int take, CancellationToken ct) =>
        await context.Set<AcceleratorUsageReservation>().AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.ReservedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToArrayAsync(ct);
    public async Task<IReadOnlyList<AcceleratorUsageReservation>> ExpiredReservationsAsync(DateTime now, int take, CancellationToken ct) =>
        await context.Set<AcceleratorUsageReservation>().Where(x =>
            (x.State == AcceleratorUsageState.Reserved || x.State == AcceleratorUsageState.Dispatched) && x.LeaseExpiresAtUtc <= now)
            .OrderBy(x => x.LeaseExpiresAtUtc).ThenBy(x => x.Id).Take(take).ToArrayAsync(ct);
    public async Task<IReadOnlyList<AcceleratorProfile>> DeletedUserProfilesAsync(int take, CancellationToken ct) =>
        await context.Set<AcceleratorProfile>().Include(x => x.Skills).Where(x => !x.IsDeleted && context.Users.IgnoreQueryFilters().Any(u => u.Id == x.UserId && u.IsDeleted))
            .OrderBy(x => x.Id).Take(take).ToArrayAsync(ct);
    public void Add(object entity) => context.Add(entity);
}
