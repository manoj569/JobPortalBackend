using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.EmployerPipeline;

public sealed record EmployerPipelineResponse(
    Guid Id,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid JobId,
    string JobTitle,
    JobApplicationStatus ApplicationStatus,
    EmployerPipelineStage Stage,
    EmployerScreeningStatus ScreeningStatus,
    decimal? ScreeningScore,
    string? ScreeningNotes,
    DateTime? ScreeningStartedAtUtc,
    DateTime? ScreenedAtUtc,
   
    DateTime? HiredAtUtc);

public sealed record EmployerPipelineQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? JobId = null,
    EmployerPipelineStage? Stage = null,
    EmployerScreeningStatus? ScreeningStatus = null);

public sealed record UpdateEmployerPipelineStageRequest(
    EmployerPipelineStage Stage);

public sealed record StartEmployerScreeningRequest(
    string? Notes);

public sealed record UpdateEmployerScreeningRequest(
    decimal? Score,
    string? Notes);

public sealed record CompleteEmployerScreeningRequest(
    decimal? Score,
    string? Notes);
