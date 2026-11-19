using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.EmployerInterviews;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.EmployerInterviews;

public sealed class EmployerInterviewService(
    IEmployerInterviewRepository interviews,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<EmployerInterviewQuery> queryValidator,
    IValidator<CreateEmployerInterviewRequest> createValidator,
    IValidator<UpdateEmployerInterviewRequest> updateValidator,
    IValidator<RescheduleEmployerInterviewRequest> rescheduleValidator,
    IValidator<CancelEmployerInterviewRequest> cancelValidator,
    IValidator<CompleteEmployerInterviewRequest> completeValidator,
NotificationOutbox notificationOutbox,
TimeProvider timeProvider)
    : IEmployerInterviewService
{
    public async Task<PagedResponse<EmployerInterviewResponse>>
        SearchAsync(
            Guid employerUserId,
            EmployerInterviewQuery query,
            CancellationToken cancellationToken = default)
    {
        await queryValidator.ValidateAndThrowAsync(
            query,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var result =
            await interviews.SearchAsync(
                employerUserId,
                query.JobId,
                query.Status,
                query.Result,
                query.RoundType,
                query.PageNumber,
                query.PageSize,
                cancellationToken);

        return new(
            result.Items.Select(Map).ToArray(),
            query.PageNumber,
            query.PageSize,
            result.TotalCount);
    }

    public async Task<EmployerInterviewResponse> GetAsync(
        Guid employerUserId,
        Guid interviewId,
        CancellationToken cancellationToken = default)
    {
        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        return Map(
            await RequiredInterviewAsync(
                employerUserId,
                interviewId,
                cancellationToken));
    }

    public async Task<IReadOnlyCollection<EmployerInterviewResponse>>
        GetForApplicationAsync(
            Guid employerUserId,
            Guid applicationId,
            CancellationToken cancellationToken = default)
    {
        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var pipeline =
            await interviews.GetOwnedPipelineAsync(
                employerUserId,
                applicationId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Hiring pipeline was not found.");

        return (await interviews.GetForApplicationAsync(
                employerUserId,
                pipeline.JobApplicationId,
                cancellationToken))
            .Select(Map)
            .ToArray();
    }

    public async Task<EmployerInterviewResponse> CreateAsync(
        Guid employerUserId,
        Guid applicationId,
        CreateEmployerInterviewRequest request,
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
            await interviews.GetOwnedPipelineAsync(
                employerUserId,
                applicationId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Hiring pipeline was not found.");

        if (pipeline.Stage !=
            EmployerPipelineStage.Interview)
        {
            throw new ConflictException(
                "Application must be in Interview stage.");
        }

        if (pipeline.ScreeningStatus !=
            EmployerScreeningStatus.Passed)
        {
            throw new ConflictException(
                "Screening must be passed before scheduling an interview.");
        }

        var scheduledAtUtc =
            RequireFutureUtc(
                request.ScheduledAtUtc);

        var roundNumber =
            await interviews.GetNextRoundNumberAsync(
                applicationId,
                cancellationToken);

        var interview =
            new EmployerInterview
            {
                JobApplicationId =
                    applicationId,

                JobApplication =
                    pipeline.JobApplication,

                PipelineId =
                    pipeline.Id,

                Pipeline =
                    pipeline,

                RoundNumber =
                    roundNumber,

                RoundType =
                    request.RoundType,

                Title =
                    TextNormalizer.TrimOrNull(
                        request.Title),

                ScheduledAtUtc =
                    scheduledAtUtc,

                DurationMinutes =
                    request.DurationMinutes,

                InterviewFormat =
                    request.InterviewFormat,

                MeetingUrl =
                    TextNormalizer.TrimOrNull(
                        request.MeetingUrl),

                Location =
                    TextNormalizer.TrimOrNull(
                        request.Location),

                InterviewerName =
                    TextNormalizer.TrimOrNull(
                        request.InterviewerName),

                InterviewerEmail =
                    TextNormalizer.TrimOrNull(
                        request.InterviewerEmail),

                InternalNotes =
                    TextNormalizer.TrimOrNull(
                        request.InternalNotes),

                Status =
                    EmployerInterviewStatus.Scheduled,

                Result =
                    EmployerInterviewResult.Pending,

                CreatedByUserId =
                    employerUserId,

                CreatedByUser =
                    employer
            };

        await interviews.AddAsync(
            interview,
            cancellationToken);

        await WriteAuditAsync(
            employerUserId,
            interview,
            AuditAction.Create,
            "interviewScheduled",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(interview);
    }

    public async Task<EmployerInterviewResponse> UpdateAsync(
        Guid employerUserId,
        Guid interviewId,
        UpdateEmployerInterviewRequest request,
        CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var interview =
            await RequiredInterviewAsync(
                employerUserId,
                interviewId,
                cancellationToken);

        EnsureScheduled(interview);

        interview.RoundType =
            request.RoundType;

        interview.Title =
            TextNormalizer.TrimOrNull(
                request.Title);

        interview.DurationMinutes =
            request.DurationMinutes;

        interview.InterviewFormat =
            request.InterviewFormat;

        interview.MeetingUrl =
            TextNormalizer.TrimOrNull(
                request.MeetingUrl);

        interview.Location =
            TextNormalizer.TrimOrNull(
                request.Location);

        interview.InterviewerName =
            TextNormalizer.TrimOrNull(
                request.InterviewerName);

        interview.InterviewerEmail =
            TextNormalizer.TrimOrNull(
                request.InterviewerEmail);

        interview.InternalNotes =
            TextNormalizer.TrimOrNull(
                request.InternalNotes);

        await WriteAuditAsync(
            employerUserId,
            interview,
            AuditAction.Update,
            "interviewUpdated",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(interview);
    }

    public async Task<EmployerInterviewResponse> RescheduleAsync(
        Guid employerUserId,
        Guid interviewId,
        RescheduleEmployerInterviewRequest request,
        CancellationToken cancellationToken = default)
    {
        await rescheduleValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var interview =
            await RequiredInterviewAsync(
                employerUserId,
                interviewId,
                cancellationToken);

        EnsureScheduled(interview);

        interview.ScheduledAtUtc =
            RequireFutureUtc(
                request.ScheduledAtUtc);

        if (request.MeetingUrl is not null)
        {
            interview.MeetingUrl =
                TextNormalizer.TrimOrNull(
                    request.MeetingUrl);
        }

        if (request.Location is not null)
        {
            interview.Location =
                TextNormalizer.TrimOrNull(
                    request.Location);
        }

        await WriteAuditAsync(
            employerUserId,
            interview,
            AuditAction.Update,
            "interviewRescheduled",
            cancellationToken);


        notificationOutbox.Enqueue(
    NotificationSource.EmployerInterview,
    interview.Id,
    Guid.NewGuid(),
    interview.JobApplication.UserId,
    $"employer-interview-rescheduled:{interview.Id:D}:{interview.ScheduledAtUtc.Ticks}",
    "Interview rescheduled",
    $"Your interview for {interview.JobApplication.Job.Title} has been rescheduled.",
    "/dashboard/applications");

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(interview);
    }

    public async Task<EmployerInterviewResponse> CancelAsync(
        Guid employerUserId,
        Guid interviewId,
        CancelEmployerInterviewRequest request,
        CancellationToken cancellationToken = default)
    {
        await cancelValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var interview =
            await RequiredInterviewAsync(
                employerUserId,
                interviewId,
                cancellationToken);

        EnsureScheduled(interview);

        interview.Status =
            EmployerInterviewStatus.Cancelled;

        interview.CancelledAtUtc =
            UtcNow;

        if (!string.IsNullOrWhiteSpace(
                request.Reason))
        {
            interview.InternalNotes =
                TextNormalizer.TrimOrNull(
                    request.Reason);
        }

        await WriteAuditAsync(
            employerUserId,
            interview,
            AuditAction.Update,
            "interviewCancelled",
            cancellationToken);

        notificationOutbox.Enqueue(
    NotificationSource.EmployerInterview,
    interview.Id,
    Guid.NewGuid(),
    interview.JobApplication.UserId,
    $"employer-interview-cancelled:{interview.Id:D}",
    "Interview cancelled",
    $"Your interview for {interview.JobApplication.Job.Title} has been cancelled.",
    "/dashboard/applications");

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(interview);
    }

    public async Task<EmployerInterviewResponse> CompleteAsync(
        Guid employerUserId,
        Guid interviewId,
        CompleteEmployerInterviewRequest request,
        CancellationToken cancellationToken = default)
    {
        await completeValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var interview =
            await RequiredInterviewAsync(
                employerUserId,
                interviewId,
                cancellationToken);

        EnsureScheduled(interview);

        interview.Status =
            EmployerInterviewStatus.Completed;

        interview.Result =
            request.Result;

        interview.Score =
            request.Score;

        interview.Feedback =
            TextNormalizer.TrimOrNull(
                request.Feedback);

        interview.InternalNotes =
            TextNormalizer.TrimOrNull(
                request.InternalNotes);

        interview.CompletedAtUtc =
            UtcNow;

        await WriteAuditAsync(
            employerUserId,
            interview,
            AuditAction.Update,
            "interviewCompleted",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(interview);
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

    private async Task<EmployerInterview>
        RequiredInterviewAsync(
            Guid employerUserId,
            Guid interviewId,
            CancellationToken cancellationToken)
    {
        return await interviews
                   .GetOwnedInterviewAsync(
                       employerUserId,
                       interviewId,
                       cancellationToken)
               ?? throw new NotFoundException(
                   "Interview was not found.");
    }

    private static void EnsureScheduled(
        EmployerInterview interview)
    {
        if (interview.Status !=
            EmployerInterviewStatus.Scheduled)
        {
            throw new ConflictException(
                "Only a Scheduled interview can be modified.");
        }
    }

    private DateTime RequireFutureUtc(
        DateTime value)
    {
        if (value.Kind !=
            DateTimeKind.Utc)
        {
            throw new BadRequestException(
                "Interview date and time must be UTC.");
        }

        if (value <= UtcNow)
        {
            throw new BadRequestException(
                "Interview date and time must be in the future.");
        }

        return value;
    }

    private async Task WriteAuditAsync(
        Guid employerUserId,
        EmployerInterview interview,
        AuditAction action,
        string operation,
        CancellationToken cancellationToken)
    {
        await auditWriter.AppendAsync(
            new(
                action,
                "EmployerInterview",
                interview.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["applicationId"] =
                        interview.JobApplicationId
                            .ToString(),

                    ["pipelineId"] =
                        interview.PipelineId
                            .ToString(),

                    ["roundNumber"] =
    interview.RoundNumber
        .ToString(
            System.Globalization.CultureInfo.InvariantCulture),

                    ["roundType"] =
                        interview.RoundType
                            .ToString(),

                    ["status"] =
                        interview.Status
                            .ToString(),

                    ["result"] =
                        interview.Result
                            .ToString(),

                    ["operation"] =
                        operation
                },
                new(
                    employerUserId,
                    "Employer")),
            cancellationToken);
    }

    private static EmployerInterviewResponse Map(
        EmployerInterview interview)
    {
        var application =
            interview.JobApplication;

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
            interview.Id,
            application.Id,
            interview.PipelineId,
            candidate.Id,
            candidateName,
            candidate.Email,
            application.JobId,
            application.Job.Title,
            interview.RoundNumber,
            interview.RoundType,
            interview.Title,
            interview.ScheduledAtUtc,
            interview.DurationMinutes,
            interview.InterviewFormat,
            interview.MeetingUrl,
            interview.Location,
            interview.InterviewerName,
            interview.InterviewerEmail,
            interview.Status,
            interview.Result,
            interview.Score,
            interview.Feedback,
            interview.InternalNotes,
            interview.CompletedAtUtc,
            interview.CancelledAtUtc);
    }

    private DateTime UtcNow =>
        timeProvider
            .GetUtcNow()
            .UtcDateTime;
}
