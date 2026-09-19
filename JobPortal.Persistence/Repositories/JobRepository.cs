using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class JobRepository(JobPortalDbContext context) : IJobRepository
{
    public Task<Job?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Job> query = context.Jobs;
        if (includeDeleted) query = query.IgnoreQueryFilters();
        return query
            .Include(x => x.Company)
            .Include(x => x.Category)
            .Include(x => x.Referral)
            .Include(x => x.RecruiterContact).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<Job> source = context.Jobs.AsNoTracking().IgnoreQueryFilters()
            .Include(x => x.Company).Include(x => x.Category)
            .Include(x => x.Referral);

        if (query.IsDeleted.HasValue) source = source.Where(x => x.IsDeleted == query.IsDeleted.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(x => x.Title.Contains(term) || x.ReferenceNumber.Contains(term) ||
                x.Description.Contains(term) || (x.Location != null && x.Location.Contains(term)) ||
                x.Company.Name.Contains(term) || x.Category.Name.Contains(term));
        }
        if (query.CompanyId.HasValue) source = source.Where(x => x.CompanyId == query.CompanyId);
        if (query.CategoryId.HasValue) source = source.Where(x => x.CategoryId == query.CategoryId);
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
        if (query.EmploymentType.HasValue) source = source.Where(x => x.EmploymentType == query.EmploymentType);
        if (query.WorkplaceType.HasValue) source = source.Where(x => x.WorkplaceType == query.WorkplaceType);
        if (query.ExperienceLevel.HasValue) source = source.Where(x => x.ExperienceLevel == query.ExperienceLevel);
        if (query.IsFeatured.HasValue) source = source.Where(x => x.IsFeatured == query.IsFeatured);
        if (query.IsHidden.HasValue) source = source.Where(x => x.IsHidden == query.IsHidden);
        if (query.PublishedFromUtc.HasValue) source = source.Where(x => x.PublishedAtUtc >= query.PublishedFromUtc);
        if (query.PublishedToUtc.HasValue) source = source.Where(x => x.PublishedAtUtc <= query.PublishedToUtc);
        if (query.ExpiresFromUtc.HasValue) source = source.Where(x => x.ExpiresAtUtc >= query.ExpiresFromUtc);
        if (query.ExpiresToUtc.HasValue) source = source.Where(x => x.ExpiresAtUtc <= query.ExpiresToUtc);

        var totalCount = await source.CountAsync(cancellationToken);
        source = ApplySorting(source, query.SortBy, query.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase));
        var items = await source.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        context.Companies.AnyAsync(x => x.Id == companyId, cancellationToken);
    public Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        context.Categories.AnyAsync(x => x.Id == categoryId, cancellationToken);
    public Task<int> ExpireOverduePublishedAsync(
        DateTime utcNow, CancellationToken cancellationToken = default) =>
        context.Jobs
            .Where(x => x.Status == JobStatus.Published &&
                x.ExpiresAtUtc.HasValue && x.ExpiresAtUtc <= utcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, JobStatus.Expired)
                .SetProperty(x => x.IsFeatured, false)
                .SetProperty(x => x.UpdatedAtUtc, utcNow), cancellationToken);
    public Task AddAsync(Job job, CancellationToken cancellationToken = default) =>
        context.Jobs.AddAsync(job, cancellationToken).AsTask();
    public void Update(Job job) => context.Jobs.Update(job);
    public void Remove(Job job) => context.Jobs.Remove(job);
    public async Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default) =>
        _ = await context.Jobs.IgnoreQueryFilters().Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);

    // Job Aggregation & Deduplication (Phase 1)
    public Task<Job?> FindByExternalUrlAsync(string externalUrl, CancellationToken cancellationToken = default)
    {
        var hash = JobPortal.Application.Abstractions.Jobs.ApplicationUrlIdentity.Hash(externalUrl);
        if (hash is null) return Task.FromResult<Job?>(null);
        return CanonicalUrlQuery(hash).FirstOrDefaultAsync(cancellationToken);
    }

    internal IQueryable<Job> CanonicalUrlQuery(string hash) => context.Jobs
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Category)
            .Where(x => x.CanonicalApplicationUrlHash == hash && !x.IsDeleted);

    public Task<Job?> FindByFingerprintHashAsync(string fingerprintHash, CancellationToken cancellationToken = default) =>
        context.Jobs
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.FingerprintHash == fingerprintHash && !x.IsDeleted, cancellationToken);

    public async Task<IReadOnlyList<Job>> FindCandidatesForFuzzyMatchAsync(
        Guid companyId, string title, string location, int maxResults, CancellationToken cancellationToken = default)
    {
        return await context.Jobs
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == JobStatus.Published && !x.IsDeleted)
            .OrderByDescending(x => x.PublishedAtUtc)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Job> ApplySorting(IQueryable<Job> source, string sortBy, bool descending) =>
        (sortBy.ToLowerInvariant(), descending) switch
        {
            ("title", false) => source.OrderBy(x => x.Title).ThenBy(x => x.Id),
            ("title", true) => source.OrderByDescending(x => x.Title).ThenBy(x => x.Id),
            ("updatedat", false) => source.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            ("updatedat", true) => source.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            ("publishedat", false) => source.OrderBy(x => x.PublishedAtUtc).ThenBy(x => x.Id),
            ("publishedat", true) => source.OrderByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Id),
            ("minimumsalary", false) => source.OrderBy(x => x.MinimumSalary).ThenBy(x => x.Id),
            ("minimumsalary", true) => source.OrderByDescending(x => x.MinimumSalary).ThenBy(x => x.Id),
            ("maximumsalary", false) => source.OrderBy(x => x.MaximumSalary).ThenBy(x => x.Id),
            ("maximumsalary", true) => source.OrderByDescending(x => x.MaximumSalary).ThenBy(x => x.Id),
            ("expiresat", false) => source.OrderBy(x => x.ExpiresAtUtc).ThenBy(x => x.Id),
            ("expiresat", true) => source.OrderByDescending(x => x.ExpiresAtUtc).ThenBy(x => x.Id),
            ("status", false) => source.OrderBy(x => x.Status).ThenBy(x => x.Id),
            ("status", true) => source.OrderByDescending(x => x.Status).ThenBy(x => x.Id),
            (_, false) => source.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            _ => source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
        };
}
