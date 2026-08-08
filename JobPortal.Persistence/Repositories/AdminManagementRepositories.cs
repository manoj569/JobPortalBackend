using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class CompanyManagementRepository(JobPortalDbContext context) : ICompanyManagementRepository
{
    public async Task<(IReadOnlyCollection<CompanyResponse> Items, int TotalCount)> SearchAsync(
        CompanySearchQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.Companies.AsNoTracking().IgnoreQueryFilters();
        if (query.IsDeleted.HasValue) source = source.Where(x => x.IsDeleted == query.IsDeleted);
        if (query.IsVerified.HasValue) source = source.Where(x => x.IsVerified == query.IsVerified);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(x => x.Name.Contains(term) || x.Slug.Contains(term) ||
                (x.Industry != null && x.Industry.Contains(term)) ||
                (x.Location != null && x.Location.Contains(term)));
        }

        var totalCount = await source.CountAsync(cancellationToken);
        source = Sort(source, query.SortBy, query.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase));
        var items = await Project(source).Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize).ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<Company?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Companies.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Company?> FindByNameOrSlugAsync(string normalizedName, string slug, CancellationToken cancellationToken = default)
    {
#pragma warning disable CA1304, CA1311, CA1862 // EF translates casing to SQL UPPER; comparison overloads are not translatable.
        var name = normalizedName.ToUpper();
        var normalizedSlug = slug.ToUpper();
        return context.Companies.SingleOrDefaultAsync(
            x => x.Name.ToUpper() == name || x.Slug.ToUpper() == normalizedSlug, cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
    }

    public Task<CompanyResponse?> GetResponseAsync(Guid id, CancellationToken cancellationToken = default) =>
        Project(context.Companies.AsNoTracking().IgnoreQueryFilters().Where(x => x.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludingId = null, CancellationToken cancellationToken = default) =>
        context.Companies.AnyAsync(x => x.Slug == slug && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

    public Task<bool> HasJobsAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Jobs.AnyAsync(x => x.CompanyId == id, cancellationToken);

    public Task AddAsync(Company company, CancellationToken cancellationToken = default) =>
        context.Companies.AddAsync(company, cancellationToken).AsTask();

    public void Remove(Company company) => context.Companies.Remove(company);

    public async Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default) =>
        await context.Companies.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new AdminOptionResponse(x.Id, x.Name, x.Slug)).ToArrayAsync(cancellationToken);

    private static IQueryable<CompanyResponse> Project(IQueryable<Company> source) =>
        source.Select(x => new CompanyResponse(x.Id, x.Name, x.Slug, x.Description, x.WebsiteUrl,
            x.LogoUrl, x.Industry, x.Location, x.EmployeeCount, x.IsVerified,
            x.CreatedAtUtc, x.UpdatedAtUtc, x.IsDeleted, x.CompanyType));

    private static IQueryable<Company> Sort(IQueryable<Company> source, string field, bool descending) =>
        (field.ToLowerInvariant(), descending) switch
        {
            ("name", false) => source.OrderBy(x => x.Name).ThenBy(x => x.Id),
            ("name", true) => source.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            ("industry", false) => source.OrderBy(x => x.Industry).ThenBy(x => x.Id),
            ("industry", true) => source.OrderByDescending(x => x.Industry).ThenBy(x => x.Id),
            ("employeecount", false) => source.OrderBy(x => x.EmployeeCount).ThenBy(x => x.Id),
            ("employeecount", true) => source.OrderByDescending(x => x.EmployeeCount).ThenBy(x => x.Id),
            ("updatedat", false) => source.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            ("updatedat", true) => source.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            (_, false) => source.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            _ => source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
        };
}

public sealed class CategoryManagementRepository(JobPortalDbContext context) : ICategoryManagementRepository
{
    public async Task<(IReadOnlyCollection<CategoryResponse> Items, int TotalCount)> SearchAsync(
        CategorySearchQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.Categories.AsNoTracking().IgnoreQueryFilters();
        if (query.IsDeleted.HasValue) source = source.Where(x => x.IsDeleted == query.IsDeleted);
        if (query.RootOnly) source = source.Where(x => x.ParentCategoryId == null);
        else if (query.ParentCategoryId.HasValue) source = source.Where(x => x.ParentCategoryId == query.ParentCategoryId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(x => x.Name.Contains(term) || x.Slug.Contains(term) ||
                (x.Description != null && x.Description.Contains(term)));
        }

        var totalCount = await source.CountAsync(cancellationToken);
        source = Sort(source, query.SortBy, query.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase));
        var items = await Project(source).Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize).ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Categories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Category?> FindByNameOrSlugAsync(string name, string slug, CancellationToken cancellationToken = default)
    {
#pragma warning disable CA1304, CA1311, CA1862 // EF translates casing to SQL UPPER; comparison overloads are not translatable.
        var normalizedName = name.ToUpper();
        var normalizedSlug = slug.ToUpper();
        return context.Categories.SingleOrDefaultAsync(
            x => x.Name.ToUpper() == normalizedName || x.Slug.ToUpper() == normalizedSlug,
            cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
    }

    public Task<CategoryResponse?> GetResponseAsync(Guid id, CancellationToken cancellationToken = default) =>
        Project(context.Categories.AsNoTracking().IgnoreQueryFilters().Where(x => x.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludingId = null, CancellationToken cancellationToken = default) =>
        context.Categories.AnyAsync(x => x.Slug == slug && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Categories.AnyAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> IsDescendantAsync(
        Guid categoryId, Guid possibleDescendantId, CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<Guid>();
        Guid? current = possibleDescendantId;
        while (current.HasValue && visited.Add(current.Value))
        {
            if (current.Value == categoryId) return true;
            current = await context.Categories.AsNoTracking()
                .Where(x => x.Id == current.Value)
                .Select(x => x.ParentCategoryId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        return false;
    }

    public async Task<bool> HasChildrenOrJobsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Categories.AnyAsync(x => x.ParentCategoryId == id, cancellationToken) ||
        await context.Jobs.AnyAsync(x => x.CategoryId == id, cancellationToken);

    public Task AddAsync(Category category, CancellationToken cancellationToken = default) =>
        context.Categories.AddAsync(category, cancellationToken).AsTask();

    public void Remove(Category category) => context.Categories.Remove(category);

    public async Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default) =>
        await context.Categories.AsNoTracking()
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new AdminOptionResponse(x.Id, x.Name, x.Slug)).ToArrayAsync(cancellationToken);

    private static IQueryable<CategoryResponse> Project(IQueryable<Category> source) =>
        source.Select(x => new CategoryResponse(x.Id, x.Name, x.Slug, x.Description, x.DisplayOrder,
            x.ParentCategoryId, x.ParentCategory == null ? null : x.ParentCategory.Name,
            x.CreatedAtUtc, x.UpdatedAtUtc, x.IsDeleted));

    private static IQueryable<Category> Sort(IQueryable<Category> source, string field, bool descending) =>
        (field.ToLowerInvariant(), descending) switch
        {
            ("name", false) => source.OrderBy(x => x.Name).ThenBy(x => x.Id),
            ("name", true) => source.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            ("displayorder", false) => source.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ThenBy(x => x.Id),
            ("displayorder", true) => source.OrderByDescending(x => x.DisplayOrder).ThenBy(x => x.Name).ThenBy(x => x.Id),
            ("updatedat", false) => source.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            ("updatedat", true) => source.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            (_, false) => source.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            _ => source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
        };
}
