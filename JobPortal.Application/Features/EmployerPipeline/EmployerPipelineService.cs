using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.EmployerPipeline;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.EmployerPipeline;

public sealed class EmployerPipelineService(
    IEmployerPipelineRepository pipelines,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<EmployerPipelineQuery> queryValidator,
    IValidator<UpdateEmployerPipelineStageRequest> stageValidator,
    IValidator<StartEmployerScreeningRequest> startValidator,
    IValidator<UpdateEmployerScreeningRequest> updateValidator,
    IValidator<CompleteEmployerScreeningRequest> completeValidator,
    TimeProvider timeProvider)
    : IEmployerPipelineService
{
    public async Task<PagedResponse<EmployerPipelineResponse>> SearchAsync(
        Guid employerUserId,
        EmployerPipelineQuery query,
        CancellationToken cancellationToken = default)
    {
        await queryValidator.ValidateAndThrowAsync(
            query,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var result = await pipelines.SearchAsync(
            employerUserId,
            query.JobId,
            query.Stage,
            query.ScreeningStatus,
            query.PageNumber,
            query.PageSize,
            cancellationToken);

        return new PagedResponse<EmployerPipelineResponse>(
            result.Items.Select(Map).ToArray(),
            query.PageNumber,
            query.PageSize,
            result.TotalCount);
    }

    public async Task<EmployerPipelineResponse> GetAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var pipeline = await RequiredPipelineAsync(
            employerUserId,
            applicationId,
            cancellationToken);

        return Map(pipeline);
    }

    public async Task<EmployerPipelineResponse> StartScreeningAsync(
        Guid employerUserId,
        Guid applicationId,
        StartEmployerScreeningRequest request,
        CancellationToken cancellationToken = default)
    {
        await startValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        var employer = await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var application =
            await pipelines.GetOwnedApplicationAsync(
                employerUserId,
                applicationId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Application was not found.");

        if (application.Status !=
            JobApplicationStatus.Shortlisted)
        {
            throw new ConflictException(
                "Only a Shortlisted application can enter screening.");
        }

        var existing =
            await pipelines.GetOwnedPipelineAsync(
                employerUserId,
                applicationId,
                cancellationToken);

        if (existing is not null)
        {
            throw new ConflictException(
                "Hiring pipeline already exists for this application.");
        }

        var pipeline = new EmployerHiringPipeline
        {
            JobApplicationId = application.Id,
            JobApplication = application,

            Stage =
                EmployerPipelineStage.Screening,

            ScreeningStatus =
                EmployerScreeningStatus.InProgress,

            ScreeningNotes =
                TextNormalizer.TrimOrNull(request.Notes),

            ScreeningStartedAtUtc =
                UtcNow,

            ScreenedByUserId =
                employerUserId,

            ScreenedByUser =
                employer
        };

        await pipelines.AddAsync(
            pipeline,
            cancellationToken);

        await WriteAuditAsync(
            employerUserId,
            pipeline,
            "screeningStarted",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(pipeline);
    }

    public async Task<EmployerPipelineResponse> UpdateScreeningAsync(
        Guid employerUserId,
        Guid applicationId,
        UpdateEmployerScreeningRequest request,
        CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var pipeline =
            await RequiredPipelineAsync(
                employerUserId,
                applicationId,
                cancellationToken);

        if (pipeline.Stage !=
                EmployerPipelineStage.Screening ||
            pipeline.ScreeningStatus !=
                EmployerScreeningStatus.InProgress)
        {
            throw new ConflictException(
                "Only an in-progress screening can be updated.");
        }

        pipeline.ScreeningScore =
            request.Score;

        pipeline.ScreeningNotes =
            TextNormalizer.TrimOrNull(request.Notes);

        await WriteAuditAsync(
            employerUserId,
            pipeline,
            "screeningUpdated",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(pipeline);
    }

    public async Task<EmployerPipelineResponse> PassScreeningAsync(
        Guid employerUserId,
        Guid applicationId,
        CompleteEmployerScreeningRequest request,
        CancellationToken cancellationToken = default)
    {
        await completeValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var pipeline =
            await RequiredPipelineAsync(
                employerUserId,
                applicationId,
                cancellationToken);

        EnsureScreeningInProgress(pipeline);

        pipeline.ScreeningStatus =
            EmployerScreeningStatus.Passed;

        pipeline.ScreeningScore =
            request.Score;

        pipeline.ScreeningNotes =
            TextNormalizer.TrimOrNull(request.Notes);

        pipeline.ScreenedAtUtc =
            UtcNow;

        /*
         * Passing screening makes the candidate ready
         * for the Interview module.
         */
        pipeline.Stage =
            EmployerPipelineStage.Interview;

        await WriteAuditAsync(
            employerUserId,
            pipeline,
            "screeningPassed",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(pipeline);
    }

    public async Task<EmployerPipelineResponse> FailScreeningAsync(
        Guid employerUserId,
        Guid applicationId,
        CompleteEmployerScreeningRequest request,
        CancellationToken cancellationToken = default)
    {
        await completeValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var pipeline =
            await RequiredPipelineAsync(
                employerUserId,
                applicationId,
                cancellationToken);

        EnsureScreeningInProgress(pipeline);

        pipeline.ScreeningStatus =
            EmployerScreeningStatus.Failed;

        pipeline.ScreeningScore =
            request.Score;

        pipeline.ScreeningNotes =
            TextNormalizer.TrimOrNull(request.Notes);

        pipeline.ScreenedAtUtc =
            UtcNow;

        /*
         * Do not delete the pipeline.
         * Screening failure remains auditable.
         *
         * The application-level Rejected transition can be
         * handled by the Employer Applications module.
         */

        await WriteAuditAsync(
            employerUserId,
            pipeline,
            "screeningFailed",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(pipeline);
    }

    public async Task<EmployerPipelineResponse> UpdateStageAsync(
        Guid employerUserId,
        Guid applicationId,
        UpdateEmployerPipelineStageRequest request,
        CancellationToken cancellationToken = default)
    {
        await stageValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        await RequiredEmployerAsync(
            employerUserId,
            cancellationToken);

        var pipeline =
            await RequiredPipelineAsync(
                employerUserId,
                applicationId,
                cancellationToken);

        EnsureStageTransition(
            pipeline.Stage,
            request.Stage);

        if (request.Stage ==
                EmployerPipelineStage.Interview &&
            pipeline.ScreeningStatus !=
                EmployerScreeningStatus.Passed)
        {
            throw new ConflictException(
                "Screening must be passed before moving to Interview.");
        }

        if (request.Stage == EmployerPipelineStage.Hired)
        {
            throw new ConflictException(
                "Candidate cannot be moved directly to Hired. " +
                "An accepted offer must be completed through the offer workflow.");
        }

        pipeline.Stage = request.Stage;

        await WriteAuditAsync(
            employerUserId,
            pipeline,
            "pipelineStageChanged",
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Map(pipeline);
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

    private async Task<EmployerHiringPipeline>
        RequiredPipelineAsync(
            Guid employerUserId,
            Guid applicationId,
            CancellationToken cancellationToken)
    {
        return await pipelines.GetOwnedPipelineAsync(
                   employerUserId,
                   applicationId,
                   cancellationToken)
               ?? throw new NotFoundException(
                   "Hiring pipeline was not found.");
    }

    private static void EnsureScreeningInProgress(
        EmployerHiringPipeline pipeline)
    {
        if (pipeline.Stage !=
                EmployerPipelineStage.Screening ||
            pipeline.ScreeningStatus !=
                EmployerScreeningStatus.InProgress)
        {
            throw new ConflictException(
                "Screening is not currently in progress.");
        }
    }

    private static void EnsureStageTransition(
    EmployerPipelineStage current,
    EmployerPipelineStage requested)
    {
        var allowed = current switch
        {
            EmployerPipelineStage.Screening =>
                requested ==
                EmployerPipelineStage.Interview,

            EmployerPipelineStage.Interview =>
                requested ==
                EmployerPipelineStage.Offer,

            _ => false
        };

        if (!allowed)
        {
            throw new ConflictException(
                $"Pipeline cannot transition from {current} to {requested}.");
        }
    }

    private async Task WriteAuditAsync(
        Guid employerUserId,
        EmployerHiringPipeline pipeline,
        string operation,
        CancellationToken cancellationToken)
    {
        await auditWriter.AppendAsync(
            new(
                AuditAction.Update,
                "EmployerHiringPipeline",
                pipeline.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["applicationId"] =
                        pipeline.JobApplicationId.ToString(),

                    ["stage"] =
                        pipeline.Stage.ToString(),

                    ["screeningStatus"] =
                        pipeline.ScreeningStatus.ToString(),

                    ["operation"] =
                        operation
                },
                new(
                    employerUserId,
                    "Employer")),
            cancellationToken);
    }

    private static EmployerPipelineResponse Map(
        EmployerHiringPipeline pipeline)
    {
        var application =
            pipeline.JobApplication;

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

        return new EmployerPipelineResponse(
            pipeline.Id,
            application.Id,
            candidate.Id,
            candidateName,
            candidate.Email,
            application.JobId,
            application.Job.Title,
            application.Status,
            pipeline.Stage,
            pipeline.ScreeningStatus,
            pipeline.ScreeningScore,
            pipeline.ScreeningNotes,
            pipeline.ScreeningStartedAtUtc,
            pipeline.ScreenedAtUtc,
          
            pipeline.HiredAtUtc);
    }

    private DateTime UtcNow =>
        timeProvider
            .GetUtcNow()
            .UtcDateTime;
}
