using JobPortal.Application.Features.EmployerJobs;
using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.EmployerJobs;

public interface IEmployerJobService
{
    Task<PagedResponse<JobResponse>> SearchAsync(
        Guid employerUserId,
        EmployerJobSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<JobResponse> GetAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<JobResponse> CreateAsync(
        Guid employerUserId,
        EmployerCreateJobRequest request,
        CancellationToken cancellationToken = default);

    Task<JobResponse> UpdateAsync(
        Guid employerUserId,
        Guid jobId,
        EmployerUpdateJobRequest request,
        CancellationToken cancellationToken = default);

    Task<JobResponse> PublishAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<JobResponse> PauseAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<JobResponse> CloseAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<JobResponse> ArchiveAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);
}

public interface IEmployerJobRepository
{
    Task<Company?> GetOwnedCompanyAsync(
        Guid employerUserId,
        CancellationToken cancellationToken = default);

    Task<Job?> GetOwnedJobAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
