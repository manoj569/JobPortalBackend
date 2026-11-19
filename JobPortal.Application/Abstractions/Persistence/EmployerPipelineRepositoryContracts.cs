using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IEmployerPipelineRepository
{
    Task<JobApplication?> GetOwnedApplicationAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<EmployerHiringPipeline?> GetOwnedPipelineAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyCollection<EmployerHiringPipeline> Items, int TotalCount)>
        SearchAsync(
            Guid employerUserId,
            Guid? jobId,
            EmployerPipelineStage? stage,
            EmployerScreeningStatus? screeningStatus,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default);

    Task AddAsync(
        EmployerHiringPipeline pipeline,
        CancellationToken cancellationToken = default);
}
