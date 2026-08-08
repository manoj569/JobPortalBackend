using JobPortal.Application.Features.AdminManagement;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface ICompanyManagementRepository
{
    Task<(IReadOnlyCollection<CompanyResponse> Items, int TotalCount)> SearchAsync(CompanySearchQuery query, CancellationToken cancellationToken = default);
    Task<Company?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Company?> FindByNameOrSlugAsync(string normalizedName, string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult<Company?>(null);
    Task<CompanyResponse?> GetResponseAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludingId = null, CancellationToken cancellationToken = default);
    Task<bool> HasJobsAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Company company, CancellationToken cancellationToken = default);
    void Remove(Company company);
    Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default);
}

public interface ICategoryManagementRepository
{
    Task<(IReadOnlyCollection<CategoryResponse> Items, int TotalCount)> SearchAsync(CategorySearchQuery query, CancellationToken cancellationToken = default);
    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Category?> FindByNameOrSlugAsync(string name, string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult<Category?>(null);
    Task<CategoryResponse?> GetResponseAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludingId = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> IsDescendantAsync(Guid categoryId, Guid possibleDescendantId, CancellationToken cancellationToken = default);
    Task<bool> HasChildrenOrJobsAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Category category, CancellationToken cancellationToken = default);
    void Remove(Category category);
    Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default);
}
