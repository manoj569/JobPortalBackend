using JobPortal.Application.Features.EmployerTalentPool;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.EmployerTalentPool;

public interface IEmployerTalentPoolService
{
    Task<EmployerTalentPoolResponse> GetAsync(
        Guid employerUserId,
        EmployerTalentPoolQuery query,
        CancellationToken cancellationToken = default);

    Task<EmployerTalentPoolItemResponse> AddAsync(
        Guid employerUserId,
        AddEmployerTalentPoolCandidateRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerTalentPoolItemResponse> UpdateNotesAsync(
        Guid employerUserId,
        Guid candidateUserId,
        UpdateEmployerTalentPoolNotesRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid employerUserId,
        Guid candidateUserId,
        CancellationToken cancellationToken = default);
}

public interface IEmployerTalentPoolRepository
{
    Task<(IReadOnlyCollection<EmployerTalentPoolCandidate> Items, int TotalCount)>
        SearchAsync(
            Guid employerUserId,
            EmployerTalentPoolQuery query,
            CancellationToken cancellationToken = default);

    Task<User?> GetVisibleCandidateAsync(
        Guid candidateUserId,
        CancellationToken cancellationToken = default);

    Task<Domain.Entities.EmployerTalentPool?> GetAsync(
        Guid employerUserId,
        Guid candidateUserId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Domain.Entities.EmployerTalentPool entry,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}

public sealed record EmployerTalentPoolCandidate(
    Guid CandidateUserId,
    string FirstName,
    string LastName,
    string? Headline,
    string? CurrentCity,
    string? Location,
    decimal? YearsOfExperience,
    string? ProfileImageUrl,
    string? Notes,
    DateTime AddedAtUtc,
    IReadOnlyCollection<string> Skills);
