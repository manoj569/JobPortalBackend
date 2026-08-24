using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.CandidateCompanies;

public interface ICandidateCompanyService
{
    Task<IReadOnlyCollection<CompanyOption>> SearchAsync(Guid candidateId, string query, int limit = 10, CancellationToken ct = default);
    Task<CreateCandidateCompanyResponse> CreateAsync(Guid candidateId, CreateCandidateCompanyRequest request, CancellationToken ct = default);
}

public interface ICandidateCompanyRepository
{
    Task<bool> IsCandidateAsync(Guid candidateId, CancellationToken ct);
    Task<IReadOnlyCollection<CompanyOption>> SearchAsync(string normalizedQuery, int limit, CancellationToken ct);
    Task<Company?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct);
    Task<int> CountCreatedSinceAsync(Guid candidateId, DateTime since, CancellationToken ct);
    Task<Guid?> FindActiveAdministratorIdAsync(CancellationToken ct);
    Task AddAsync(Company company, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
    void DiscardPendingChanges();
}
