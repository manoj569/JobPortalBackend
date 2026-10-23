using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPortal.Persistence.Repositories;

public sealed class JobSourceRepository(JobPortalDbContext context, IOptions<JobAggregationOptions>? options = null)
    : IJobSourceManagementRepository
{
    public async Task<IReadOnlyCollection<JobSource>> GetDueSourcesAsync(
        DateTime nowUtc, int maxResults, CancellationToken cancellationToken = default) =>
        await DueSourcesQuery(nowUtc, maxResults).ToArrayAsync(cancellationToken);

    internal IQueryable<JobSource> DueSourcesQuery(DateTime nowUtc, int maxResults)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, 100);
        return context.JobSources.AsNoTracking()
            .Where(JobSourceSchedule.DuePredicate(nowUtc,
                options?.Value.Scheduler.InterruptedRunCooldownMinutes ?? 60))
            // Inventory and generic website sources (including the old UI alias)
            // execute through the existing scheduler and durable run worker.
            .Where(x => x.AtsType != AtsType.Custom || (x.AtsIdentifier == EmployerDirectInventoryContract.Identifier || x.AtsIdentifier == GenericEmployerContract.Identifier || x.AtsIdentifier == GenericEmployerContract.LegacyIdentifier))
            .Where(x => !context.JobSourceRuns.Where(JobSourceRunStore.ActivePredicate).Any(r => r.JobSourceId == x.Id))
            .OrderBy(x => x.LastRunAtUtc.HasValue)
            .ThenBy(x => x.LastRunAtUtc).ThenBy(x => x.Id)
            .Take(maxResults);
    }

    public async Task<(IReadOnlyCollection<JobSource> Items, int TotalCount)> SearchAsync(
        JobSourceSearchQuery query, CancellationToken cancellationToken = default)
    {
        var items = context.JobSources.AsNoTracking().Include(x => x.Company).AsQueryable();
        if (query.CompanyId.HasValue) items = items.Where(x => x.CompanyId == query.CompanyId);
        if (query.AtsType.HasValue) items = items.Where(x => x.AtsType == query.AtsType);
        if (query.IsActive.HasValue) items = items.Where(x => x.IsActive == query.IsActive);
        var count = await items.CountAsync(cancellationToken);
        var page = await items.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return (page, count);
    }

    public Task<bool> ConfigurationExistsAsync(Guid companyId, AtsType atsType, string? atsIdentifier,
        Guid? excludingId = null, CancellationToken cancellationToken = default) =>
        context.JobSources.AnyAsync(x => x.CompanyId == companyId && x.AtsType == atsType &&
            (x.AtsIdentifier == atsIdentifier || atsType == AtsType.Custom &&
                (atsIdentifier == GenericEmployerContract.Identifier || atsIdentifier == GenericEmployerContract.LegacyIdentifier) &&
                (x.AtsIdentifier == GenericEmployerContract.Identifier || x.AtsIdentifier == GenericEmployerContract.LegacyIdentifier)) &&
            (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

    public Task AddAsync(JobSource source, CancellationToken cancellationToken = default) =>
        context.JobSources.AddAsync(source, cancellationToken).AsTask();

    public void Remove(JobSource source) => context.JobSources.Remove(source);

    public Task<JobSource?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        context.JobSources
            .Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Update(JobSource source)
    {
        // Item recovery can detach the runner's source, then publication can load
        // another instance. Keep EF's identity map and copy only run bookkeeping;
        // never overwrite configuration or attach the Company graph.
        var entry = context.ChangeTracker.Entries<JobSource>()
            .FirstOrDefault(x => x.Entity.Id == source.Id) ?? context.Entry(source);
        var tracked = entry.Entity;
        if (!ReferenceEquals(tracked, source))
        {
            tracked.EmployerStrategyJson = source.EmployerStrategyJson;
            tracked.EmployerDiagnosticsJson = source.EmployerDiagnosticsJson;
            tracked.LastInventoryAtUtc = source.LastInventoryAtUtc;
            tracked.LastInventoryVersion = source.LastInventoryVersion;
            tracked.LastInventoryDigest = source.LastInventoryDigest;
            tracked.LastObservedSnapshotId = source.LastObservedSnapshotId;
            tracked.LastObservedAtUtc = source.LastObservedAtUtc;
            tracked.LastRunAtUtc = source.LastRunAtUtc;
            tracked.LastSuccessfulRunAtUtc = source.LastSuccessfulRunAtUtc;
            tracked.LastError = source.LastError;
            tracked.ConsecutiveFailures = source.ConsecutiveFailures;
        }
        if (entry.State == EntityState.Detached)
            entry.State = EntityState.Unchanged;

        entry.Property(x => x.EmployerStrategyJson).IsModified = true;
        entry.Property(x => x.EmployerDiagnosticsJson).IsModified = true;
        entry.Property(x => x.LastInventoryAtUtc).IsModified = true;
        entry.Property(x => x.LastInventoryVersion).IsModified = true;
        entry.Property(x => x.LastInventoryDigest).IsModified = true;
        entry.Property(x => x.LastObservedSnapshotId).IsModified = true;
        entry.Property(x => x.LastObservedAtUtc).IsModified = true;
        entry.Property(x => x.LastRunAtUtc).IsModified = true;
        entry.Property(x => x.LastSuccessfulRunAtUtc).IsModified = true;
        entry.Property(x => x.LastError).IsModified = true;
        entry.Property(x => x.ConsecutiveFailures).IsModified = true;
    }
}
