using JobPortal.Application.Features.EmployerCompanies;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.EmployerCompanies;

public interface IEmployerCompanyService
{
    Task<EmployerCompanyResponse?> GetMineAsync(
        Guid employerUserId,
        CancellationToken cancellationToken = default);

    Task<EmployerCompanyResponse> SetupAsync(
        Guid employerUserId,
        EmployerCompanySetupRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerCompanyResponse> UpdateAsync(
        Guid employerUserId,
        EmployerCompanyUpdateRequest request,
        CancellationToken cancellationToken = default);
}

public interface IEmployerCompanyRepository
{
    Task<bool> IsEmployerAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Company?> GetByOwnerUserIdAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    Task<Company?> FindByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken = default);

    Task<Company?> FindByWebsiteDomainAsync(
        string normalizedDomain,
        CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(
        string slug,
        Guid? excludingCompanyId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Company company,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        CancellationToken cancellationToken = default);
}
