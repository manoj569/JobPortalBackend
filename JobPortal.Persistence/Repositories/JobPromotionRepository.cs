using JobPortal.Application.Features.JobPromotions;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class JobPromotionRepository(JobPortalDbContext context) : IJobPromotionRepository
{
    public Task<JobPromotion?> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken) =>
        context.JobPromotions.Include(x => x.Job)
            .SingleOrDefaultAsync(x => x.JobId == jobId && !x.IsDeleted, cancellationToken);

    public async Task<(IReadOnlyCollection<JobPromotionAdminItem> Items, int Total)> SearchAsync(
        JobPromotionQuery query, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var source = context.JobPromotions.AsNoTracking().Where(x => !x.IsDeleted);
        if (query.IsEnabled.HasValue) source = source.Where(x => x.IsEnabled == query.IsEnabled.Value);
        var total = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.JobId)
            .Skip(checked((query.PageNumber - 1) * query.PageSize)).Take(query.PageSize)
            .Select(x => new JobPromotionAdminItem(
                x.JobId, x.Job.Slug, x.Job.Title, x.IsEnabled,
                PublicJobRepository.AvailableJobs(context.Jobs, nowUtc).Any(job => job.Id == x.JobId),
                x.BannerImageUrl, x.StartsAtUtc, x.EndsAtUtc, x.DisplayPriority,
                x.CreatedAtUtc, x.UpdatedAtUtc, x.CreatedByUserId, x.UpdatedByUserId))
            .ToArrayAsync(cancellationToken);
        return (items, total);
    }

    public Task<bool> IsJobEligibleAsync(Guid jobId, DateTime nowUtc, CancellationToken cancellationToken) =>
        PublicJobRepository.AvailableJobs(context.Jobs, nowUtc).AnyAsync(x => x.Id == jobId, cancellationToken);

    public async Task<IReadOnlyCollection<ActivePromotedJob>> ActiveAsync(
        DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
        await context.JobPromotions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsEnabled &&
                (!x.StartsAtUtc.HasValue || x.StartsAtUtc <= nowUtc) &&
                (!x.EndsAtUtc.HasValue || x.EndsAtUtc > nowUtc) &&
                PublicJobRepository.AvailableJobs(context.Jobs, nowUtc).Any(job => job.Id == x.JobId))
            .OrderByDescending(x => x.DisplayPriority).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.JobId)
            .Take(limit)
            .Select(x => new ActivePromotedJob(
                x.Job.Id, x.Job.Slug, x.Job.Title, x.Job.Company.Name, x.Job.Company.LogoUrl,
                x.BannerImageUrl, x.Job.Category == null ? "Not specified" : x.Job.Category.Name,
                x.Job.Location, x.Job.WorkplaceType, x.Job.EmploymentType,
                x.Job.MinimumExperienceYears, x.Job.MaximumExperienceYears, x.Job.EducationRequirement,
                x.Job.MinimumSalary, x.Job.MaximumSalary, x.Job.CurrencyCode,
                x.Job.JobSkills.Where(skill => !skill.IsDeleted && !skill.Skill.IsDeleted)
                    .OrderBy(skill => skill.Skill.Name).Select(skill => skill.Skill.Name).ToArray(),
                x.Job.PublishedAtUtc!.Value, x.DisplayPriority))
            .ToArrayAsync(cancellationToken);

    public Task<JobPromotion?> GetActiveBannerAsync(Guid jobId, DateTime nowUtc, CancellationToken cancellationToken) =>
        context.JobPromotions.AsNoTracking().Include(x => x.Job)
            .SingleOrDefaultAsync(x => x.JobId == jobId && !x.IsDeleted && x.IsEnabled &&
                x.BannerImageContent != null &&
                (!x.StartsAtUtc.HasValue || x.StartsAtUtc <= nowUtc) &&
                (!x.EndsAtUtc.HasValue || x.EndsAtUtc > nowUtc) &&
                PublicJobRepository.AvailableJobs(context.Jobs, nowUtc).Any(job => job.Id == x.JobId),
                cancellationToken);

    public async Task UpsertAsync(JobPromotion promotion, Guid actorId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var id = promotion.Id == Guid.Empty ? Guid.NewGuid() : promotion.Id;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "JobPromotions"
                ("Id", "JobId", "IsEnabled", "BannerImageUrl", "BannerImageContent", "BannerContentType",
                 "StartsAtUtc", "EndsAtUtc", "DisplayPriority", "CreatedAtUtc", "UpdatedAtUtc",
                 "IsDeleted", "DeletedAtUtc", "CreatedByUserId", "UpdatedByUserId")
            VALUES ({id}, {promotion.JobId}, {promotion.IsEnabled}, {promotion.BannerImageUrl},
                    {promotion.BannerImageContent}, {promotion.BannerContentType}, {promotion.StartsAtUtc},
                    {promotion.EndsAtUtc}, {promotion.DisplayPriority}, {now}, NULL, FALSE, NULL, {actorId}, {actorId})
            ON CONFLICT ("JobId") DO UPDATE SET
                "IsEnabled" = EXCLUDED."IsEnabled",
                "BannerImageUrl" = EXCLUDED."BannerImageUrl",
                "BannerImageContent" = EXCLUDED."BannerImageContent",
                "BannerContentType" = EXCLUDED."BannerContentType",
                "StartsAtUtc" = EXCLUDED."StartsAtUtc",
                "EndsAtUtc" = EXCLUDED."EndsAtUtc",
                "DisplayPriority" = EXCLUDED."DisplayPriority",
                "UpdatedAtUtc" = {now},
                "UpdatedByUserId" = {actorId}
            """, cancellationToken);
        context.Entry(promotion).State = EntityState.Detached;
    }

    public Task<bool> JobExistsAsync(Guid jobId, CancellationToken cancellationToken) =>
        context.Jobs.AnyAsync(x => x.Id == jobId && !x.IsDeleted, cancellationToken);
}
