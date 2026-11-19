using JobPortal.Application.Abstractions.EmployerCompanies;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class EmployerCompanyRepository(
    JobPortalDbContext context)
    : IEmployerCompanyRepository
{
    public Task<bool> IsEmployerAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        context.Users
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.Id == userId &&
                    x.RoleId == SystemRoleIds.Employer &&
                    x.Status == UserStatus.Active,
                cancellationToken);

    public Task<Company?> GetByOwnerUserIdAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default) =>
        context.Companies
            .FirstOrDefaultAsync(
                x =>
                    x.OwnerUserId == ownerUserId &&
                    !x.IsDeleted,
                cancellationToken);

    public Task<Company?> FindByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken = default) =>
        context.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.NormalizedName == normalizedName &&
                    !x.IsDeleted,
                cancellationToken);

    public Task<Company?> FindByWebsiteDomainAsync(
        string normalizedDomain,
        CancellationToken cancellationToken = default) =>
        context.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.NormalizedWebsiteDomain == normalizedDomain &&
                    !x.IsDeleted,
                cancellationToken);

    public Task<bool> SlugExistsAsync(
        string slug,
        Guid? excludingCompanyId = null,
        CancellationToken cancellationToken = default) =>
        context.Companies.AnyAsync(
            x =>
                x.Slug == slug &&
                !x.IsDeleted &&
                (!excludingCompanyId.HasValue ||
                 x.Id != excludingCompanyId.Value),
            cancellationToken);

    public Task AddAsync(
        Company company,
        CancellationToken cancellationToken = default) =>
        context.Companies
            .AddAsync(company, cancellationToken)
            .AsTask();

    public Task SaveAsync(
        CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
