using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class EmployerApplicationRepository(
    JobPortalDbContext context)
    : IEmployerApplicationRepository
{
    public Task<JobApplication?> GetOwnedApplicationAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        context.JobApplications
            .Include(x => x.User)
            .Include(x => x.Job)
                .ThenInclude(x => x.Company)
            .Include(x => x.Job)
                .ThenInclude(x => x.Category)
            .Include(x => x.StatusHistory)
                .ThenInclude(x => x.ActorUser)
            .FirstOrDefaultAsync(
                x =>
                    x.Id == applicationId &&
                    x.Job.Company.OwnerUserId == employerUserId,
                cancellationToken);
}
