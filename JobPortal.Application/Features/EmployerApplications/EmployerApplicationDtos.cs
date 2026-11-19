using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.EmployerApplications;

public sealed record EmployerApplicationQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? JobId = null,
    Guid? CategoryId = null,
    JobApplicationStatus? Status = null,
    DateTime? SubmittedFromUtc = null,
    DateTime? SubmittedToUtc = null,
    string? Keyword = null);

public sealed record UpdateEmployerApplicationStatusRequest(
    JobApplicationStatus Status,
    string? InternalNote);
