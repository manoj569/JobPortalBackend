using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public sealed record ExistingJobImportIdentity(
    Guid CompanyId,
    string Title,
    string ApplicationUrl);

public interface IAdminImportRepository
{
    Task<IReadOnlyCollection<Company>> FindCompaniesAsync(
        IReadOnlyCollection<string> slugs,
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Category>> FindCategoriesAsync(
        IReadOnlyCollection<string> slugs,
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ExistingJobImportIdentity>> FindJobIdentitiesAsync(
        IReadOnlyCollection<Guid> companyIds,
        CancellationToken cancellationToken = default);

    Task AddCompaniesAsync(
        IReadOnlyCollection<Company> companies,
        CancellationToken cancellationToken = default);

    void UpdateCompany(Company company);

    Task AddJobsAsync(
        IReadOnlyCollection<Job> jobs,
        CancellationToken cancellationToken = default);
}
