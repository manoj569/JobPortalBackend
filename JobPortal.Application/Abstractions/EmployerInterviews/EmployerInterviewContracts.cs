using JobPortal.Application.Features.EmployerInterviews;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.EmployerInterviews;

public interface IEmployerInterviewService
{
    Task<PagedResponse<EmployerInterviewResponse>> SearchAsync(
        Guid employerUserId,
        EmployerInterviewQuery query,
        CancellationToken cancellationToken = default);

    Task<EmployerInterviewResponse> GetAsync(
        Guid employerUserId,
        Guid interviewId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<EmployerInterviewResponse>>
        GetForApplicationAsync(
            Guid employerUserId,
            Guid applicationId,
            CancellationToken cancellationToken = default);

    Task<EmployerInterviewResponse> CreateAsync(
        Guid employerUserId,
        Guid applicationId,
        CreateEmployerInterviewRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerInterviewResponse> UpdateAsync(
        Guid employerUserId,
        Guid interviewId,
        UpdateEmployerInterviewRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerInterviewResponse> RescheduleAsync(
        Guid employerUserId,
        Guid interviewId,
        RescheduleEmployerInterviewRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerInterviewResponse> CancelAsync(
        Guid employerUserId,
        Guid interviewId,
        CancelEmployerInterviewRequest request,
        CancellationToken cancellationToken = default);

    Task<EmployerInterviewResponse> CompleteAsync(
        Guid employerUserId,
        Guid interviewId,
        CompleteEmployerInterviewRequest request,
        CancellationToken cancellationToken = default);
}
