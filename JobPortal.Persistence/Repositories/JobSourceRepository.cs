using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class JobSourceRepository(JobPortalDbContext context)
    : IJobSourceRepository
{
    public Task<JobSource?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        context.JobSources
            .Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Update(JobSource source)
    {
        // A failed record can clear the change tracker. Attach only this source,
        // never its Company graph, and persist only run bookkeeping.
        var entry = context.Entry(source);
        if (entry.State == EntityState.Detached)
            entry.State = EntityState.Unchanged;

        entry.Property(x => x.LastRunAtUtc).IsModified = true;
        entry.Property(x => x.LastSuccessfulRunAtUtc).IsModified = true;
        entry.Property(x => x.LastError).IsModified = true;
        entry.Property(x => x.ConsecutiveFailures).IsModified = true;
    }
}
