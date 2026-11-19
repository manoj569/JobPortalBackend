using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IEmployerApplicationRepository
{
    Task<JobApplication?> GetOwnedApplicationAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);
}
