using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

// Real repository/projection tests with synthetic data. No network or database connections.
public sealed class JobProviderIntegrationAuditTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublicAndSavedCountsExcludeSoftDeletedRequiredReferences(bool deleteCompany)
    {
        using var f = new JobSourceFixture();
        var job = Job(f, "deleted-reference");
        job.Status = JobStatus.Published;
        job.PublishedAtUtc = JobSourceFixture.Now;
        var userId = Guid.NewGuid();
        f.Context.Add(job);
        f.Context.SavedJobs.Add(new SavedJob { UserId = userId, Job = job, JobId = job.Id });
        if (deleteCompany) f.Company.IsDeleted = true;
        else f.Category.IsDeleted = true;
        await f.Context.SaveChangesAsync();
        var publicPage = await new PublicJobRepository(f.Context, new Clock()).SearchAsync(new());
        var savedPage = await new DashboardRepository(f.Context, new Clock()).GetSavedJobsAsync(userId, new());
        Assert.Equal(0, publicPage.TotalCount);
        Assert.Empty(publicPage.Items);
        Assert.Equal(0, savedPage.TotalCount);
        Assert.Empty(savedPage.Items);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task PublicReferralProjectionNeverDisclosesPrivateNameOrUnapprovedMetadata(bool approved, bool active)
    {
        await using var f = await ReferralFixture.CreateAsync();
        var referral = await f.Db.JobReferrals.Include(x => x.ReferrerUser).SingleAsync();
        referral.ApprovalStatus = approved ? JobReferralApprovalStatus.Approved : JobReferralApprovalStatus.Pending;
        referral.ReferrerUser.Status = active ? UserStatus.Active : UserStatus.Suspended;
        await f.Db.SaveChangesAsync();
        var repository = new PublicJobRepository(f.Db, f.Clock);
        var card = Assert.Single((await repository.SearchAsync(new())).Items);
        Assert.Equal(approved && active, card.IsReferralJob);
        Assert.Equal(approved && active ? "Employee Referrer" : null, card.ReferrerName);
        Assert.Equal(approved && active ? f.ReferralId : (Guid?)null, card.ReferralId);
        var json = System.Text.Json.JsonSerializer.Serialize(card);
        Assert.DoesNotContain("Private Referrer", json);
        Assert.DoesNotContain("employee@example.test", json);
        var filtered = await repository.SearchAsync(new(ReferralOnly: true, Search: "Software", CompanyId: card.CompanyId));
        Assert.Equal(approved && active ? 1 : 0, filtered.TotalCount);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("deleted")]
    [InlineData("disabled")]
    [InlineData("wrong-company")]
    public async Task PublicationNeedsNoApprovalButStillValidatesSourceOwnershipAndState(string condition)
    {
        using var f = new JobSourceFixture();
        var clock = new Clock();
        if (condition == "deleted") f.Source.IsDeleted = true;
        if (condition == "disabled") f.Source.IsActive = false;
        if (condition == "wrong-company") f.Source.CompanyId = Guid.NewGuid();
        var job = Job(f, "unapproved");
        f.Context.Jobs.Add(job);
        await f.Context.SaveChangesAsync();
        var service = Service(f, clock);
        if (condition != "valid")
        {
            var error = await Assert.ThrowsAsync<BadRequestException>(() => service.PublishAsync(job.Id));
            Assert.Equal("invalid_job_source", error.Code);
            Assert.Equal(JobStatus.Draft, (await f.Context.Jobs.AsNoTracking().SingleAsync()).Status);
        }
        else Assert.Equal(JobStatus.Published, (await service.PublishAsync(job.Id)).Status);
    }

    [Fact]
    public async Task SourceAndManualJobsCanBePublishedAndUnhiddenWithoutApproval()
    {
        using var f = new JobSourceFixture();
        var clock = new Clock();
        var first = Job(f, "first");
        var second = Job(f, "second");
        var manual = Job(f, "manual");
        manual.JobSourceId = null;
        f.Context.Jobs.AddRange(first, second, manual);
        await f.Context.SaveChangesAsync();
        var service = Service(f, clock);
        Assert.Equal(JobStatus.Published, (await service.PublishAsync(first.Id)).Status);
        clock.Now = clock.Now.AddDays(1);
        Assert.Equal(JobStatus.Published, (await service.PublishAsync(second.Id)).Status);
        Assert.Equal(JobStatus.Published, (await service.PublishAsync(manual.Id)).Status);
        first.IsHidden = true;
        await f.Context.SaveChangesAsync();
        await service.SetHiddenAsync(first.Id, false);
        f.Context.ChangeTracker.Clear();
        Assert.False((await f.Context.Jobs.SingleAsync(x => x.Id == first.Id)).IsHidden);
        Assert.Equal(JobStatus.Published, (await f.Context.Jobs.SingleAsync(x => x.Id == second.Id)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://assets.example.test/company.png")]
    public async Task SavedProjectionPagesOnlyOwnedAvailableJobsWithSharedCompanyLogo(string? logo)
    {
        using var f = new JobSourceFixture();
        f.Company.LogoUrl = logo;
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var jobs = Enumerable.Range(0, 7).Select(i => Job(f, $"saved-{i}")).ToArray();
        foreach (var job in jobs) { job.Status = JobStatus.Published; job.PublishedAtUtc = JobSourceFixture.Now; }
        jobs[2].IsHidden = true;
        jobs[3].ExpiresAtUtc = JobSourceFixture.Now;
        jobs[4].Status = JobStatus.Closed;
        jobs[5].PublishedAtUtc = null;
        f.Context.AddRange(jobs);
        f.Context.SavedJobs.AddRange(jobs.Select((job, i) => new SavedJob
        { UserId = i == 6 ? other : userId, JobId = job.Id, Job = job, CreatedAtUtc = JobSourceFixture.Now }));
        await f.Context.SaveChangesAsync();
        var repository = new DashboardRepository(f.Context, new Clock());
        var first = await repository.GetSavedJobsAsync(userId, new(1, 1));
        var second = await repository.GetSavedJobsAsync(userId, new(2, 1));
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        var a = Assert.Single(first.Items);
        var b = Assert.Single(second.Items);
        Assert.NotEqual(a.Job.Id, b.Job.Id);
        Assert.All(new[] { a, b }, dto =>
        {
            Assert.Equal(f.Company.Id, dto.Job.CompanyId);
            Assert.Equal(f.Company.Name, dto.Job.CompanyName);
            Assert.Equal(logo, dto.Job.CompanyLogoUrl);
        });
        Assert.Empty((await repository.GetSavedJobsAsync(userId, new(3, 1))).Items);
        Assert.Single((await repository.GetSavedJobsAsync(other, new())).Items);
    }

    [Fact]
    public async Task ReferralCountsPagesAndExpiryBoundaryMatchPublicVisibility()
    {
        await using var f = await ReferralFixture.CreateAsync();
        var ids = new List<Guid> { f.ReferralId };
        for (var i = 0; i < 6; i++) ids.Add(await f.AddOpportunity(5));
        var rows = await f.Db.JobReferrals.Include(x => x.Job).ToArrayAsync();
        foreach (var row in rows) row.CreatedAtUtc = f.Clock.Utc;
        rows.Single(x => x.Id == ids[2]).Job.PublishedAtUtc = null;
        rows.Single(x => x.Id == ids[3]).Job.ExpiresAtUtc = f.Clock.Utc;
        rows.Single(x => x.Id == ids[4]).Job.IsHidden = true;
        rows.Single(x => x.Id == ids[5]).ApprovalStatus = JobReferralApprovalStatus.Pending;
        rows.Single(x => x.Id == ids[6]).Job.Status = JobStatus.Closed;
        await f.Db.SaveChangesAsync();
        var repository = new JobReferralRepository(f.Db, f.Clock);
        var first = await repository.GetApprovedAsync(1, 1);
        var second = await repository.GetApprovedAsync(2, 1);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        var ordered = new[] { ids[0], ids[1] }.Order().ToArray();
        Assert.Equal(ordered[0], Assert.Single(first.Items).Id);
        Assert.Equal(ordered[1], Assert.Single(second.Items).Id);
        Assert.Empty((await repository.GetApprovedAsync(3, 1)).Items);
        var submissions = await repository.GetByReferrerAsync(f.ReferrerId, null, null, 1, 2);
        Assert.Equal(7, submissions.TotalCount);
        Assert.Equal(ids.Order().Take(2), submissions.Items.Select(x => x.Id));
    }

    private static Job Job(JobSourceFixture f, string slug) => new()
    {
        Company = f.Company, CompanyId = f.Company.Id, Category = f.Category, CategoryId = f.Category.Id,
        JobSourceId = f.Source.Id, ExternalJobId = slug, Title = slug, Slug = slug, ReferenceNumber = slug,
        Description = "Build reliable software.", ApplicationUrl = "https://example.test/apply", CurrencyCode = "INR",
        EmploymentType = EmploymentType.FullTime, WorkplaceType = WorkplaceType.Remote, ExperienceLevel = ExperienceLevel.Mid,
        ExpiresAtUtc = JobSourceFixture.Now.AddDays(10)
    };
    private static JobService Service(JobSourceFixture f, Clock clock) => new(
        new JobRepository(f.Context), new UnitOfWork(f.Context), f.Audit, new CreateJobRequestValidator(),
        new UpdateJobRequestValidator(), new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(),
        clock, sources: f.Repository);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(JobSourceFixture.Now);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
