using JobPortal.Application.Features.HiringDrives;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class HiringDriveRepository(JobPortalDbContext context) : IHiringDriveRepository
{
    private IQueryable<HiringDrive> Current(DateTime now) => context.HiringDrives.Where(x => x.IsActive
        && x.StartAtUtc <= now && (x.EndAtUtc == null || x.EndAtUtc >= now)
        && (x.JobId == null || PublicJobRepository.AvailableJobs(context.Jobs, now).Any(j => j.Id == x.JobId)));
    public async Task<IReadOnlyCollection<HiringDrive>> PublicAsync(DateTime now, CancellationToken cancellationToken) =>
        await Current(now).AsNoTracking().Include(x => x.Company).OrderBy(x => x.DisplayOrder)
            .ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
    public async Task<IReadOnlyCollection<HiringDrive>> HomepageAsync(DateTime now, CancellationToken cancellationToken) =>
        await Current(now).Where(x => x.ShowOnHome && (x.EndAtUtc == null || x.EndAtUtc > now)).AsNoTracking().Include(x => x.Company)
            .OrderBy(x => x.DisplayOrder).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);
    public Task<HiringDrive?> PublicDetailAsync(Guid id, DateTime now, CancellationToken cancellationToken) =>
        Current(now).AsNoTracking().Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<(IReadOnlyCollection<HiringDrive> Items, int Total)> SearchAsync(HiringDriveQuery query, CancellationToken cancellationToken)
    {
        var source = context.HiringDrives.AsNoTracking();
        var total = await source.CountAsync(cancellationToken);
        var items = await source.OrderBy(x => x.DisplayOrder).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return (items, total);
    }
    public Task<HiringDrive?> GetAsync(Guid id, CancellationToken cancellationToken) => context.HiringDrives.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<bool> CompanyExistsAsync(Guid id, CancellationToken cancellationToken) => context.Companies.AnyAsync(x => x.Id == id, cancellationToken);
    public Task<bool> JobExistsAsync(Guid id, CancellationToken cancellationToken) => context.Jobs.AnyAsync(x => x.Id == id, cancellationToken);
    public async Task AddAsync(HiringDrive drive, CancellationToken cancellationToken) => await context.HiringDrives.AddAsync(drive, cancellationToken);
    public async Task<HiringDrive?> ClickAsync(Guid id, DateTime now, CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<HiringDrive?>(async retryCancellationToken =>
        {
            // Keep the locked destination read, visibility check, and atomic increment in one
            // retryable transaction. No tracking ensures each retry uses the newly locked row state.
            await using var transaction = await context.Database.BeginTransactionAsync(retryCancellationToken);
            var drive = await context.HiringDrives.FromSqlInterpolated($"SELECT * FROM \"HiringDrives\" WHERE \"Id\" = {id} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(retryCancellationToken);
            if (drive is null || !drive.IsActive || drive.StartAtUtc > now || drive.EndAtUtc < now) return null;
            if (drive.JobId is { } jobId)
            {
                drive.Job = await PublicJobRepository.AvailableJobs(context.Jobs, now)
                    .SingleOrDefaultAsync(job => job.Id == jobId, retryCancellationToken);
                if (drive.Job is null) return null;
            }
            var changed = await Current(now).Where(x => x.Id == id).ExecuteUpdateAsync(
                s => s.SetProperty(x => x.ClickCount, x => x.ClickCount + 1), retryCancellationToken);
            await transaction.CommitAsync(retryCancellationToken);
            return changed == 1 ? drive : null;
        }, cancellationToken);
    }
    public async Task ImpressionsAsync(IReadOnlyCollection<Guid> ids, DateTime now, CancellationToken cancellationToken) =>
        await Current(now).Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(
            s => s.SetProperty(x => x.ImpressionCount, x => x.ImpressionCount + 1), cancellationToken);
}
