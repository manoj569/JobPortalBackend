using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AdminApplications;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Application.Features.Candidates;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class AdminJobLifecycleTests
{
    private static readonly DateTime Now =
        new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PublishRequiresActiveReferencesAndFutureExpiry()
    {
        var fixture = CreateFixture();
        fixture.Job.ExpiresAtUtc = null;
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PublishAsync(fixture.Job.Id));

        fixture.Job.ExpiresAtUtc = Now.AddDays(1);
        fixture.Repository.CompanyExists = false;
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PublishAsync(fixture.Job.Id));

        fixture.Repository.CompanyExists = true;
        fixture.Repository.CategoryExists = false;
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PublishAsync(fixture.Job.Id));

        fixture.Repository.CategoryExists = true;
        var response = await fixture.Service.PublishAsync(fixture.Job.Id);

        Assert.Equal(JobStatus.Published, response.Status);
        Assert.Equal(Now, response.PublishedAtUtc);
        Assert.False(response.IsHidden);
        Assert.False(response.IsFeatured);
        Assert.Contains(
            fixture.Audit.Events,
            audit => audit.Action == AuditAction.Publish &&
                audit.EntityId == fixture.Job.Id.ToString());
    }

    [Fact]
    public async Task PublishRejectsExpiredAndArchivedJobs()
    {
        var fixture = CreateFixture();
        fixture.Job.ExpiresAtUtc = Now;
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PublishAsync(fixture.Job.Id));

        fixture.Job.Status = JobStatus.Archived;
        fixture.Job.ExpiresAtUtc = Now.AddDays(1);
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.PublishAsync(fixture.Job.Id));
    }

    [Fact]
    public async Task CloseAndUnpublishFollowExplicitTransitions()
    {
        var closeFixture = CreateFixture(JobStatus.Published);
        closeFixture.Job.IsFeatured = true;
        var closed = await closeFixture.Service.CloseAsync(closeFixture.Job.Id);

        Assert.Equal(JobStatus.Closed, closed.Status);
        Assert.False(closed.IsFeatured);
        await Assert.ThrowsAsync<ConflictException>(() =>
            closeFixture.Service.CloseAsync(closeFixture.Job.Id));

        var unpublishFixture = CreateFixture(JobStatus.Published);
        unpublishFixture.Job.IsFeatured = true;
        var draft = await unpublishFixture.Service.UnpublishAsync(
            unpublishFixture.Job.Id);

        Assert.Equal(JobStatus.Draft, draft.Status);
        Assert.Null(draft.PublishedAtUtc);
        Assert.False(draft.IsFeatured);
    }

    [Fact]
    public async Task ArchiveIsFinalAndFeatureRequiresVisibleUnexpiredPublication()
    {
        var fixture = CreateFixture(JobStatus.Published);
        fixture.Job.IsHidden = true;
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.SetFeaturedAsync(fixture.Job.Id, true));

        fixture.Job.IsHidden = false;
        fixture.Job.ExpiresAtUtc = Now;
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.SetFeaturedAsync(fixture.Job.Id, true));

        fixture.Job.ExpiresAtUtc = Now.AddDays(1);
        var featured = await fixture.Service.SetFeaturedAsync(fixture.Job.Id, true);
        Assert.True(featured.IsFeatured);

        var archived = await fixture.Service.ArchiveAsync(fixture.Job.Id);
        Assert.Equal(JobStatus.Archived, archived.Status);
        Assert.False(archived.IsFeatured);
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.PublishAsync(fixture.Job.Id));
    }

    [Fact]
    public async Task PublishedUpdateCannotRemoveFutureExpiryAndArchiveCannotBeEdited()
    {
        var fixture = CreateFixture(JobStatus.Published);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.UpdateAsync(fixture.Job.Id, Request(fixture.Job, null)));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.UpdateAsync(
                fixture.Job.Id, Request(fixture.Job, Now.AddSeconds(-1))));

        fixture.Job.Status = JobStatus.Archived;
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.UpdateAsync(
                fixture.Job.Id, Request(fixture.Job, Now.AddDays(2))));
    }

    [Fact]
    public async Task SearchPreservesExpiryFiltersAndPagination()
    {
        var fixture = CreateFixture();
        var query = new JobSearchQuery(
            PageNumber: 3,
            PageSize: 10,
            CompanyId: fixture.Job.CompanyId,
            CategoryId: fixture.Job.CategoryId,
            Status: JobStatus.Draft,
            IsFeatured: false,
            ExpiresFromUtc: Now,
            ExpiresToUtc: Now.AddDays(30));
        fixture.Repository.SearchItems = [fixture.Job];
        fixture.Repository.TotalCount = 25;

        var response = await fixture.Service.SearchAsync(query);

        Assert.Same(query, fixture.Repository.LastQuery);
        Assert.Equal(3, response.PageNumber);
        Assert.Equal(10, response.PageSize);
        Assert.Equal(25, response.TotalCount);
    }

    [Fact]
    public async Task ComposeSavesOptionalRecruiterContactWithTheDraftAggregate()
    {
        var company = new Company { Id = Guid.NewGuid(), Name = "Acme", Slug = "acme" };
        var category = new Category { Id = Guid.NewGuid(), Name = "Engineering", Slug = "engineering" };
        var jobs = new JobRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();
        var service = new JobService(jobs, unitOfWork, new AuditWriterTestDouble(),
            new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
            new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(),
            new FixedTimeProvider(Now), new CompanyManagementRepositoryFake(company),
            new CategoryManagementRepositoryFake(category));

        var response = await service.ComposeAsync(Guid.NewGuid(), new(
            new("Platform Engineer"), new(ExistingId: company.Id),
            new(ExistingId: category.Id),
            new("Jane", "Talent Partner", "jane@example.test", "+91 99999 99999", true)));

        Assert.True(response.RecruiterContactCreated);
        var job = Assert.IsType<Job>(jobs.AddedJob);
        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Equal("jane@example.test", job.RecruiterContact?.Email);
        Assert.True(job.RecruiterContact?.IsSharingApproved);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    private static Fixture CreateFixture(JobStatus status = JobStatus.Draft)
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = "Acme",
            Slug = "acme",
            OwnerUserId = Guid.NewGuid()
        };
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Engineering",
            Slug = "engineering"
        };
        var job = new Job
        {
            Id = Guid.NewGuid(),
            ReferenceNumber = "JOB-42",
            Title = "Platform Engineer",
            Slug = "platform-engineer",
            Description = "Build secure services.",
            ApplicationUrl = "https://example.test/apply",
            CompanyId = company.Id,
            Company = company,
            CategoryId = category.Id,
            Category = category,
            CurrencyCode = "INR",
            EmploymentType = EmploymentType.FullTime,
            WorkplaceType = WorkplaceType.Remote,
            ExperienceLevel = ExperienceLevel.Mid,
            Status = status,
            ExpiresAtUtc = Now.AddDays(10),
            PublishedAtUtc = status == JobStatus.Published ? Now.AddDays(-1) : null
        };
        var repository = new JobRepositoryFake { Job = job };
        var audit = new AuditWriterTestDouble();
        var service = new JobService(
            repository,
            new UnitOfWorkFake(),
            audit,
          new CreateJobRequestValidator(),
new UpdateJobRequestValidator(),
new UpdateRecruiterContactRequestValidator(),
new JobSearchQueryValidator(),
            new FixedTimeProvider(Now));
        return new(service, repository, job, audit);
    }

    private static UpdateJobRequest Request(Job job, DateTime? expiresAtUtc) => new(
        job.Title,
        job.Description,
        job.CompanyId,
        job.CategoryId,
        job.ApplicationUrl,
        job.Responsibilities,
        job.Requirements,
        job.Benefits,
        job.Location,
        job.MinimumSalary,
        job.MaximumSalary,
        job.CurrencyCode,
        job.EmploymentType,
        job.WorkplaceType,
        job.ExperienceLevel,
        expiresAtUtc);

    private sealed record Fixture(
        JobService Service,
        JobRepositoryFake Repository,
        Job Job,
        AuditWriterTestDouble Audit);

    private sealed class JobRepositoryFake : IJobRepository
    {
        public Job? Job { get; init; }
        public Job? AddedJob { get; private set; }
        public bool CompanyExists { get; set; } = true;
        public bool CategoryExists { get; set; } = true;
        public IReadOnlyCollection<Job> SearchItems { get; set; } = [];
        public int TotalCount { get; set; }
        public JobSearchQuery? LastQuery { get; private set; }

        public Task<Job?> GetByIdAsync(
            Guid id,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Job?.Id == id ? Job : null);

        public Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(
            JobSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult((SearchItems, TotalCount));
        }

        public Task<bool> CompanyExistsAsync(
            Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CompanyExists);

        public Task<bool> CategoryExistsAsync(
            Guid categoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CategoryExists);

        public Task<int> ExpireOverduePublishedAsync(
            DateTime utcNow, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task AddAsync(
            Job job, CancellationToken cancellationToken = default)
        {
            AddedJob = job;
            return Task.CompletedTask;
        }

        public void Update(Job job)
        {
        }

        public void Remove(Job job) => job.IsDeleted = true;

        public Task DeletePermanentlyAsync(
            Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class UnitOfWorkFake : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class CompanyManagementRepositoryFake(Company company) : ICompanyManagementRepository
    {
        public Task<Company?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Company?>(id == company.Id ? company : null);
        public Task<(IReadOnlyCollection<CompanyResponse> Items, int TotalCount)> SearchAsync(CompanySearchQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CompanyResponse?> GetResponseAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SlugExistsAsync(string slug, Guid? excludingId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasJobsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Company value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Remove(Company value) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CategoryManagementRepositoryFake(Category category) : ICategoryManagementRepository
    {
        public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Category?>(id == category.Id ? category : null);
        public Task<(IReadOnlyCollection<CategoryResponse> Items, int TotalCount)> SearchAsync(CategorySearchQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CategoryResponse?> GetResponseAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SlugExistsAsync(string slug, Guid? excludingId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsDescendantAsync(Guid categoryId, Guid possibleDescendantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasChildrenOrJobsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Category value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Remove(Category value) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}

public sealed class JobLifecycleVisibilityTests
{
    private static readonly DateTime Now =
        new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AdminSearchCombinesLifecycleFiltersAndPaginates()
    {
        await using var context = CreateContext();
        var seed = Seed(context);
        await context.SaveChangesAsync();
        var repository = new JobRepository(context);

        var (filtered, filteredCount) = await repository.SearchAsync(new(
            PageSize: 100,
            Search: "Visible",
            CompanyId: seed.Visible.CompanyId,
            CategoryId: seed.Visible.CategoryId,
            Status: JobStatus.Published,
            IsFeatured: true,
            ExpiresFromUtc: Now.AddDays(1),
            ExpiresToUtc: Now.AddDays(20)));
        var (page, totalCount) = await repository.SearchAsync(new(
            PageNumber: 2,
            PageSize: 3));

        Assert.Equal(1, filteredCount);
        Assert.Equal(seed.Visible.Id, Assert.Single(filtered).Id);
        Assert.Equal(seed.All.Count, totalCount);
        Assert.Equal(3, page.Count);
    }

    [Fact]
    public async Task PublicAndRelatedQueriesReturnOnlyAvailableJobs()
    {
        await using var context = CreateContext();
        var seed = Seed(context);
        await context.SaveChangesAsync();
        var repository = new PublicJobRepository(
            context, new FixedTimeProvider(Now));

        var (items, totalCount) = await repository.SearchAsync(
            new PublicJobQuery(PageSize: 100));
        var related = await repository.GetRelatedAsync(seed.Visible.Slug, 20);

        Assert.Equal(3, totalCount);
        Assert.All(items, item =>
            Assert.Contains(
                item.Id,
                new[] { seed.Visible.Id, seed.RelatedVisible.Id, seed.NoExpiry.Id }));
        Assert.Equal(
            new[] { seed.RelatedVisible.Id, seed.NoExpiry.Id }.Order(),
            related.Select(item => item.Id).Order());

        var (featured, featuredCount) = await repository.SearchAsync(
            new PublicJobQuery(PageSize: 100, IsFeatured: true));
        Assert.Equal(2, featuredCount);
        Assert.Equal(
            new[] { seed.Visible.Id, seed.NoExpiry.Id }.Order(),
            featured.Select(item => item.Id).Order());
    }

    [Fact]
    public async Task SavedJobsHideUnavailableJobs()
    {
        await using var context = CreateContext();
        var seed = Seed(context);
        var candidate = Candidate("saved@example.test");
        context.Users.Add(candidate);
        foreach (var job in seed.All)
            context.SavedJobs.Add(new SavedJob
            {
                UserId = candidate.Id,
                User = candidate,
                JobId = job.Id,
                Job = job
            });
        await context.SaveChangesAsync();
        var repository = new DashboardRepository(
            context, new FixedTimeProvider(Now));

        var (items, totalCount) = await repository.GetSavedJobsAsync(
            candidate.Id, new DashboardQuery(1, 100));

        Assert.Equal(3, totalCount);
        Assert.All(items, item =>
            Assert.Contains(
                item.Job.Id,
                new[] { seed.Visible.Id, seed.RelatedVisible.Id, seed.NoExpiry.Id }));
        foreach (var unavailable in seed.All.Except(
                     new[] { seed.Visible, seed.RelatedVisible, seed.NoExpiry }))
            Assert.False(await repository.IsAvailableJobAsync(unavailable.Id));
    }

    [Fact]
    public async Task ClosedAndExpiredJobsBlockNewApplicationsButKeepExistingOnesVisible()
    {
        await using var context = CreateContext();
        var seed = Seed(context);
        var candidate = Candidate("applicant@example.test");
        context.Users.Add(candidate);
        var closedApplication = Application(candidate, seed.Closed);
        var expiredApplication = Application(candidate, seed.Expired);
        context.JobApplications.AddRange(closedApplication, expiredApplication);
        await context.SaveChangesAsync();
        var candidateRepository = new CandidateRepository(
            context, new FixedTimeProvider(Now));
        var adminRepository = new AdminApplicationRepository(context);

        Assert.Null(await candidateRepository.GetAvailableJobAsync(seed.Closed.Id));
        Assert.Null(await candidateRepository.GetAvailableJobAsync(seed.Expired.Id));
        Assert.Null(await candidateRepository.GetAvailableJobAsync(seed.Overdue.Id));
        Assert.Null(await candidateRepository.GetAvailableJobAsync(seed.Archived.Id));

        var (candidateItems, candidateCount) =
            await candidateRepository.GetApplicationsAsync(
                candidate.Id, new JobApplicationQuery(1, 20));
        var (adminItems, adminCount) = await adminRepository.SearchAsync(
            new AdminApplicationQuery(PageSize: 20));

        Assert.Equal(2, candidateCount);
        Assert.Equal(2, candidateItems.Count);
        Assert.Equal(2, adminCount);
        Assert.Equal(2, adminItems.Count);
    }

    private static JobApplication Application(User candidate, Job job) => new()
    {
        UserId = candidate.Id,
        User = candidate,
        JobId = job.Id,
        Job = job,
        Status = JobApplicationStatus.Submitted,
        SubmittedAtUtc = Now.AddDays(-2)
    };

    private static SeedResult Seed(JobPortalDbContext context)
    {
        var ownerId = Guid.NewGuid();
        var company = new Company
        {
            Name = "Acme",
            Slug = "acme",
            OwnerUserId = ownerId
        };
        var category = new Category
        {
            Name = "Engineering",
            Slug = "engineering"
        };
        var visible = Job(
            company, category, "Visible", JobStatus.Published, Now.AddDays(10), true);
        var relatedVisible = Job(
            company, category, "Related", JobStatus.Published, Now.AddDays(5));
        var closed = Job(
            company, category, "Closed", JobStatus.Closed, Now.AddDays(4), true);
        var expired = Job(
            company, category, "Expired", JobStatus.Expired, Now.AddDays(-1), true);
        var overdue = Job(
            company, category, "Overdue", JobStatus.Published, Now, true);
        var archived = Job(
            company, category, "Archived", JobStatus.Archived, Now.AddDays(3), true);
        var draft = Job(
            company, category, "Draft", JobStatus.Draft, Now.AddDays(3), true);
        var noExpiry = Job(
            company, category, "No Expiry", JobStatus.Published, null, true);
        context.AddRange(
            company, category, visible, relatedVisible, closed, expired,
            overdue, archived, draft, noExpiry);
        return new(
            visible,
            relatedVisible,
            closed,
            expired,
            overdue,
            archived,
            noExpiry,
            [visible, relatedVisible, closed, expired, overdue, archived, draft, noExpiry]);
    }

    private static Job Job(
        Company company,
        Category category,
        string title,
        JobStatus status,
        DateTime? expiresAtUtc,
        bool featured = false) => new()
        {
            ReferenceNumber = $"JOB-{title}",
            Title = title,
            Slug = title.ToLowerInvariant().Replace(' ', '-'),
            Description = "Description",
            ApplicationUrl = "https://example.test/apply",
            CompanyId = company.Id,
            Company = company,
            CategoryId = category.Id,
            Category = category,
            CurrencyCode = "INR",
            EmploymentType = EmploymentType.FullTime,
            WorkplaceType = WorkplaceType.Remote,
            ExperienceLevel = ExperienceLevel.Mid,
            Status = status,
            IsFeatured = featured,
            PublishedAtUtc = Now.AddDays(-3),
            ExpiresAtUtc = expiresAtUtc
        };

    private static User Candidate(string email) => new()
    {
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        PasswordHash = "not-used",
        FirstName = "Casey",
        LastName = "Candidate",
        RoleId = SystemRoleIds.Candidate,
        Status = UserStatus.Active,
        EmailConfirmed = true
    };

    private static JobPortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record SeedResult(
        Job Visible,
        Job RelatedVisible,
        Job Closed,
        Job Expired,
        Job Overdue,
        Job Archived,
        Job NoExpiry,
        IReadOnlyCollection<Job> All);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}

public sealed class AutomaticJobExpiryTests
{
    private static readonly DateTime Now =
        new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExpiryIsUtcEfficientAndIdempotent()
    {
        var repository = new ExpiryRepositoryFake(
        [
            NewJob(JobStatus.Published, Now.AddSeconds(-1), true),
            NewJob(JobStatus.Published, Now.AddDays(1), true),
            NewJob(JobStatus.Closed, Now.AddDays(-1), true)
        ]);
        var service = new JobExpiryService(
            repository, new FixedTimeProvider(Now));

        var first = await service.ExpireOverdueAsync();
        var second = await service.ExpireOverdueAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(Now, repository.LastUtcNow);
        Assert.Equal(JobStatus.Expired, repository.Jobs[0].Status);
        Assert.False(repository.Jobs[0].IsFeatured);
        Assert.Equal(JobStatus.Published, repository.Jobs[1].Status);
        Assert.Equal(JobStatus.Closed, repository.Jobs[2].Status);
    }

    private static Job NewJob(
        JobStatus status, DateTime? expiresAtUtc, bool featured) => new()
        {
            Status = status,
            ExpiresAtUtc = expiresAtUtc,
            IsFeatured = featured
        };

    private sealed class ExpiryRepositoryFake(List<Job> jobs) : IJobRepository
    {
        public List<Job> Jobs { get; } = jobs;
        public DateTime? LastUtcNow { get; private set; }

        public Task<int> ExpireOverduePublishedAsync(
            DateTime utcNow, CancellationToken cancellationToken = default)
        {
            LastUtcNow = utcNow;
            var overdue = Jobs.Where(job =>
                job.Status == JobStatus.Published &&
                job.ExpiresAtUtc.HasValue &&
                job.ExpiresAtUtc <= utcNow).ToArray();
            foreach (var job in overdue)
            {
                job.Status = JobStatus.Expired;
                job.IsFeatured = false;
                job.UpdatedAtUtc = utcNow;
            }
            return Task.FromResult(overdue.Length);
        }

        public Task<Job?> GetByIdAsync(
            Guid id,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(
            JobSearchQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CompanyExistsAsync(
            Guid companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CategoryExistsAsync(
            Guid categoryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(
            Job job, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Update(Job job) => throw new NotSupportedException();
        public void Remove(Job job) => throw new NotSupportedException();

        public Task DeletePermanentlyAsync(
            Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}

public sealed class AdminJobLifecycleApiContractTests
{
    [Fact]
    public void LifecycleEndpointsRequireAdministratorAndWorkerNeverMigratesDatabase()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            root, "JobPortal.API", "Controllers", "JobsController.cs"));
        var program = File.ReadAllText(Path.Combine(
            root, "JobPortal.API", "Program.cs"));
        var worker = File.ReadAllText(Path.Combine(
            root,
            "JobPortal.API",
            "HostedServices",
            "JobExpiryHostedService.cs"));

        Assert.Contains(
            "[Authorize(Roles = \"Administrator\")]",
            controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[AllowAnonymous]", controller, StringComparison.Ordinal);
        foreach (var action in new[]
                 {
                     "publish", "unpublish", "close", "archive", "feature", "unfeature"
                 })
            Assert.Contains(
                $"[HttpPost(\"{{id:guid}}/{action}\")]",
                controller,
                StringComparison.Ordinal);

        Assert.Contains(
            "AddHostedService<JobExpiryHostedService>()",
            program,
            StringComparison.Ordinal);
        Assert.Contains("PeriodicTimer", worker, StringComparison.Ordinal);
        Assert.DoesNotContain(".Migrate(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Database.Migrate", worker, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "JobPortal.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
