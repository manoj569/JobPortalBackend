using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IEmployerInterviewRepository
{
    Task<EmployerHiringPipeline?> GetOwnedPipelineAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<EmployerInterview?> GetOwnedInterviewAsync(
        Guid employerUserId,
        Guid interviewId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<EmployerInterview>>
        GetForApplicationAsync(
            Guid employerUserId,
            Guid applicationId,
            CancellationToken cancellationToken = default);

    Task<(IReadOnlyCollection<EmployerInterview> Items, int TotalCount)>
        SearchAsync(
            Guid employerUserId,
            Guid? jobId,
            EmployerInterviewStatus? status,
            EmployerInterviewResult? result,
            InterviewRoundType? roundType,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default);

    Task<int> GetNextRoundNumberAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        EmployerInterview interview,
        CancellationToken cancellationToken = default);
}
