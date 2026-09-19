using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class JobSourceRepository(JobPortalDbContext context)
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
            .Where(x => x.IsActive && !x.IsDeleted &&
                (x.LastRunAtUtc == null || x.LastRunAtUtc.Value.AddMinutes(x.ScanIntervalMinutes) <= nowUtc))
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
            x.AtsIdentifier == atsIdentifier && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

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
        // A failed record can clear the change tracker. Attach only this source,
        // never its Company graph, and persist only run bookkeeping.
        var entry = context.Entry(source);
        if (entry.State == EntityState.Detached)
            entry.State = EntityState.Unchanged;

        entry.Property(x => x.LastRunAtUtc).IsModified = true;
        entry.Property(x => x.LastSuccessfulRunAtUtc).IsModified = true;
        entry.Property(x => x.LastError).IsModified = true;
        entry.Property(x => x.ConsecutiveFailures).IsModified = true;
    }
}
