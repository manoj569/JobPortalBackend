using JobPortal.Application.Features.AdminApplications;
using JobPortal.Application.Features.EmployerApplications;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.EmployerApplications;

public interface IEmployerApplicationService
{
    Task<PagedResponse<AdminApplicationListItem>> SearchAsync(
        Guid employerUserId,
        EmployerApplicationQuery query,
        CancellationToken cancellationToken = default);

    Task<AdminApplicationDetail> GetAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<AdminApplicationResumeDownload> DownloadResumeAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<AdminApplicationDetail> UpdateStatusAsync(
        Guid employerUserId,
        Guid applicationId,
        UpdateEmployerApplicationStatusRequest request,
        CancellationToken cancellationToken = default);
}
