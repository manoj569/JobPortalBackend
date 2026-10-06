using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.AdminApplications;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.AdminApplications;

public sealed class AdminApplicationService(
    IAdminApplicationRepository applications,
    IUserRepository users,
    IResumeStorage resumeStorage,
    IEmailService emailService,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<AdminApplicationQuery> queryValidator,
    IValidator<UpdateAdminApplicationStatusRequest> updateValidator,
    TimeProvider timeProvider) : IAdminApplicationService
{
    private static readonly Dictionary<string, string> ResumeContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] =
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

    public async Task<PagedResponse<AdminApplicationListItem>> SearchAsync(
        AdminApplicationQuery query, CancellationToken cancellationToken = default)
    {
        await queryValidator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await applications.SearchAsync(query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task<AdminApplicationDetail> GetAsync(
        Guid applicationId, CancellationToken cancellationToken = default) =>
        Map(await RequiredApplicationAsync(applicationId, cancellationToken));

    public async Task<AdminApplicationResumeDownload> DownloadResumeAsync(
        Guid applicationId, CancellationToken cancellationToken = default)
    {
        var application = await RequiredApplicationAsync(applicationId, cancellationToken);
        if (application.ResumeStorageKey is null || application.ResumeFileName is null)
            throw new NotFoundException("Application resume was not found.");
        var extension = Path.GetExtension(application.ResumeFileName);
        if (!ResumeContentTypes.TryGetValue(extension, out var contentType))
            throw new NotFoundException("Application resume was not found.");
        var content = await resumeStorage.OpenReadAsync(
            application.UserId, application.ResumeStorageKey, originalFileName: application.ResumeFileName,
            contentType: contentType, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("Application resume was not found.");
        return new(content, $"resume{extension.ToLowerInvariant()}", contentType);
    }

    public async Task<AdminApplicationDetail> UpdateStatusAsync(
        Guid administratorUserId, Guid applicationId,
        UpdateAdminApplicationStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var administrator = await RequiredAdministratorAsync(
            administratorUserId, cancellationToken);
        var application = await RequiredApplicationAsync(applicationId, cancellationToken);
        EnsureTransition(application.Status, request.Status);

        var previousStatus = application.Status;
        application.Status = request.Status;
        application.StatusHistory.Add(new JobApplicationStatusHistory
        {
            ApplicationId = application.Id,
            ActorUserId = administratorUserId,
            ActorUser = administrator,
            PreviousStatus = previousStatus,
            NewStatus = request.Status,
            ChangedAtUtc = UtcNow,
            InternalNote = TextNormalizer.TrimOrNull(request.InternalNote)
        });
        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "JobApplication",
            application.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["previousStatus"] = previousStatus.ToString(),
                ["newStatus"] = request.Status.ToString()
            },
            new(administratorUserId, "Administrator")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.Status is JobApplicationStatus.Shortlisted or JobApplicationStatus.Rejected)
        {
            _ = await emailService.SendApplicationStatusAsync(
                application.User,
                application.Job.Title,
                request.Status,
                cancellationToken);
        }
        return Map(application);
    }

    private async Task<JobApplication> RequiredApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken) =>
        await applications.GetByIdAsync(applicationId, cancellationToken)
        ?? throw new NotFoundException("Application was not found.");

    private async Task<User> RequiredAdministratorAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var administrator = await users.GetByIdWithRoleAsync(userId, cancellationToken);
        if (administrator is null ||
            administrator.RoleId != SystemRoleIds.Administrator ||
            administrator.Status != UserStatus.Active)
            throw new UnauthorizedException("An active Administrator account is required.");
        return administrator;
    }

    private static void EnsureTransition(
        JobApplicationStatus current, JobApplicationStatus requested)
    {
        var allowed = current switch
        {
            JobApplicationStatus.Submitted =>
                requested is JobApplicationStatus.Reviewed or
                    JobApplicationStatus.Shortlisted or JobApplicationStatus.Rejected,
            JobApplicationStatus.Reviewed =>
                requested is JobApplicationStatus.Shortlisted or JobApplicationStatus.Rejected,
            _ => false
        };
        if (!allowed)
            throw new ConflictException(
                $"Application cannot transition from {current} to {requested}.");
    }

    private static AdminApplicationDetail Map(JobApplication application) => new(
        application.Id,
        application.Status,
        application.SubmittedAtUtc,
        application.WithdrawnAtUtc,
        application.CoverLetter,
        application.ResumeStorageKey is not null,
        application.ResumeStorageKey is null ? null : SafeResumeFileName(application.ResumeFileName),
        new AdminCandidateProfile(
            application.User.Id,
            application.User.Email,
            application.User.FirstName,
            application.User.LastName,
            application.User.Headline,
            application.User.Bio,
            application.User.Location,
            Deserialize<string>(application.User.SkillsJson),
            Deserialize<string>(application.User.EducationJson),
            Deserialize<string>(application.User.ExperienceJson),
            application.User.LinkedInUrl,
            application.User.PortfolioUrl,
            Deserialize<EmploymentType>(application.User.PreferredJobTypesJson)),
        new AdminApplicationJobSummary(
            application.Job.Id,
            application.Job.Title,
            application.Job.Slug,
            application.Job.Location,
            application.Job.EmploymentType,
            application.Job.WorkplaceType,
            application.Job.CompanyId,
            application.Job.Company.Name,
            application.Job.CategoryId,
            application.Job.Category.Name),
        application.StatusHistory
            .OrderBy(x => x.ChangedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => new AdminApplicationStatusHistoryResponse(
                x.Id,
                x.PreviousStatus,
                x.NewStatus,
                x.ChangedAtUtc,
                x.ActorUserId,
                $"{x.ActorUser.FirstName} {x.ActorUser.LastName}".Trim(),
                x.InternalNote))
            .ToArray());

    private static string? SafeResumeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var extension = Path.GetExtension(fileName);
        return ResumeContentTypes.ContainsKey(extension)
            ? $"resume{extension.ToLowerInvariant()}"
            : null;
    }

    private static T[] Deserialize<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<T[]>(json) ?? [];

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
