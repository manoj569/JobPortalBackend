using JobPortal.Application.Abstractions.EmployerJobs;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class EmployerJobRepository(
    JobPortalDbContext context)
    : IEmployerJobRepository
{
    public Task<Company?> GetOwnedCompanyAsync(
        Guid employerUserId,
        CancellationToken cancellationToken = default) =>
        context.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.OwnerUserId == employerUserId &&
                    !x.IsDeleted,
                cancellationToken);

    public Task<Job?> GetOwnedJobAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default) =>
        context.Jobs
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Category)
            .FirstOrDefaultAsync(
                x =>
                    x.Id == jobId &&
                    !x.IsDeleted &&
                    x.Company.OwnerUserId == employerUserId,
                cancellationToken);
}
