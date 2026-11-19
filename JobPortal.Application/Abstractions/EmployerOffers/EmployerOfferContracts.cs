using JobPortal.Application.Features.EmployerOffers;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.EmployerOffers;

public interface IEmployerOfferService
{
    Task<PagedResponse<EmployerOfferResponse>> SearchAsync(
        Guid employerUserId,
        EmployerOfferQuery query,
        CancellationToken cancellationToken = default);

    Task<EmployerOfferResponse> GetAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken = default);

    Task<EmployerOfferResponse> GetForApplicationAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<EmployerOfferResponse> CreateAsync(
        Guid employerUserId,
        Guid applicationId,
        CreateEmployerOfferRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerOfferResponse> UpdateAsync(
        Guid employerUserId,
        Guid offerId,
        UpdateEmployerOfferRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerOfferResponse> SendAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken = default);

    Task<EmployerOfferResponse> WithdrawAsync(
        Guid employerUserId,
        Guid offerId,
        WithdrawEmployerOfferRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerOfferResponse> AcceptByCandidateAsync(
    Guid candidateUserId,
    Guid offerId,
    CancellationToken cancellationToken = default);

    Task<EmployerOfferResponse> DeclineByCandidateAsync(
        Guid candidateUserId,
        Guid offerId,
        DeclineEmployerOfferRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerOfferResponse> HireAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken = default);
}
