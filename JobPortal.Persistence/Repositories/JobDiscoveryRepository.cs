using JobPortal.Application.Features.JobDiscovery;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

#pragma warning disable CA1304, CA1311, CA1725, CA1862

namespace JobPortal.Persistence.Repositories;

public sealed class JobDiscoveryRepository(JobPortalDbContext db) : IJobDiscoveryRepository
{
    public Task AddAsync(JobDiscoveryRun run, CancellationToken ct) => db.JobDiscoveryRuns.AddAsync(run, ct).AsTask();
    public Task<JobDiscoveryRun?> GetForUpdateAsync(Guid id, CancellationToken ct) =>
        db.JobDiscoveryRuns.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<JobDiscoveryRunDetailsResponse?> GetDetailsAsync(Guid id, CancellationToken ct)
    {
        var run = await db.JobDiscoveryRuns.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.Trigger, x.Status, x.StartedAtUtc, x.CompletedAtUtc, x.CandidateCount,
                x.DuplicateCount, x.ImportedCount, x.ErrorSummary
            }).SingleOrDefaultAsync(ct);
        if (run is null) return null;
        var items = await db.JobDiscoveryItems.AsNoTracking().Where(x => x.RunId == id)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => new JobDiscoveryItemResponse(x.Id, x.Provider, x.SourceJobId, x.Title,
                x.CompanyName, x.CategoryName, x.ApplicationUrl, x.Location, x.Description,
                x.EmploymentType, x.PublishedAtUtc, x.Status, x.DuplicateReason, x.ExistingJobId,
                x.ImportedJobId, x.CreatedAtUtc)).ToArrayAsync(ct);
        return new(run.Id, run.Trigger, run.Status, run.StartedAtUtc, run.CompletedAtUtc,
            run.CandidateCount, run.DuplicateCount, run.ImportedCount, run.ErrorSummary, items);
    }

    public async Task<IReadOnlyCollection<JobDiscoveryRunSummary>> ListAsync(int take, CancellationToken ct) =>
        await db.JobDiscoveryRuns.AsNoTracking().OrderByDescending(x => x.StartedAtUtc).Take(take)
            .Select(x => new JobDiscoveryRunSummary(x.Id, x.Trigger, x.Status, x.StartedAtUtc,
                x.CompletedAtUtc, x.CandidateCount, x.DuplicateCount, x.ImportedCount, x.ErrorSummary))
            .ToArrayAsync(ct);
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
