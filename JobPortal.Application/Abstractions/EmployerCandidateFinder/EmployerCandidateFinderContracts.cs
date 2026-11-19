using JobPortal.Application.Features.EmployerCandidateFinder;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.EmployerCandidateFinder;

public interface IEmployerCandidateFinderService
{
    Task<EmployerCandidateFinderResponse> SearchAsync(
        Guid employerUserId,
        Guid jobId,
        EmployerCandidateFinderQuery query,
        CancellationToken cancellationToken = default);
}

public interface IEmployerCandidateFinderRepository
{
    Task<Job?> GetOwnedJobAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<EmployerCandidateFinderCandidate>>
        GetCandidatesAsync(
            EmployerCandidateFinderQuery query,
            CancellationToken cancellationToken = default);
}

public sealed record EmployerCandidateFinderCandidate(
    Guid UserId,
    string FirstName,
    string LastName,
    string? Headline,
    string? CurrentCity,
    string? Location,
    decimal? YearsOfExperience,
    string PreferredJobRolesJson,
    string PreferredCitiesJson,
    IReadOnlyCollection<EmployerCandidateFinderSkill> Skills);

public sealed record EmployerCandidateFinderSkill(
    string Name,
    string NormalizedName,
    decimal? YearsOfExperience);
