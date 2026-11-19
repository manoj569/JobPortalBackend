using FluentValidation;
using JobPortal.Application.Abstractions.AdminApplications;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.EmployerApplications;
using JobPortal.Application.Abstractions.EmployerCompanies;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.AdminApplications;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.EmployerApplications;

public sealed class EmployerApplicationService(
    IEmployerApplicationRepository employerApplications,
    IEmployerCompanyRepository employerCompanies,
    IAdminApplicationService adminApplications,
    IUserRepository users,
    IEmailService emailService,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<EmployerApplicationQuery> queryValidator,
    IValidator<UpdateEmployerApplicationStatusRequest> updateValidator,
    TimeProvider timeProvider)
    : IEmployerApplicationService
{
    public async Task<PagedResponse<AdminApplicationListItem>> SearchAsync(
        Guid employerUserId,
        EmployerApplicationQuery query,
        CancellationToken cancellationToken = default)
    {
        await queryValidator.ValidateAndThrowAsync(
            query,
            cancellationToken);

        var company =
            await employerCompanies.GetByOwnerUserIdAsync(
                employerUserId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Employer company was not found.");

        /*
         * Reuse the existing Admin search implementation,
         * but CompanyId is forced server-side.
         *
         * Employer cannot choose another CompanyId.
         */
        var adminQuery = new AdminApplicationQuery(
            PageNumber: query.PageNumber,
            PageSize: query.PageSize,
            JobId: query.JobId,
            CompanyId: company.Id,
            CategoryId: query.CategoryId,
            Status: query.Status,
            SubmittedFromUtc: query.SubmittedFromUtc,
            SubmittedToUtc: query.SubmittedToUtc,
            Keyword: query.Keyword);

        return await adminApplications.SearchAsync(
            adminQuery,
            cancellationToken);
    }

    public async Task<AdminApplicationDetail> GetAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        await RequiredOwnedApplicationAsync(
            employerUserId,
            applicationId,
            cancellationToken);

        /*
         * Ownership has already been established.
         * Reuse existing secure response mapping.
         */
        return await adminApplications.GetAsync(
            applicationId,
            cancellationToken);
    }

    public async Task<AdminApplicationResumeDownload> DownloadResumeAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        await RequiredOwnedApplicationAsync(
            employerUserId,
            applicationId,
            cancellationToken);

        /*
         * Existing implementation already protects:
         *
         * - opaque storage key
         * - allowed PDF/DOC/DOCX types
         * - safe download filename
         */
        return await adminApplications.DownloadResumeAsync(
            applicationId,
            cancellationToken);
    }

    public async Task<AdminApplicationDetail> UpdateStatusAsync(
        Guid employerUserId,
        Guid applicationId,
        UpdateEmployerApplicationStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        var employer =
            await RequiredEmployerAsync(
                employerUserId,
                cancellationToken);

        var application =
            await RequiredOwnedApplicationAsync(
                employerUserId,
                applicationId,
                cancellationToken);

        EnsureTransition(
            application.Status,
            request.Status);

        var previousStatus =
            application.Status;

        application.Status =
            request.Status;

        application.StatusHistory.Add(
            new JobApplicationStatusHistory
            {
                ApplicationId =
                    application.Id,

                Application =
                    application,

                ActorUserId =
                    employerUserId,

                ActorUser =
                    employer,

                PreviousStatus =
                    previousStatus,

                NewStatus =
                    request.Status,

                ChangedAtUtc =
                    UtcNow,

                InternalNote =
                    TextNormalizer.TrimOrNull(
                        request.InternalNote)
            });

        await auditWriter.AppendAsync(
            new(
                AuditAction.Update,
                "JobApplication",
                application.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["previousStatus"] =
                        previousStatus.ToString(),

                    ["newStatus"] =
                        request.Status.ToString(),

                    ["jobId"] =
                        application.JobId.ToString(),

                    ["companyId"] =
                        application.Job.CompanyId.ToString(),

                    ["source"] =
                        "employerApplicationPipeline"
                },
                new(
                    employerUserId,
                    "Employer")),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        /*
         * Keep the existing CareerHarbor behaviour:
         * candidate receives email on important outcome states.
         */
        if (request.Status is
            JobApplicationStatus.Shortlisted or
            JobApplicationStatus.Rejected)
        {
            _ = await emailService.SendApplicationStatusAsync(
                application.User,
                application.Job.Title,
                request.Status,
                cancellationToken);
        }

        /*
         * Reload through the existing mapper after saving.
         */
        return await adminApplications.GetAsync(
            applicationId,
            cancellationToken);
    }

    private async Task<JobApplication> RequiredOwnedApplicationAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var application =
            await employerApplications.GetOwnedApplicationAsync(
                employerUserId,
                applicationId,
                cancellationToken);

        if (application is null)
        {
            /*
             * Don't reveal that another employer owns it.
             */
            throw new NotFoundException(
                "Application was not found.");
        }

        return application;
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
            employer.RoleId != SystemRoleIds.Employer ||
            employer.Status != UserStatus.Active)
        {
            throw new UnauthorizedException(
                "An active Employer account is required.");
        }

        return employer;
    }

    private static void EnsureTransition(
        JobApplicationStatus current,
        JobApplicationStatus requested)
    {
        var allowed =
            current switch
            {
                JobApplicationStatus.Submitted =>
                    requested is
                        JobApplicationStatus.Reviewed or
                        JobApplicationStatus.Shortlisted or
                        JobApplicationStatus.Rejected,

                JobApplicationStatus.Reviewed =>
                    requested is
                        JobApplicationStatus.Shortlisted or
                        JobApplicationStatus.Rejected,

                _ => false
            };

        if (!allowed)
        {
            throw new ConflictException(
                $"Application cannot transition from {current} to {requested}.");
        }
    }

    private DateTime UtcNow =>
        timeProvider
            .GetUtcNow()
            .UtcDateTime;
}
