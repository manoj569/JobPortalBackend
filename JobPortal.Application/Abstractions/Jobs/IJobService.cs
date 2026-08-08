using JobPortal.Application.Features.Jobs;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Jobs;

public interface IJobService
{
    Task<JobResponse> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken = default);
    Task<ComposeJobResponse> ComposeAsync(Guid administratorUserId, ComposeJobRequest request, CancellationToken cancellationToken = default);
    Task<JobResponse> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JobResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JobResponse> PublishAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JobResponse> UnpublishAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JobResponse> CloseAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JobResponse> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JobResponse> SetFeaturedAsync(Guid id, bool isFeatured, CancellationToken cancellationToken = default);
    Task<JobResponse> SetHiddenAsync(Guid id, bool isHidden, CancellationToken cancellationToken = default);
    Task<PagedResponse<JobResponse>> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default);
    Task<AdminRecruiterContactResponse> GetRecruiterContactAsync(
    Guid jobId,
    CancellationToken cancellationToken = default);

    Task<AdminRecruiterContactResponse> UpdateRecruiterContactAsync(
        Guid jobId,
        UpdateRecruiterContactRequest request,
        CancellationToken cancellationToken = default);
}

public interface IJobExpiryService
{
    Task<int> ExpireOverdueAsync(CancellationToken cancellationToken = default);
}
