using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.EmployerOffers;

public sealed record EmployerOfferResponse(
    Guid Id,
    Guid ApplicationId,
    Guid PipelineId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid JobId,
    string JobTitle,
    EmployerOfferStatus Status,
    decimal? AnnualSalary,
    string CurrencyCode,
    DateTime? JoiningDateUtc,
    DateTime? ExpiresAtUtc,
    string? WorkLocation,
    string? Notes,
    string? OfferLetterUrl,
    DateTime? SentAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? DeclinedAtUtc,
    DateTime? WithdrawnAtUtc,
    DateTime? ExpiredAtUtc,
    DateTime? HiredAtUtc);

public sealed record EmployerOfferQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? JobId = null,
    EmployerOfferStatus? Status = null);

public sealed record CreateEmployerOfferRequest(
    string JobTitle,
    decimal? AnnualSalary,
    string CurrencyCode,
    DateTime? JoiningDateUtc,
    DateTime? ExpiresAtUtc,
    string? WorkLocation,
    string? Notes,
    string? OfferLetterUrl);

public sealed record UpdateEmployerOfferRequest(
    string JobTitle,
    decimal? AnnualSalary,
    string CurrencyCode,
    DateTime? JoiningDateUtc,
    DateTime? ExpiresAtUtc,
    string? WorkLocation,
    string? Notes,
    string? OfferLetterUrl);

public sealed record WithdrawEmployerOfferRequest(
    string? Reason);

public sealed record DeclineEmployerOfferRequest(
    string? Reason);
