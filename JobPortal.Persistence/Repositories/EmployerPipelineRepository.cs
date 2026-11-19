using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class EmployerPipelineRepository(
    JobPortalDbContext context)
    : IEmployerPipelineRepository
{
    public Task<JobApplication?> GetOwnedApplicationAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        return context.JobApplications
            .Include(x => x.User)
            .Include(x => x.Job)
                .ThenInclude(x => x.Company)
            .FirstOrDefaultAsync(
                x =>
                    x.Id == applicationId &&
                    !x.IsDeleted &&
                    x.Job.Company.OwnerUserId == employerUserId,
                cancellationToken);
    }

    public Task<EmployerHiringPipeline?> GetOwnedPipelineAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        return context.Set<EmployerHiringPipeline>()
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.User)
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.Job)
                    .ThenInclude(x => x.Company)
            .FirstOrDefaultAsync(
                x =>
                    !x.IsDeleted &&
                    x.JobApplicationId == applicationId &&
                    x.JobApplication.Job.Company.OwnerUserId ==
                        employerUserId,
                cancellationToken);
    }

    public async Task<(
        IReadOnlyCollection<EmployerHiringPipeline> Items,
        int TotalCount)> SearchAsync(
        Guid employerUserId,
        Guid? jobId,
        EmployerPipelineStage? stage,
        EmployerScreeningStatus? screeningStatus,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<EmployerHiringPipeline>()
            .AsNoTracking()
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.User)
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.Job)
                    .ThenInclude(x => x.Company)
            .Where(x =>
                !x.IsDeleted &&
                x.JobApplication.Job.Company.OwnerUserId ==
                    employerUserId);

        if (jobId.HasValue)
        {
            query = query.Where(
                x => x.JobApplication.JobId == jobId.Value);
        }

        if (stage.HasValue)
        {
            query = query.Where(
                x => x.Stage == stage.Value);
        }

        if (screeningStatus.HasValue)
        {
            query = query.Where(
                x => x.ScreeningStatus ==
                    screeningStatus.Value);
        }

        var totalCount =
            await query.CountAsync(cancellationToken);

        var items =
            await query
                .OrderByDescending(x => x.CreatedAtUtc)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddAsync(
        EmployerHiringPipeline pipeline,
        CancellationToken cancellationToken = default)
    {
        await context.Set<EmployerHiringPipeline>()
            .AddAsync(
                pipeline,
                cancellationToken);
    }
}
