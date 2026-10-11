using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.AdminApplications;

public sealed record AdminApplicationQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? JobId = null,
    Guid? CompanyId = null,
    Guid? CategoryId = null,
    JobApplicationStatus? Status = null,
    DateTime? SubmittedFromUtc = null,
    DateTime? SubmittedToUtc = null,
    string? Keyword = null);

public sealed record AdminApplicationListItem(
    Guid Id,
    JobApplicationStatus Status,
    DateTime SubmittedAtUtc,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid JobId,
    string JobTitle,
    string JobSlug,
    Guid CompanyId,
    string CompanyName,
    Guid? CategoryId,
    string CategoryName,
    bool HasResume);

public sealed record AdminCandidateProfile(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Headline,
    string? Bio,
    string? Location,
    IReadOnlyCollection<string> Skills,
    IReadOnlyCollection<string> Education,
    IReadOnlyCollection<string> Experience,
    string? LinkedInUrl,
    string? PortfolioUrl,
    IReadOnlyCollection<EmploymentType> PreferredJobTypes);

public sealed record AdminApplicationJobSummary(
    Guid Id,
    string Title,
    string Slug,
    string? Location,
    EmploymentType EmploymentType,
    WorkplaceType WorkplaceType,
    Guid CompanyId,
    string CompanyName,
    Guid? CategoryId,
    string CategoryName);

public sealed record AdminApplicationStatusHistoryResponse(
    Guid Id,
    JobApplicationStatus? PreviousStatus,
    JobApplicationStatus NewStatus,
    DateTime ChangedAtUtc,
    Guid ActorUserId,
    string ActorName,
    string? InternalNote);

public sealed record AdminApplicationDetail(
    Guid Id,
    JobApplicationStatus Status,
    DateTime SubmittedAtUtc,
    DateTime? WithdrawnAtUtc,
    string? CoverLetter,
    bool HasResume,
    string? ResumeFileName,
    AdminCandidateProfile Candidate,
    AdminApplicationJobSummary Job,
    IReadOnlyCollection<AdminApplicationStatusHistoryResponse> StatusHistory);

public sealed record UpdateAdminApplicationStatusRequest(
    JobApplicationStatus Status, string? InternalNote);

public sealed record AdminApplicationResumeDownload(
    Stream Content, string FileName, string ContentType);
