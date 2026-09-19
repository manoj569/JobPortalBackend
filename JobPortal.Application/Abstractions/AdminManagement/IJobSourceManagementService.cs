using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.AdminManagement;

public interface IJobSourceManagementService
{
    Task<PagedResponse<JobSourceResponse>> SearchAsync(JobSourceSearchQuery query, CancellationToken cancellationToken = default);
    Task<JobSourceResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JobSourceResponse> CreateAsync(SaveJobSourceRequest request, CancellationToken cancellationToken = default);
    Task<JobSourceResponse> UpdateAsync(Guid id, SaveJobSourceRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JobSourceRunResult> RunAsync(Guid id, CancellationToken cancellationToken = default);
}
