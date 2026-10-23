using JobPortal.Application.Features.JobPromotions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobPromotionPostgresTests(WorkdayCheckpointDatabase database) : IClassFixture<WorkdayCheckpointDatabase>
{
    private static readonly int[] ConcurrentPriorities = [0, 5];
    [LocalPromotionPostgresFact]
    public async Task ConcurrentAdminUpsertsStayUniqueAndPublicCardUsesPersistedJobData()
    {
        var now = DateTime.UtcNow;
        await using var db = database.Open();
        var (owner, job) = await AddPublicJobAsync(db, now, "persisted-promotion-slug");
        var originalTitle = job.Title;
        var originalCategoryId = job.CategoryId;
        var first = new JobPromotion { JobId = job.Id, IsEnabled = true, StartsAtUtc = now.AddMinutes(-1) };
        var second = new JobPromotion { JobId = job.Id, IsEnabled = true, DisplayPriority = 5, StartsAtUtc = now.AddMinutes(-1) };

        await Task.WhenAll(
            UpsertOnNewContext(first, owner.Id),
            UpsertOnNewContext(second, owner.Id));

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.JobPromotions.CountAsync(x => x.JobId == job.Id));
        var saved = await db.JobPromotions.SingleAsync(x => x.JobId == job.Id);
        Assert.True(saved.IsEnabled);
        Assert.Contains(saved.DisplayPriority, ConcurrentPriorities);
        var publicDetail = await new PublicJobRepository(db, new FixedClock(now)).GetDetailsAsync(job.Slug);
        Assert.NotNull(publicDetail);
        var card = Assert.Single(await new JobPromotionRepository(db).ActiveAsync(now, 24, default));
        Assert.Equal(job.Id, card.JobId);
        Assert.Equal("persisted-promotion-slug", card.JobSlug);
        Assert.Equal(originalTitle, card.Title);
        Assert.Equal(originalCategoryId, job.CategoryId);
        Assert.Equal(originalTitle, job.Title);
        Assert.Equal("Not specified", card.JobCategory);
    }

    [LocalPromotionPostgresFact]
    public async Task ExpiredAndUnavailableJobsAreExcludedByTheSharedPublicVisibilityRule()
    {
        var now = DateTime.UtcNow;
        await using var db = database.Open();
        var (_, visible) = await AddPublicJobAsync(db, now, "shared-rule-visible");
        var (_, hidden) = await AddPublicJobAsync(db, now, "shared-rule-hidden"); hidden.IsHidden = true;
        var (_, unpublished) = await AddPublicJobAsync(db, now, "shared-rule-unpublished"); unpublished.Status = JobStatus.Draft;
        var (_, closed) = await AddPublicJobAsync(db, now, "shared-rule-closed"); closed.Status = JobStatus.Closed;
        var (_, expired) = await AddPublicJobAsync(db, now, "shared-rule-expired"); expired.ExpiresAtUtc = now.AddSeconds(-1);
        var (_, deleted) = await AddPublicJobAsync(db, now, "shared-rule-deleted"); deleted.IsDeleted = true;
        var (_, deletedCompanyJob) = await AddPublicJobAsync(db, now, "shared-rule-company-deleted"); deletedCompanyJob.Company.IsDeleted = true;
        var (_, deletedCategoryJob) = await AddPublicJobAsync(db, now, "shared-rule-category-deleted");
        deletedCategoryJob.Category!.IsDeleted = true;
        await db.SaveChangesAsync();

        var repository = new JobPromotionRepository(db);
        Assert.True(await repository.IsJobEligibleAsync(visible.Id, now, default));
        foreach (var job in new[] { hidden, unpublished, closed, expired, deleted, deletedCompanyJob, deletedCategoryJob })
        {
            Assert.False(await repository.IsJobEligibleAsync(job.Id, now, default));
            Assert.Null(await new PublicJobRepository(db, new FixedClock(now)).GetDetailsAsync(job.Slug));
        }

        var drive = new JobPortal.Domain.Entities.HiringDrive
        {
            Title = "Unchanged drive", JobId = visible.Id, StartAtUtc = now.AddMinutes(-1), IsActive = true
        };
        db.HiringDrives.Add(drive);
        await db.SaveChangesAsync();
        Assert.Single(await new HiringDriveRepository(db).PublicAsync(now, default));
    }

    [LocalPromotionPostgresFact]
    public async Task BannerBytesPersistInPostgresAndAreOnlyServedWhilePromotionAndJobArePublic()
    {
        var now = DateTime.UtcNow;
        await using var db = database.Open();
        var (owner, job) = await AddPublicJobAsync(db, now, "banner-promotion-job");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=");
        var promotion = new JobPromotion
        {
            JobId = job.Id, IsEnabled = true, StartsAtUtc = now.AddMinutes(-1),
            BannerImageUrl = $"/api/job-promotions/{job.Id:D}/banner", BannerContentType = "image/png",
            BannerImageContent = png
        };
        await new JobPromotionRepository(db).UpsertAsync(promotion, owner.Id, default);
        db.ChangeTracker.Clear();
        var banner = await new JobPromotionRepository(db).GetActiveBannerAsync(job.Id, now, default);
        Assert.NotNull(banner);
        Assert.Equal(png, banner.BannerImageContent);
        Assert.Equal("image/png", banner.BannerContentType);

        var savedJob = await db.Jobs.SingleAsync(x => x.Id == job.Id);
        savedJob.Status = JobStatus.Closed;
        await db.SaveChangesAsync();
        Assert.Null(await new JobPromotionRepository(db).GetActiveBannerAsync(job.Id, now, default));
        Assert.Empty(await new JobPromotionRepository(db).ActiveAsync(now, 24, default));
    }

    private async Task UpsertOnNewContext(JobPromotion promotion, Guid actorId)
    {
        await using var context = database.Open();
        await new JobPromotionRepository(context).UpsertAsync(promotion, actorId, default);
    }

    private static async Task<(User Owner, Job Job)> AddPublicJobAsync(JobPortalDbContext db, DateTime now, string slug)
    {
        var roleId = await db.Roles.Select(role => role.Id).FirstAsync();
        var owner = new User
        {
            Email = Guid.NewGuid().ToString("N") + "@promotion.test", FirstName = "Promotion",
            LastName = "Fixture", RoleId = roleId
        };
        var company = new Company
        {
            Name = "Promotion " + Guid.NewGuid().ToString("N"), Slug = Guid.NewGuid().ToString("N"), OwnerUser = owner
        };
        var category = new Category
        {
            Name = "Promotion category " + Guid.NewGuid().ToString("N"), Slug = Guid.NewGuid().ToString("N")
        };
        var job = new Job
        {
            ReferenceNumber = slug, Slug = slug, Title = "Promoted title", Description = "Persisted description",
            ApplicationUrl = "https://jobs.example.test/apply", Status = JobStatus.Published,
            PublishedAtUtc = now.AddDays(-1), ExpiresAtUtc = now.AddDays(1), Company = company, Category = category
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return (owner, job);
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class LocalPromotionPostgresFactAttribute : FactAttribute
    {
        public LocalPromotionPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires explicitly disposable loopback PostgreSQL fixture; production configuration is never used.";
        }
    }
}
