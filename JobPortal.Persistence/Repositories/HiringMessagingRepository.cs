using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class HiringMessagingRepository(
    JobPortalDbContext context)
    : IHiringMessagingRepository
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
                    x.Job.Company.OwnerUserId == employerUserId,
                cancellationToken);
    }

    public Task<HiringConversation?> GetByApplicationAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        return BaseQuery()
            .FirstOrDefaultAsync(
                x => x.JobApplicationId == applicationId,
                cancellationToken);
    }

    public Task<HiringConversation?> GetForEmployerAsync(
        Guid employerUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return BaseQuery()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == conversationId &&
                    x.EmployerUserId == employerUserId,
                cancellationToken);
    }

    public Task<HiringConversation?> GetForCandidateAsync(
        Guid candidateUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return BaseQuery()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == conversationId &&
                    x.CandidateUserId == candidateUserId,
                cancellationToken);
    }

    public async Task<(
        IReadOnlyCollection<HiringConversation> Items,
        int TotalCount)> SearchEmployerAsync(
        Guid employerUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = BaseQuery()
            .AsNoTracking()
            .Where(x =>
                x.EmployerUserId == employerUserId);

        var totalCount =
            await query.CountAsync(cancellationToken);

        var items =
            await query
                .OrderByDescending(x =>
                    x.LastMessageAtUtc ?? x.CreatedAtUtc)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToArrayAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(
        IReadOnlyCollection<HiringConversation> Items,
        int TotalCount)> SearchCandidateAsync(
        Guid candidateUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = BaseQuery()
            .AsNoTracking()
            .Where(x =>
                x.CandidateUserId == candidateUserId);

        var totalCount =
            await query.CountAsync(cancellationToken);

        var items =
            await query
                .OrderByDescending(x =>
                    x.LastMessageAtUtc ?? x.CreatedAtUtc)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToArrayAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddConversationAsync(
        HiringConversation conversation,
        CancellationToken cancellationToken = default)
    {
        await context.HiringConversations.AddAsync(
            conversation,
            cancellationToken);
    }

    public async Task AddMessageAsync(
        HiringMessage message,
        CancellationToken cancellationToken = default)
    {
        await context.HiringMessages.AddAsync(
            message,
            cancellationToken);
    }

    private IQueryable<HiringConversation> BaseQuery()
    {
        return context.HiringConversations
            .Include(x => x.CandidateUser)
            .Include(x => x.EmployerUser)
            .Include(x => x.JobApplication)
                .ThenInclude(x => x.Job)
            .Include(x => x.Messages)
                .ThenInclude(x => x.SenderUser);
    }
}
