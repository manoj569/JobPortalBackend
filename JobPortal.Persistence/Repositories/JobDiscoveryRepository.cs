using JobPortal.Application.Features.JobDiscovery;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

#pragma warning disable CA1304, CA1311, CA1725, CA1862

namespace JobPortal.Persistence.Repositories;

public sealed class JobDiscoveryRepository(JobPortalDbContext db) : IJobDiscoveryRepository
{
    public Task AddAsync(JobDiscoveryRun run, CancellationToken ct) => db.JobDiscoveryRuns.AddAsync(run, ct).AsTask();
    public async Task<JobDiscoveryRun?> GetAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = db.JobDiscoveryRuns.Include(x => x.Items).Where(x => x.Id == id);
        return await (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct);
    }
    public async Task<IReadOnlyCollection<JobDiscoveryRun>> ListAsync(int take, CancellationToken ct) =>
        await db.JobDiscoveryRuns.AsNoTracking().OrderByDescending(x => x.StartedAtUtc).Take(take).ToArrayAsync(ct);
    public async Task<(Guid? JobId, string? Reason)> FindDuplicateAsync(string provider, ExternalJobCandidate c, DateTime cutoff, CancellationToken ct)
    {
        if (await db.JobDiscoveryItems.AnyAsync(x => x.Provider == provider && x.SourceJobId == c.SourceJobId, ct)) return (null, "ProviderSourceJobId");
        var url = c.ApplicationUrl.Trim().ToLower();
        var byUrl = await db.Jobs.AsNoTracking().Where(x => x.ApplicationUrl.ToLower() == url).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (byUrl is not null) return (byUrl, "ApplicationUrl");
        var title = c.Title.Trim().ToLower(); var company = c.CompanyName.Trim().ToLower(); var location = (c.Location ?? "").Trim().ToLower();
        var probable = await db.Jobs.AsNoTracking().Where(x => x.CreatedAtUtc >= cutoff && x.Title.ToLower() == title &&
            x.Company.Name.ToLower() == company && (x.Location ?? "").ToLower() == location).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return probable is null ? (null, null) : (probable, "TitleCompanyLocation30Days");
    }
}
