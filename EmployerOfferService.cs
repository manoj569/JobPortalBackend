using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.EmployerOffers;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.EmployerOffers;

public sealed class EmployerOfferService(
    IEmployerOfferRepository offers,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<EmployerOfferQuery> queryValidator,
    IValidator<CreateEmployerOfferRequest> createValidator,
    IValidator<UpdateEmployerOfferRequest> updateValidator,
    IValidator<WithdrawEmployerOfferRequest> withdrawValidator,
   IValidator<DeclineEmployerOfferRequest> declineValidator,
NotificationOutbox notificationOutbox,
TimeProvider timeProvider)
    : IEmployerOfferService
{
    public async Task<PagedResponse<EmployerOfferResponse>>
        SearchAsync(
            Guid employerUserId,
            EmployerOfferQuery query,
            CancellationToken cancellationToken = default)
    {
        await queryValidator.ValidateAndThrowAsync(
            query,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var result =
            await offers.SearchAsync(
                employerUserId,
                query.JobId,
                query.Status,
                query.PageNumber,
                query.PageSize,
                cancellationToken);

        return new(
            result.Items.Select(Map).ToArray(),
            query.PageNumber,
            query.PageSize,
            result.TotalCount);
    }

    public async Task<EmployerOfferResponse> GetAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        return Map(
            await RequiredOfferAsync(
                employerUserId,
                offerId,
                cancellationToken));
    }

    public async Task<EmployerOfferResponse>
        GetForApplicationAsync(
            Guid employerUserId,
            Guid applicationId,
            CancellationToken cancellationToken = default)
    {
        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var offer =
            await offers
                .GetOwnedOfferForApplicationAsync(
                    employerUserId,
                    applicationId,
                    cancellationToken)
            ?? throw new NotFoundException(
                "Offer was not found.");

        return Map(offer);
    }

    public async Task<EmployerOfferResponse> CreateAsync(
        Guid employerUserId,
        Guid applicationId,
        CreateEmployerOfferRequest request,
        CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        var employer =
            await RequiredEmployerAsync(
                employerUserId,
                cancellationToken);

        var pipeline =
            await offers.GetOwnedPipelineAsync(
                employerUserId,
                applicationId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Hiring pipeline was not found.");

        if (pipeline.Stage !=
            EmployerPipelineStage.Offer)
        {
            throw new ConflictException(
                "Application must be in Offer stage before creating an offer.");
        }

        var existing =
            await offers
                .GetOwnedOfferForApplicationAsync(
                    employerUserId,
                    applicationId,
                    cancellationToken);

        if (existing is not null)
        {
            throw new ConflictException(
                "An offer already exists for this application.");
        }

        ValidateDates(
            request.JoiningDateUtc,
            request.ExpiresAtUtc);

        var offer =
            new EmployerOffer
            {
                JobApplicationId =
                    applicationId,

                JobApplication =
                    pipeline.JobApplication,

                PipelineId =
                    pipeline.Id,

                Pipeline =
                    pipeline,

                Status =
                    EmployerOfferStatus.Draft,

                JobTitle =
                    request.JobTitle.Trim(),

                AnnualSalary =
                    request.AnnualSalary,

                CurrencyCode =
                    request.CurrencyCode
                        .Trim()
                        .ToUpperInvariant(),

                JoiningDateUtc =
                    request.JoiningDateUtc,

                ExpiresAtUtc =
                    request.ExpiresAtUtc,

                WorkLocation =
                    TextNormalizer.TrimOrNull(
                        request.WorkLocation),

                Notes =
                    TextNormalizer.TrimOrNull(
                        request.Notes),

                OfferLetterUrl =
                    TextNormalizer.TrimOrNull(
                        request.OfferLetterUrl),

                CreatedByUserId =
                    employerUserId,

                CreatedByUser =
                    employer
            };

        await offers.AddAsync(
            offer,
            cancellationToken);

        await WriteAuditAsync(
            employerUserId,
            offer,
            AuditAction.Create,
            "offerCreated",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(offer);
    }

    public async Task<EmployerOfferResponse> UpdateAsync(
        Guid employerUserId,
        Guid offerId,
        UpdateEmployerOfferRequest request,
        CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var offer =
            await RequiredOfferAsync(
                employerUserId,
                offerId,
                cancellationToken);

        if (offer.Status !=
            EmployerOfferStatus.Draft)
        {
            throw new ConflictException(
                "Only a Draft offer can be edited.");
        }

        ValidateDates(
            request.JoiningDateUtc,
            request.ExpiresAtUtc);

        offer.JobTitle =
            request.JobTitle.Trim();

        offer.AnnualSalary =
            request.AnnualSalary;

        offer.CurrencyCode =
            request.CurrencyCode
                .Trim()
                .ToUpperInvariant();

        offer.JoiningDateUtc =
            request.JoiningDateUtc;

        offer.ExpiresAtUtc =
            request.ExpiresAtUtc;

        offer.WorkLocation =
            TextNormalizer.TrimOrNull(
                request.WorkLocation);

        offer.Notes =
            TextNormalizer.TrimOrNull(
                request.Notes);

        offer.OfferLetterUrl =
            TextNormalizer.TrimOrNull(
                request.OfferLetterUrl);

        await WriteAuditAsync(
            employerUserId,
            offer,
            AuditAction.Update,
            "offerUpdated",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(offer);
    }

    public async Task<EmployerOfferResponse> SendAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var offer =
            await RequiredOfferAsync(
                employerUserId,
                offerId,
                cancellationToken);

        if (offer.Status !=
            EmployerOfferStatus.Draft)
        {
            throw new ConflictException(
                "Only a Draft offer can be sent.");
        }

        if (offer.ExpiresAtUtc.HasValue &&
            offer.ExpiresAtUtc.Value <= UtcNow)
        {
            throw new ConflictException(
                "The offer expiry date has already passed.");
        }

        offer.Status =
            EmployerOfferStatus.Sent;

        offer.SentAtUtc =
            UtcNow;

        /*
         * Keep the pipeline in Offer stage.
         * Candidate acceptance happens later.
         */

        await WriteAuditAsync(
            employerUserId,
            offer,
            AuditAction.Submit,
            "offerSent",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(offer);
    }

    public async Task<EmployerOfferResponse> WithdrawAsync(
        Guid employerUserId,
        Guid offerId,
        WithdrawEmployerOfferRequest request,
        CancellationToken cancellationToken = default)
    {
        await withdrawValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var offer =
            await RequiredOfferAsync(
                employerUserId,
                offerId,
                cancellationToken);

        if (offer.Status is
            EmployerOfferStatus.Accepted or
            EmployerOfferStatus.Declined or
            EmployerOfferStatus.Withdrawn or
            EmployerOfferStatus.Expired)
        {
            throw new ConflictException(
                $"Offer cannot be withdrawn from {offer.Status}.");
        }

        offer.Status =
            EmployerOfferStatus.Withdrawn;

        offer.WithdrawnAtUtc =
            UtcNow;

        if (!string.IsNullOrWhiteSpace(
                request.Reason))
        {
            offer.Notes =
                TextNormalizer.TrimOrNull(
                    request.Reason);
        }

        await WriteAuditAsync(
            employerUserId,
            offer,
            AuditAction.Withdraw,
            "offerWithdrawn",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(offer);
    }

    public async Task<EmployerOfferResponse> MarkAcceptedAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var offer =
            await RequiredOfferAsync(
                employerUserId,
                offerId,
                cancellationToken);

        EnsureSentAndNotExpired(
            offer);

        offer.Status =
            EmployerOfferStatus.Accepted;

        offer.AcceptedAtUtc =
            UtcNow;

        await WriteAuditAsync(
            employerUserId,
            offer,
            AuditAction.Confirm,
            "offerAccepted",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(offer);
    }

    public async Task<EmployerOfferResponse> MarkDeclinedAsync(
        Guid employerUserId,
        Guid offerId,
        DeclineEmployerOfferRequest request,
        CancellationToken cancellationToken = default)
    {
        await declineValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var offer =
            await RequiredOfferAsync(
                employerUserId,
                offerId,
                cancellationToken);

        EnsureSentAndNotExpired(
            offer);

        offer.Status =
            EmployerOfferStatus.Declined;

        offer.DeclinedAtUtc =
            UtcNow;

        if (!string.IsNullOrWhiteSpace(
                request.Reason))
        {
            offer.Notes =
                TextNormalizer.TrimOrNull(
                    request.Reason);
        }

        await WriteAuditAsync(
            employerUserId,
            offer,
            AuditAction.Update,
            "offerDeclined",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(offer);
    }

    public async Task<EmployerOfferResponse> HireAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var offer =
            await RequiredOfferAsync(
                employerUserId,
                offerId,
                cancellationToken);

        if (offer.Status !=
            EmployerOfferStatus.Accepted)
        {
            throw new ConflictException(
                "Only an Accepted offer can be marked as Hired.");
        }

        if (offer.Pipeline.Stage !=
            EmployerPipelineStage.Offer)
        {
            throw new ConflictException(
                "Application is not currently in Offer stage.");
        }

        offer.Pipeline.Stage =
            EmployerPipelineStage.Hired;

        offer.Pipeline.HiredAtUtc =
            UtcNow;

        await WriteAuditAsync(
            employerUserId,
            offer,
            AuditAction.Update,
            "candidateHired",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(offer);
    }

    private async Task<User> RequiredEmployerAsync(
        Guid employerUserId,
        CancellationToken cancellationToken)
    {
        var employer =
            await users.GetByIdWithRoleAsync(
                employerUserId,
                cancellationToken);

        if (employer is null ||
            employer.RoleId !=
            SystemRoleIds.Employer ||
            employer.Status !=
            UserStatus.Active)
        {
            throw new UnauthorizedException(
                "An active Employer account is required.");
        }

        return employer;
    }

    private async Task<EmployerOffer> RequiredOfferAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken)
    {
        return await offers.GetOwnedOfferAsync(
                   employerUserId,
                   offerId,
                   cancellationToken)
               ?? throw new NotFoundException(
                   "Offer was not found.");
    }

    private void EnsureSentAndNotExpired(
        EmployerOffer offer)
    {
        if (offer.Status !=
            EmployerOfferStatus.Sent)
        {
            throw new ConflictException(
                "Only a Sent offer can receive a candidate decision.");
        }

        if (offer.ExpiresAtUtc.HasValue &&
            offer.ExpiresAtUtc.Value <= UtcNow)
        {
            offer.Status =
                EmployerOfferStatus.Expired;

            offer.ExpiredAtUtc =
                UtcNow;

            throw new ConflictException(
                "The offer has expired.");
        }
    }

    private void ValidateDates(
        DateTime? joiningDateUtc,
        DateTime? expiresAtUtc)
    {
        if (joiningDateUtc.HasValue &&
            joiningDateUtc.Value.Kind !=
            DateTimeKind.Utc)
        {
            throw new BadRequestException(
                "JoiningDateUtc must be UTC.");
        }

        if (expiresAtUtc.HasValue &&
            expiresAtUtc.Value.Kind !=
            DateTimeKind.Utc)
        {
            throw new BadRequestException(
                "ExpiresAtUtc must be UTC.");
        }

        if (expiresAtUtc.HasValue &&
            expiresAtUtc.Value <= UtcNow)
        {
            throw new BadRequestException(
                "ExpiresAtUtc must be in the future.");
        }

        if (joiningDateUtc.HasValue &&
            expiresAtUtc.HasValue &&
            joiningDateUtc.Value <=
            expiresAtUtc.Value)
        {
            throw new BadRequestException(
                "JoiningDateUtc must be after ExpiresAtUtc.");
        }
    }

    private async Task WriteAuditAsync(
        Guid employerUserId,
        EmployerOffer offer,
        AuditAction action,
        string operation,
        CancellationToken cancellationToken)
    {
        await auditWriter.AppendAsync(
            new(
                action,
                "EmployerOffer",
                offer.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["applicationId"] =
                        offer.JobApplicationId
                            .ToString(),

                    ["pipelineId"] =
                        offer.PipelineId
                            .ToString(),

                    ["status"] =
                        offer.Status
                            .ToString(),

                    ["operation"] =
                        operation
                },
                new(
                    employerUserId,
                    "Employer")),
            cancellationToken);
    }

    private static EmployerOfferResponse Map(
        EmployerOffer offer)
    {
        var application =
            offer.JobApplication;

        var candidate =
            application.User;

        var candidateName =
            string.Join(
                " ",
                new[]
                {
                    candidate.FirstName,
                    candidate.LastName
                }
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x)));

        return new(
            offer.Id,
            application.Id,
            offer.PipelineId,
            candidate.Id,
            candidateName,
            candidate.Email,
            application.JobId,
            offer.JobTitle,
            offer.Status,
            offer.AnnualSalary,
            offer.CurrencyCode,
            offer.JoiningDateUtc,
            offer.ExpiresAtUtc,
            offer.WorkLocation,
            offer.Notes,
            offer.OfferLetterUrl,
            offer.SentAtUtc,
            offer.AcceptedAtUtc,
            offer.DeclinedAtUtc,
            offer.WithdrawnAtUtc,
            offer.ExpiredAtUtc,
            offer.Pipeline.HiredAtUtc);
    }

    private DateTime UtcNow =>
        timeProvider
            .GetUtcNow()
            .UtcDateTime;
}
