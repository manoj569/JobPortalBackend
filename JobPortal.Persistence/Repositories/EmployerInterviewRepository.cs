using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class EmployerInterviewRepository(
    JobPortalDbContext context)
    : IEmployerInterviewRepository
{
    public Task<EmployerHiringPipeline?> GetOwnedPipelineAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        return context.EmployerHiringPipelines
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.User)
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.Job)
                    .ThenInclude(x => x.Company)
            .FirstOrDefaultAsync(
                x =>
                    x.JobApplicationId == applicationId &&
                    x.JobApplication.Job.Company.OwnerUserId ==
                        employerUserId,
                cancellationToken);
    }

    public Task<EmployerInterview?> GetOwnedInterviewAsync(
        Guid employerUserId,
        Guid interviewId,
        CancellationToken cancellationToken = default)
    {
        return context.EmployerInterviews
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.User)
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.Job)
                    .ThenInclude(x => x.Company)
            .Include(x => x.Pipeline)
            .FirstOrDefaultAsync(
                x =>
                    x.Id == interviewId &&
                    x.JobApplication.Job.Company.OwnerUserId ==
                        employerUserId,
                cancellationToken);
    }

    public async Task<IReadOnlyCollection<EmployerInterview>>
        GetForApplicationAsync(
            Guid employerUserId,
            Guid applicationId,
            CancellationToken cancellationToken = default)
    {
        return await context.EmployerInterviews
            .AsNoTracking()
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.User)
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.Job)
                    .ThenInclude(x => x.Company)
            .Where(x =>
                x.JobApplicationId == applicationId &&
                x.JobApplication.Job.Company.OwnerUserId ==
                    employerUserId)
            .OrderBy(x => x.RoundNumber)
            .ThenBy(x => x.ScheduledAtUtc)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<(
        IReadOnlyCollection<EmployerInterview> Items,
        int TotalCount)> SearchAsync(
        Guid employerUserId,
        Guid? jobId,
        EmployerInterviewStatus? status,
        EmployerInterviewResult? result,
        InterviewRoundType? roundType,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.EmployerInterviews
            .AsNoTracking()
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.User)
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.Job)
                    .ThenInclude(x => x.Company)
            .Where(x =>
                x.JobApplication.Job.Company.OwnerUserId ==
                    employerUserId);

        if (jobId.HasValue)
        {
            query = query.Where(
                x => x.JobApplication.JobId ==
                     jobId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(
                x => x.Status == status.Value);
        }

        if (result.HasValue)
        {
            query = query.Where(
                x => x.Result == result.Value);
        }

        if (roundType.HasValue)
        {
            query = query.Where(
                x => x.RoundType == roundType.Value);
        }

        var totalCount =
            await query.CountAsync(
                cancellationToken);

        var items =
            await query
                .OrderBy(x => x.ScheduledAtUtc)
                .ThenBy(x => x.RoundNumber)
                .Skip(
                    (pageNumber - 1) *
                    pageSize)
                .Take(pageSize)
                .ToArrayAsync(
                    cancellationToken);

        return (items, totalCount);
    }

    public async Task<int> GetNextRoundNumberAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var currentMaximum =
            await context.EmployerInterviews
                .Where(x =>
                    x.JobApplicationId ==
                    applicationId)
                .Select(x => (int?)x.RoundNumber)
                .MaxAsync(cancellationToken);

        return (currentMaximum ?? 0) + 1;
    }

    public async Task AddAsync(
        EmployerInterview interview,
        CancellationToken cancellationToken = default)
    {
        await context.EmployerInterviews
            .AddAsync(
                interview,
                cancellationToken);
    }
}
