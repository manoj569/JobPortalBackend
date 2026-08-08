using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

#pragma warning disable CA1304, CA1311 // SQL Server translates ToUpper to UPPER; no process culture is used.
public sealed class AdminImportRepository(JobPortalDbContext context) :
    IAdminImportRepository
{
    public async Task<IReadOnlyCollection<Company>> FindCompaniesAsync(
        IReadOnlyCollection<string> slugs,
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default) =>
        await context.Companies.AsNoTracking()
            .Where(company =>
                slugs.Contains(company.Slug) ||
                normalizedNames.Contains(company.Name.ToUpper()))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Category>> FindCategoriesAsync(
        IReadOnlyCollection<string> slugs,
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default) =>
        await context.Categories.AsNoTracking()
            .Where(category => slugs.Contains(category.Slug) ||
                normalizedNames.Contains(category.Name.ToUpper()))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<ExistingJobImportIdentity>> FindJobIdentitiesAsync(
        IReadOnlyCollection<Guid> companyIds,
        CancellationToken cancellationToken = default) =>
        await context.Jobs.AsNoTracking()
            .Where(job => companyIds.Contains(job.CompanyId))
            .Select(job => new ExistingJobImportIdentity(
                job.CompanyId,
                job.Title,
                job.ApplicationUrl))
            .ToArrayAsync(cancellationToken);

    public Task AddCompaniesAsync(
        IReadOnlyCollection<Company> companies,
        CancellationToken cancellationToken = default) =>
        context.Companies.AddRangeAsync(companies, cancellationToken);

    public void UpdateCompany(Company company) =>
        context.Companies.Update(company);

    public Task AddJobsAsync(
        IReadOnlyCollection<Job> jobs,
        CancellationToken cancellationToken = default) =>
        context.Jobs.AddRangeAsync(jobs, cancellationToken);
}
#pragma warning restore CA1304, CA1311
