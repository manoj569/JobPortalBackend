using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobPromotions;
using JobPortal.API.Middleware;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobPromotionTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] ExpectedSkills = ["C#", "SQL"];

    [Fact]
    public async Task EligibleJobCanBePromotedWithoutChangingTheOriginalJob()
    {
        var job = Job("persisted-public-slug");
        var repo = new PromotionRepositoryFake(job);
        var service = Service(repo);
        var actor = Guid.NewGuid();

        var saved = await service.SaveAsync(actor, job.Id,
            new(true, Now.AddHours(-1), Now.AddDays(2), 3), default);

        Assert.True(saved.IsEnabled);
        Assert.Equal("persisted-public-slug", saved.JobSlug);
        Assert.Equal(job.Id, saved.JobId);
        Assert.Equal(actor, repo.Promotion!.CreatedByUserId);
        Assert.Equal(3, repo.Promotion.DisplayPriority);
        Assert.Equal("persisted-public-slug", job.Slug);
        Assert.Equal(JobStatus.Published, job.Status);
        Assert.True(job.IsFeatured);
        Assert.Equal("Original category", job.Category!.Name);
        Assert.Equal("Original description", job.Description);
    }

    [Fact]
    public async Task IneligibleJobCannotBeEnabled()
    {
        var repo = new PromotionRepositoryFake(Job("closed-job")) { Eligible = false };
        var service = Service(repo);

        await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(Guid.NewGuid(),
            repo.Job.Id, new(true), default));

        Assert.Null(repo.Promotion);
        Assert.Equal(JobStatus.Published, repo.Job.Status);
    }

    [Fact]
    public async Task ActiveCardReturnsTheLinkedJobsPersistedSlugAndExistingDetailsRoute()
    {
        var repo = new PromotionRepositoryFake(Job("authoritative-slug"));
        repo.ActiveJobs = [new ActivePromotedJob(repo.Job.Id, repo.Job.Slug, repo.Job.Title,
            "Original company", "https://careerharbor.in/logo.png", null, "Original category",
            "Pune", WorkplaceType.Hybrid, EmploymentType.FullTime, 1, 3, null, null, null,
            null, ["C#", "SQL"], Now.AddDays(-1), 2)];

        var item = Assert.Single(await Service(repo).ActiveAsync(default));

        Assert.Equal(repo.Job.Id, item.JobId);
        Assert.Equal(repo.Job.Slug, item.JobSlug);
        Assert.Equal("/dashboard/jobs/authoritative-slug", item.DetailsUrl);
        Assert.Equal("Original category", item.JobCategory);
        Assert.Equal(ExpectedSkills, item.Skills);
        Assert.Null(item.EducationRequirement);
        Assert.Null(item.MinimumSalary);
        Assert.Null(item.MaximumSalary);
    }

    [Fact]
    public async Task InvalidBannerContentIsRejectedAndValidPngIsStoredInThePromotion()
    {
        var repo = new PromotionRepositoryFake(Job("banner-job"))
        {
            Promotion = new JobPromotion { JobId = Guid.NewGuid(), Job = Job("banner-job") }
        };
        repo.Promotion.JobId = repo.Job.Id;
        repo.Promotion.Job = repo.Job;
        var service = Service(repo);

        using var invalid = new MemoryStream("<script>bad</script>"u8.ToArray());
        await Assert.ThrowsAsync<BadRequestException>(() => service.UploadBannerAsync(Guid.NewGuid(),
            repo.Job.Id, new(invalid, invalid.Length, "banner.png", "image/png"), default));

        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=");
        using var valid = new MemoryStream(png);
        var saved = await service.UploadBannerAsync(Guid.NewGuid(), repo.Job.Id,
            new(valid, valid.Length, "../../banner.png", "image/png"), default);
        Assert.Equal($"/api/job-promotions/{repo.Job.Id:D}/banner", saved.BannerImageUrl);
        Assert.Equal(png, repo.Promotion.BannerImageContent);
        Assert.Equal("image/png", repo.Promotion.BannerContentType);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public async Task PublicBannerReturnsStoredImageWithCrossOriginResourcePolicy(string contentType)
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var service = new BannerService(new JobPromotionBanner(bytes, contentType));
        var controller = new JobPromotionsController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var result = await controller.Banner(Guid.NewGuid(), default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(bytes, file.FileContents);
        Assert.Equal(contentType, file.ContentType);
        Assert.Equal("cross-origin", controller.Response.Headers["Cross-Origin-Resource-Policy"]);
    }

    [Fact]
    public async Task MissingPublicBannerKeepsNotFoundBehaviorAndDoesNotSetBannerPolicy()
    {
        var controller = new JobPromotionsController(new BannerService(null,
            new NotFoundException("Promotion banner was not found.")))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        await Assert.ThrowsAsync<NotFoundException>(() => controller.Banner(Guid.NewGuid(), default));

        Assert.False(controller.Response.Headers.ContainsKey("Cross-Origin-Resource-Policy"));
    }

    [Fact]
    public async Task SecurityHeadersMiddlewareRetainsSameSitePolicyForOtherEndpoints()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/private/profile";
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, new TestHostEnvironment());

        await middleware.InvokeAsync(context);

        Assert.Equal("same-site", context.Response.Headers["Cross-Origin-Resource-Policy"]);
    }

    [Fact]
    public void PromotionDatesAndPriorityAreValidated()
    {
        var validator = new SaveJobPromotionValidator();
        Assert.True(validator.Validate(new SaveJobPromotionRequest(true, Now, Now.AddMinutes(1), 0)).IsValid);
        Assert.False(validator.Validate(new SaveJobPromotionRequest(true, Now, Now, 0)).IsValid);
        Assert.False(validator.Validate(new SaveJobPromotionRequest(true, Now.AddDays(1), Now, 0)).IsValid);
        Assert.False(validator.Validate(new SaveJobPromotionRequest(true, DisplayPriority: -1)).IsValid);
    }

    [Fact]
    public async Task PromotionEligibilityMatchesPublicJobDetailVisibility()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new JobPortalDbContext(options);
        var visible = Job("visible-slug");
        var hidden = Job("hidden-slug"); hidden.IsHidden = true;
        var unpublished = Job("draft-slug"); unpublished.Status = JobStatus.Draft;
        var closed = Job("closed-slug"); closed.Status = JobStatus.Closed;
        var expired = Job("expired-slug"); expired.ExpiresAtUtc = Now.AddSeconds(-1);
        var deleted = Job("deleted-slug"); deleted.IsDeleted = true;
        var deletedCompany = Job("deleted-company-slug"); deletedCompany.Company.IsDeleted = true;
        var deletedCategory = Job("deleted-category-slug"); deletedCategory.Category!.IsDeleted = true;
        db.Jobs.AddRange(visible, hidden, unpublished, closed, expired, deleted, deletedCompany, deletedCategory);
        await db.SaveChangesAsync();

        var promotionRepository = new JobPromotionRepository(db);
        var publicJobs = new PublicJobRepository(db, new FixedClock(Now));
        foreach (var job in new[] { visible, hidden, unpublished, closed, expired, deleted, deletedCompany, deletedCategory })
        {
            var promotable = await promotionRepository.IsJobEligibleAsync(job.Id, Now, default);
            var publicDetails = await publicJobs.GetDetailsAsync(job.Slug, default);
            Assert.Equal(publicDetails is not null, promotable);
        }
        Assert.True(await promotionRepository.IsJobEligibleAsync(visible.Id, Now, default));
        Assert.False(await promotionRepository.IsJobEligibleAsync(closed.Id, Now, default));
    }

    [Fact]
    public void AdminRoutesRequireAdministratorAndPublicRouteIsAnonymousReadOnly()
    {
        var admin = Assert.Single(typeof(AdminJobPromotionsController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("Administrator", admin.Roles);
        Assert.Single(typeof(JobPromotionsController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.Equal(0, typeof(JobPromotionsController).GetMethods().Count(method =>
            method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), true).Length != 0 ||
            method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPutAttribute), true).Length != 0));
    }

    private static JobPromotionService Service(PromotionRepositoryFake repository) => new(
        repository, new NoOpUnitOfWork(), new NoOpAuditWriter(), new FixedClock(Now),
        new SaveJobPromotionValidator(), new JobPromotionQueryValidator());

    private static Job Job(string slug) => new()
    {
        Id = Guid.NewGuid(), ReferenceNumber = slug, Slug = slug, Title = "Original job",
        Description = "Original description", ApplicationUrl = "https://example.test/apply",
        Status = JobStatus.Published, PublishedAtUtc = Now.AddDays(-1), ExpiresAtUtc = Now.AddDays(1),
        IsFeatured = true,
        Company = new Company { Id = Guid.NewGuid(), Name = "Original company", Slug = "original-company", OwnerUserId = Guid.NewGuid() },
        Category = new Category { Id = Guid.NewGuid(), Name = "Original category", Slug = "original-category" }
    };

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
    private sealed class BannerService(JobPromotionBanner? banner, Exception? exception = null) : IJobPromotionService
    {
        public Task<JobPromotionBanner> BannerAsync(Guid jobId, CancellationToken cancellationToken) =>
            exception is not null ? Task.FromException<JobPromotionBanner>(exception) : Task.FromResult(banner!);
        public Task<JobPromotionConfigurationResponse> SaveAsync(Guid actorId, Guid jobId, SaveJobPromotionRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<JobPromotionConfigurationResponse> GetAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PagedResponse<JobPromotionConfigurationResponse>> SearchAsync(JobPromotionQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<JobPromotionConfigurationResponse> DisableAsync(Guid actorId, Guid jobId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<JobPromotionConfigurationResponse> UploadBannerAsync(Guid actorId, Guid jobId, JobPromotionBannerUpload upload, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<PromotedJobResponse>> ActiveAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }
    private sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "JobPortal.Application.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class PromotionRepositoryFake(Job job) : IJobPromotionRepository
    {
        public Job Job { get; } = job;
        public JobPromotion? Promotion { get; set; }
        public bool Eligible { get; set; } = true;
        public IReadOnlyCollection<ActivePromotedJob> ActiveJobs { get; set; } = [];
        public Task<JobPromotion?> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(Promotion?.JobId == jobId ? Promotion : null);
        public Task<(IReadOnlyCollection<JobPromotionAdminItem> Items, int Total)> SearchAsync(JobPromotionQuery query, DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<(IReadOnlyCollection<JobPromotionAdminItem>, int)>((Promotion is null ? [] : [new(
                Job.Id, Job.Slug, Job.Title, Promotion.IsEnabled, Eligible, Promotion.BannerImageUrl,
                Promotion.StartsAtUtc, Promotion.EndsAtUtc, Promotion.DisplayPriority,
                Promotion.CreatedAtUtc, Promotion.UpdatedAtUtc, Promotion.CreatedByUserId, Promotion.UpdatedByUserId)], Promotion is null ? 0 : 1));
        public Task<bool> IsJobEligibleAsync(Guid jobId, DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Eligible && jobId == Job.Id);
        public Task<IReadOnlyCollection<ActivePromotedJob>> ActiveAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(ActiveJobs);
        public Task<JobPromotion?> GetActiveBannerAsync(Guid jobId, DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Promotion?.JobId == jobId ? Promotion : null);
        public Task UpsertAsync(JobPromotion promotion, Guid actorId, CancellationToken cancellationToken)
        {
            Promotion = promotion;
            Promotion.Job = Job;
            Promotion.CreatedAtUtc = Now;
            return Task.CompletedTask;
        }
        public Task<bool> JobExistsAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult(jobId == Job.Id);
    }
}
