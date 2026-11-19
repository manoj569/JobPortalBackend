using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IEmployerOfferRepository
{
    Task<EmployerHiringPipeline?> GetOwnedPipelineAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<EmployerOffer?> GetOwnedOfferAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken = default);

    Task<EmployerOffer?> GetOwnedOfferForApplicationAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyCollection<EmployerOffer> Items, int TotalCount)>
        SearchAsync(
            Guid employerUserId,
            Guid? jobId,
            EmployerOfferStatus? status,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default);

    Task AddAsync(
        EmployerOffer offer,
        CancellationToken cancellationToken = default);

    Task<EmployerOffer?> GetCandidateOfferAsync(
    Guid candidateUserId,
    Guid offerId,
    CancellationToken cancellationToken = default);

    Task<int> ExpireOverdueAsync(
    DateTime nowUtc,
    CancellationToken cancellationToken = default);
}
