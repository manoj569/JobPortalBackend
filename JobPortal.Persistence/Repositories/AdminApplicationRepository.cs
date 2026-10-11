using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.AdminApplications;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AdminApplicationRepository(JobPortalDbContext context) :
    IAdminApplicationRepository
{
    public async Task<(IReadOnlyCollection<AdminApplicationListItem> Items, int TotalCount)>
        SearchAsync(AdminApplicationQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.JobApplications.AsNoTracking().AsQueryable();
        if (query.JobId.HasValue)
            source = source.Where(x => x.JobId == query.JobId);
        if (query.CompanyId.HasValue)
            source = source.Where(x => x.Job.CompanyId == query.CompanyId);
        if (query.CategoryId.HasValue)
            source = source.Where(x => x.Job.CategoryId == query.CategoryId);
        if (query.Status.HasValue)
            source = source.Where(x => x.Status == query.Status);
        if (query.SubmittedFromUtc.HasValue)
            source = source.Where(x => x.SubmittedAtUtc >= query.SubmittedFromUtc);
        if (query.SubmittedToUtc.HasValue)
            source = source.Where(x => x.SubmittedAtUtc <= query.SubmittedToUtc);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            source = source.Where(x =>
                x.User.FirstName.Contains(keyword) ||
                x.User.LastName.Contains(keyword) ||
                x.User.Email.Contains(keyword) ||
                x.Job.Title.Contains(keyword) ||
                x.Job.ReferenceNumber.Contains(keyword) ||
                x.Job.Company.Name.Contains(keyword));
        }

        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new AdminApplicationListItem(
                x.Id,
                x.Status,
                x.SubmittedAtUtc,
                x.UserId,
                x.User.FirstName + " " + x.User.LastName,
                x.User.Email,
                x.JobId,
                x.Job.Title,
                x.Job.Slug,
                x.Job.CompanyId,
                x.Job.Company.Name,
                x.Job.CategoryId,
                (x.Job.Category == null ? "Not specified" : x.Job.Category.Name),
                x.ResumeStorageKey != null))
            .ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<JobApplication?> GetByIdAsync(
        Guid applicationId, CancellationToken cancellationToken = default) =>
        context.JobApplications
            .Include(x => x.User)
            .Include(x => x.Job).ThenInclude(x => x.Company)
            .Include(x => x.Job).ThenInclude(x => x.Category)
            .Include(x => x.StatusHistory).ThenInclude(x => x.ActorUser)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == applicationId, cancellationToken);
}
