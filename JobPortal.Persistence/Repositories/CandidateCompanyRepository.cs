using JobPortal.Application.Abstractions.CandidateCompanies;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed class CandidateCompanyRepository(JobPortalDbContext db) : ICandidateCompanyRepository
{
    public Task<bool> IsCandidateAsync(Guid candidateId, CancellationToken ct) =>
        db.Users.AnyAsync(x => x.Id == candidateId && x.RoleId == SystemRoleIds.Candidate && x.Status == UserStatus.Active, ct);

    public async Task<IReadOnlyCollection<CompanyOption>> SearchAsync(string normalizedQuery, int limit, CancellationToken ct) =>
        await db.Companies.AsNoTracking()
            .Where(x => x.NormalizedName.Contains(normalizedQuery))
            .OrderBy(x => x.NormalizedName == normalizedQuery ? 0 : x.NormalizedName.StartsWith(normalizedQuery) ? 1 : 2)
            .ThenBy(x => x.Name).ThenBy(x => x.Id).Take(limit)
            .Select(x => new CompanyOption(x.Id, x.Name)).ToArrayAsync(ct);

    public Task<Company?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct) =>
        db.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedName == normalizedName, ct);

    public Task<int> CountCreatedSinceAsync(Guid candidateId, DateTime since, CancellationToken ct) =>
        db.Companies.CountAsync(x => x.SubmittedByCandidateId == candidateId && x.CreatedAtUtc >= since, ct);

    public async Task<Guid?> FindActiveAdministratorIdAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(x => x.RoleId == SystemRoleIds.Administrator && x.Status == UserStatus.Active)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);

    public Task AddAsync(Company company, CancellationToken ct) => db.Companies.AddAsync(company, ct).AsTask();

    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new UniqueConstraintException("A company with the normalized name already exists.", exception);
        }
    }

    public void DiscardPendingChanges()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State != EntityState.Unchanged).ToArray())
            entry.State = EntityState.Detached;
    }
}
