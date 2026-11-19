using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.EmployerInterviews;

public sealed record EmployerInterviewResponse(
    Guid Id,
    Guid ApplicationId,
    Guid PipelineId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid JobId,
    string JobTitle,
    int RoundNumber,
    InterviewRoundType RoundType,
    string? Title,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    InterviewFormat InterviewFormat,
    string? MeetingUrl,
    string? Location,
    string? InterviewerName,
    string? InterviewerEmail,
    EmployerInterviewStatus Status,
    EmployerInterviewResult Result,
    decimal? Score,
    string? Feedback,
    string? InternalNotes,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc);

public sealed record EmployerInterviewQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? JobId = null,
    EmployerInterviewStatus? Status = null,
    EmployerInterviewResult? Result = null,
    InterviewRoundType? RoundType = null);

public sealed record CreateEmployerInterviewRequest(
    InterviewRoundType RoundType,
    string? Title,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    InterviewFormat InterviewFormat,
    string? MeetingUrl,
    string? Location,
    string? InterviewerName,
    string? InterviewerEmail,
    string? InternalNotes);

public sealed record UpdateEmployerInterviewRequest(
    InterviewRoundType RoundType,
    string? Title,
    int DurationMinutes,
    InterviewFormat InterviewFormat,
    string? MeetingUrl,
    string? Location,
    string? InterviewerName,
    string? InterviewerEmail,
    string? InternalNotes);

public sealed record RescheduleEmployerInterviewRequest(
    DateTime ScheduledAtUtc,
    string? MeetingUrl,
    string? Location);

public sealed record CancelEmployerInterviewRequest(
    string? Reason);

public sealed record CompleteEmployerInterviewRequest(
    EmployerInterviewResult Result,
    decimal? Score,
    string? Feedback,
    string? InternalNotes);
