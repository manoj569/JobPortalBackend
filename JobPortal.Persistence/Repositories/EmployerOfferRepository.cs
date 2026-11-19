using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class EmployerOfferRepository(
    JobPortalDbContext context)
    : IEmployerOfferRepository
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

    public Task<EmployerOffer?> GetOwnedOfferAsync(
        Guid employerUserId,
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        return BaseQuery()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == offerId &&
                    x.JobApplication.Job.Company.OwnerUserId ==
                        employerUserId,
                cancellationToken);
    }

    public Task<EmployerOffer?>
        GetOwnedOfferForApplicationAsync(
            Guid employerUserId,
            Guid applicationId,
            CancellationToken cancellationToken = default)
    {
        return BaseQuery()
            .FirstOrDefaultAsync(
                x =>
                    x.JobApplicationId == applicationId &&
                    x.JobApplication.Job.Company.OwnerUserId ==
                        employerUserId,
                cancellationToken);
    }

    public async Task<(
        IReadOnlyCollection<EmployerOffer> Items,
        int TotalCount)> SearchAsync(
        Guid employerUserId,
        Guid? jobId,
        EmployerOfferStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query =
            BaseQuery()
                .AsNoTracking()
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
                x => x.Status ==
                     status.Value);
        }

        var totalCount =
            await query.CountAsync(
                cancellationToken);

        var items =
            await query
                .OrderByDescending(x =>
                    x.CreatedAtUtc)
                .Skip(
                    (pageNumber - 1) *
                    pageSize)
                .Take(pageSize)
                .ToArrayAsync(
                    cancellationToken);

        return (items, totalCount);
    }

    public async Task AddAsync(
        EmployerOffer offer,
        CancellationToken cancellationToken = default)
    {
        await context.EmployerOffers
            .AddAsync(
                offer,
                cancellationToken);
    }

    private IQueryable<EmployerOffer> BaseQuery()
    {
        return context.EmployerOffers
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.User)
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.Job)
                    .ThenInclude(x => x.Company)
            .Include(x => x.Pipeline);
    }

    public Task<EmployerOffer?> GetCandidateOfferAsync(
    Guid candidateUserId,
    Guid offerId,
    CancellationToken cancellationToken = default)
    {
        return BaseQuery()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == offerId &&
                    x.JobApplication.UserId == candidateUserId,
                cancellationToken);
    }


    public Task<int> ExpireOverdueAsync(
     DateTime nowUtc,
     CancellationToken cancellationToken = default)
    {
        return context.EmployerOffers
            .Where(x =>
                x.Status == EmployerOfferStatus.Sent &&
                x.ExpiresAtUtc.HasValue &&
                x.ExpiresAtUtc.Value <= nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        x => x.Status,
                        EmployerOfferStatus.Expired)
                    .SetProperty(
                        x => x.ExpiredAtUtc,
                        nowUtc),
                cancellationToken);
    }
}
