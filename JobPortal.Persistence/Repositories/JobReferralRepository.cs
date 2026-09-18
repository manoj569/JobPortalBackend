using JobPortal.Application.Abstractions.Referrals;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class JobReferralRepository(JobPortalDbContext context) : IJobReferralRepository
{
    private IQueryable<JobReferral> WithIncludes() =>
        context.JobReferrals
            .Include(x => x.Job).ThenInclude(x => x.Company)
            .Include(x => x.ReferrerUser);

    public async Task AddAsync(JobReferral referral, CancellationToken cancellationToken = default) =>
        await context.JobReferrals.AddAsync(referral, cancellationToken);

    public Task<JobReferral?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithIncludes().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<JobReferral?> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        WithIncludes().SingleOrDefaultAsync(x => x.JobId == jobId, cancellationToken);

    public async Task<(IReadOnlyCollection<JobReferral> Items, int TotalCount)> GetPendingAsync(
        int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = WithIncludes()
            .Where(x => x.ApprovalStatus == JobReferralApprovalStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(IReadOnlyCollection<JobReferral> Items, int TotalCount)> GetApprovedAsync(
        int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = WithIncludes()
            .Where(x => x.ApprovalStatus == JobReferralApprovalStatus.Approved &&
                x.Job.Status == JobStatus.Published &&
                !x.Job.IsHidden && !x.Job.IsDeleted &&
                (!x.Job.ExpiresAtUtc.HasValue || x.Job.ExpiresAtUtc > DateTime.UtcNow))
            .OrderByDescending(x => x.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyCollection<JobReferral>> GetByReferrerAsync(
        Guid referrerUserId, CancellationToken cancellationToken = default) =>
        await WithIncludes()
            .Where(x => x.ReferrerUserId == referrerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task RecordUnlockAsync(
        Guid jobReferralId, Guid seekerUserId, CancellationToken cancellationToken = default)
    {
        var alreadyUnlocked = await context.ReferralUnlocks.AnyAsync(
            x => x.JobReferralId == jobReferralId && x.UserId == seekerUserId, cancellationToken);
        if (alreadyUnlocked) return;

        await context.ReferralUnlocks.AddAsync(new ReferralUnlock
        {
            JobReferralId = jobReferralId,
            UserId = seekerUserId,
            UnlockedAtUtc = DateTime.UtcNow,
        }, cancellationToken);
    }
}
