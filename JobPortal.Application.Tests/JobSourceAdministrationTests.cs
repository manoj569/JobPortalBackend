using System.Reflection;
using System.Security.Claims;
using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobSourceCategoryResolverTests
{
    [Fact]
    public async Task GuidKeyedEnvironmentVariableBindsAndResolves()
    {
        using var f = new JobSourceFixture();
        var prefix = $"CAREERHARBOR_TEST_{Guid.NewGuid():N}_";
        var key = $"{prefix}JobAggregation__SourceCategories__{f.Source.Id:D}";
        try
        {
            Environment.SetEnvironmentVariable(key, f.Category.Id.ToString());
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var services = new ServiceCollection();
            services.Configure<JobAggregationOptions>(configuration.GetSection(JobAggregationOptions.SectionName));
            using var provider = services.BuildServiceProvider();
            var resolver = new JobSourceCategoryResolver(provider.GetRequiredService<IOptionsMonitor<JobAggregationOptions>>(),
                new CategoryManagementRepository(f.Context));
            Assert.Equal(f.Category.Id, await resolver.ResolveCategoryIdAsync(f.Source));
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }

    [Fact]
    public async Task ConfiguredMappingResolvesExistingCategory()
    {
        using var f = new JobSourceFixture();
        f.Map(f.Category.Id.ToString());
        Assert.Equal(f.Category.Id, await f.Resolver.ResolveCategoryIdAsync(f.Source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task MissingOrMalformedMappingReturnsNull(string? mapping)
    {
        using var f = new JobSourceFixture();
        f.Map(mapping);
        Assert.Null(await f.Resolver.ResolveCategoryIdAsync(f.Source));
    }

    [Fact]
    public async Task NonexistentAndDeletedCategoriesAreNotUsed()
    {
        using var f = new JobSourceFixture();
        f.Map(Guid.NewGuid().ToString());
        Assert.Null(await f.Resolver.ResolveCategoryIdAsync(f.Source));
        f.Map(f.Category.Id.ToString());
        f.Context.Categories.Remove(f.Category);
        await f.Context.SaveChangesAsync();
        Assert.Null(await f.Resolver.ResolveCategoryIdAsync(f.Source));
    }

    [Fact]
    public async Task MappingUsesSourceIdAndObservesConfigurationReload()
    {
        using var f = new JobSourceFixture();
        f.Map(f.Category.Id.ToString());
        Assert.Null(await f.Resolver.ResolveCategoryIdAsync(new JobSource()));
        Assert.Equal(f.Category.Id, await f.Resolver.ResolveCategoryIdAsync(f.Source));
        f.Map("invalid");
        Assert.Null(await f.Resolver.ResolveCategoryIdAsync(f.Source));
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var f = new JobSourceFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            f.Resolver.ResolveCategoryIdAsync(f.Source, cancellation.Token));
    }
}

public sealed class JobSourceAdministrationTests
{
    [Fact]
    public async Task ListAndGetExposeCategoryAndOperationalStatusWithPagination()
    {
        using var f = new JobSourceFixture();
        f.Map(f.Category.Id.ToString());
        f.Source.LastRunAtUtc = JobSourceFixture.Now;
        f.Source.LastSuccessfulRunAtUtc = JobSourceFixture.Now.AddDays(-1);
        f.Source.LastError = "internal sensitive message";
        f.Source.ConsecutiveFailures = 2;
        await f.Context.SaveChangesAsync();
        var page = await f.Service.SearchAsync(new(PageSize: 1, CompanyId: f.Company.Id, AtsType: AtsType.Greenhouse, IsActive: true));
        var source = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(f.Category.Id, source.CategoryId);
        Assert.Equal(f.Category.Name, source.CategoryName);
        Assert.Equal(f.Company.Name, source.CompanyName);
        Assert.Equal(JobSourceFixture.Now, source.LastRunAtUtc);
        Assert.Equal(2, source.ConsecutiveFailures);
        Assert.DoesNotContain("sensitive", source.LastError);
        Assert.Equal(source, await f.Service.GetByIdAsync(f.Source.Id));
        Assert.Empty((await f.Service.SearchAsync(new(PageNumber: 2, PageSize: 1))).Items);
    }

    [Fact]
    public async Task MissingSourceReturnsNotFound()
    {
        using var f = new JobSourceFixture();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateTrimsIdentifierAndAuditsWithoutCreatingCompanyOrCategory()
    {
        using var f = new JobSourceFixture();
        var result = await f.Service.CreateAsync(f.Request with { AtsIdentifier = "  another-board  " });
        Assert.Equal("another-board", result.AtsIdentifier);
        Assert.True(result.IsActive);
        Assert.Null(result.CategoryId);
        Assert.Equal(1, await f.Context.Companies.CountAsync());
        Assert.Equal(1, await f.Context.Categories.CountAsync());
        Assert.Contains(f.Audit.Events, x => x.Action == AuditAction.Create && x.EntityId == result.Id.ToString());
    }

    [Fact]
    public async Task NonexistentCompanyIsRejected()
    {
        using var f = new JobSourceFixture();
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.CreateAsync(f.Request with { CompanyId = Guid.NewGuid() }));
        Assert.Equal(1, await f.Context.JobSources.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("/careers")]
    [InlineData("ftp://example.test/careers")]
    [InlineData("https://user:password@example.test/careers")]
    public async Task InvalidCareerPageUrlIsRejected(string url)
    {
        using var f = new JobSourceFixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.CreateAsync(f.Request with { CareerPageUrl = url }));
    }

    [Theory]
    [InlineData(AtsType.Greenhouse, null)]
    [InlineData(AtsType.Greenhouse, " ")]
    [InlineData(AtsType.Lever, null)]
    [InlineData(AtsType.Lever, "")]
    public async Task SupportedAtsRequiresIdentifier(AtsType atsType, string? identifier)
    {
        using var f = new JobSourceFixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.CreateAsync(f.Request with { AtsType = atsType, AtsIdentifier = identifier }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10081)]
    public async Task InvalidScanIntervalIsRejected(int interval)
    {
        using var f = new JobSourceFixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.CreateAsync(f.Request with { ScanIntervalMinutes = interval }));
    }

    [Fact]
    public void ValidatorEnforcesEntityLengthsEnumsCompanyAndPagination()
    {
        using var f = new JobSourceFixture();
        var validator = new SaveJobSourceRequestValidator();
        Assert.False(validator.Validate(f.Request with { CompanyId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(f.Request with { AtsType = (AtsType)99 }).IsValid);
        Assert.False(validator.Validate(f.Request with { AtsIdentifier = new string('a', 256) }).IsValid);
        Assert.False(validator.Validate(f.Request with { CareerPageUrl = "https://example.test/" + new string('a', 2048) }).IsValid);
        Assert.True(validator.Validate(f.Request with { ScanIntervalMinutes = 1 }).IsValid);
        Assert.True(validator.Validate(f.Request with { ScanIntervalMinutes = 10080 }).IsValid);
        Assert.False(new JobSourceSearchQueryValidator().Validate(new JobSourceSearchQuery(PageSize: 101)).IsValid);
        Assert.False(new JobSourceSearchQueryValidator().Validate(new JobSourceSearchQuery(PageNumber: 0)).IsValid);
    }

    [Fact]
    public async Task DuplicateTrimmedConfigurationIsRejected()
    {
        using var f = new JobSourceFixture();
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(f.Request with { AtsIdentifier = " acme " }));
        Assert.Equal("duplicate_job_source", exception.Code);
        Assert.Equal(1, await f.Context.JobSources.CountAsync());
    }

    [Fact]
    public async Task DatabaseUniqueRaceIsTranslatedToSafeConflict()
    {
        using var f = new JobSourceFixture();
        var service = f.CreateService(unitOfWork: new UniqueFailureUnitOfWork(f.Context));
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(f.Request with { AtsIdentifier = "different" }));
        Assert.Equal("duplicate_job_source", exception.Code);
        Assert.DoesNotContain("sensitive", exception.Message);
        Assert.Empty(f.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UpdateDeactivatesWithoutChangingRunHistoryAndAllowsSameIdentity()
    {
        using var f = new JobSourceFixture();
        f.Source.LastSuccessfulRunAtUtc = JobSourceFixture.Now.AddDays(-1);
        await f.Context.SaveChangesAsync();
        var result = await f.Service.UpdateAsync(f.Source.Id, f.Request with { IsActive = false, ScanIntervalMinutes = 60 });
        Assert.False(result.IsActive);
        Assert.Equal(60, result.ScanIntervalMinutes);
        Assert.Equal(JobSourceFixture.Now.AddDays(-1), result.LastSuccessfulRunAtUtc);
        f.Context.ChangeTracker.Clear();
        Assert.False((await f.Repository.GetByIdAsync(f.Source.Id))!.IsActive);
        Assert.Contains(f.Audit.Events, x => x.Action == AuditAction.Update);
    }

    [Fact]
    public async Task UpdateCannotDuplicateAnotherSource()
    {
        using var f = new JobSourceFixture();
        var other = await f.Service.CreateAsync(f.Request with { AtsIdentifier = "other" });
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.UpdateAsync(other.Id, f.Request));
    }

    [Fact]
    public async Task DeleteIsSoftDeactivatesAndAllowsRecreation()
    {
        using var f = new JobSourceFixture();
        await f.Service.DeleteAsync(f.Source.Id);
        Assert.Empty((await f.Service.SearchAsync(new())).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetByIdAsync(f.Source.Id));
        var deleted = await f.Context.JobSources.IgnoreQueryFilters().SingleAsync();
        Assert.True(deleted.IsDeleted);
        Assert.False(deleted.IsActive);
        Assert.NotNull(deleted.DeletedAtUtc);
        Assert.Contains(f.Audit.Events, x => x.Action == AuditAction.Delete);
        Assert.NotEqual(deleted.Id, (await f.Service.CreateAsync(f.Request)).Id);
    }

    private sealed class UniqueFailureUnitOfWork(JobPortalDbContext context) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new UniqueConstraintException("sensitive database details");
        public void ResetAfterFailure() => context.ChangeTracker.Clear();
    }
}

public sealed class JobSourceManualRunTests
{
    [Fact]
    public async Task MappedCategoryCreatesDraftAndReturnsSafeControllerResult()
    {
        using var f = new JobSourceFixture();
        f.Map(f.Category.Id.ToString());
        var action = await new AdminJobSourcesController(f.Service).Run(f.Source.Id, default);
        var response = Assert.IsType<ApiResponse<JobSourceRunResult>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.True(response.Data.Succeeded);
        Assert.Equal(1, response.Data.Created);
        Assert.Equal(1, response.Data.TotalReceived);
        var job = await f.Context.Jobs.SingleAsync();
        Assert.Equal(f.Category.Id, job.CategoryId);
        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Equal(JobSourceFixture.Now, f.Source.LastRunAtUtc);
        Assert.Equal(JobSourceFixture.Now, f.Source.LastSuccessfulRunAtUtc);
        Assert.Null(f.Source.LastError);
        Assert.Equal(0, f.Source.ConsecutiveFailures);
        Assert.Contains(f.Audit.Events, x => x.Action == AuditAction.Submit);
        Assert.Contains(f.Audit.Events, x => x.Metadata!["result"] == "manual_run_succeeded");
    }

    [Fact]
    public async Task NoMappingStillRefreshesCanonicalJobAndPreservesReferralAndRecruiter()
    {
        using var f = new JobSourceFixture();
        var job = new Job
        {
            Company = f.Company, CompanyId = f.Company.Id, Category = f.Category, CategoryId = f.Category.Id,
            Title = "Curated title", Slug = "curated", ReferenceNumber = "CURATED", Description = "Curated description",
            ApplicationUrl = f.Provider.Jobs.Single().ApplicationUrl!, Status = JobStatus.Published,
            MinimumSalary = 100, PublishedAtUtc = JobSourceFixture.Now.AddDays(-2)
        };
        job.Referral = new JobReferral { Job = job, JobId = job.Id, ApprovalStatus = JobReferralApprovalStatus.Approved };
        job.RecruiterContact = new JobRecruiterContact { Job = job, JobId = job.Id, ContactName = "Curated recruiter", IsSharingApproved = true };
        f.Context.Jobs.Add(job);
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        var result = await f.Service.RunAsync(f.Source.Id);
        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Created);
        f.Context.ChangeTracker.Clear();
        var saved = await new JobRepository(f.Context).GetByIdAsync(job.Id);
        Assert.NotNull(saved);
        Assert.Equal("Curated title", saved.Title);
        Assert.Equal("Curated description", saved.Description);
        Assert.Equal(f.Company.Id, saved.CompanyId);
        Assert.Equal(f.Category.Id, saved.CategoryId);
        Assert.Equal(JobStatus.Published, saved.Status);
        Assert.Equal(100, saved.MinimumSalary);
        Assert.Equal(job.PublishedAtUtc, saved.PublishedAtUtc);
        Assert.Equal(JobReferralApprovalStatus.Approved, saved.Referral!.ApprovalStatus);
        Assert.True(saved.RecruiterContact!.IsSharingApproved);
        Assert.Equal(JobSourceFixture.Now, saved.LastSeenAtUtc);
    }

    [Fact]
    public async Task NewUnmappedJobIsSkippedWithoutRejectingSource()
    {
        using var f = new JobSourceFixture();
        var result = await f.Service.RunAsync(f.Source.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(await f.Context.Jobs.ToArrayAsync());
        Assert.NotNull(f.Source.LastSuccessfulRunAtUtc);
    }

    [Fact]
    public async Task CustomSourceCanBeStoredButClearlyReportsUnsupportedProvider()
    {
        using var f = new JobSourceFixture();
        var source = await f.Service.CreateAsync(f.Request with { AtsType = AtsType.Custom, AtsIdentifier = null });
        var result = await f.Service.RunAsync(source.Id);
        Assert.False(result.Succeeded);
        Assert.Contains("No provider is registered", result.Error);
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public async Task ProviderFailureIsSanitizedAndKeepsPreviousSuccessfulRun()
    {
        using var f = new JobSourceFixture();
        f.Source.LastSuccessfulRunAtUtc = JobSourceFixture.Now.AddDays(-1);
        f.Source.ConsecutiveFailures = 2;
        await f.Context.SaveChangesAsync();
        f.Provider.Exception = new InvalidOperationException("secret token and stack details");
        var result = await f.Service.RunAsync(f.Source.Id);
        Assert.False(result.Succeeded);
        Assert.Equal("External job source run failed.", result.Error);
        var status = await f.Service.GetByIdAsync(f.Source.Id);
        Assert.Equal(3, status.ConsecutiveFailures);
        Assert.Equal(JobSourceFixture.Now.AddDays(-1), status.LastSuccessfulRunAtUtc);
        Assert.Equal(JobSourceFixture.Now, status.LastRunAtUtc);
        Assert.Contains(f.Audit.Events, x => x.Metadata!["result"] == "manual_run_failed");
    }

    [Fact]
    public async Task InactiveSourceDoesNotFetchAndMissingSourceReturnsNotFound()
    {
        using var f = new JobSourceFixture();
        await f.Service.UpdateAsync(f.Source.Id, f.Request with { IsActive = false });
        var result = await f.Service.RunAsync(f.Source.Id);
        Assert.False(result.Succeeded);
        Assert.Equal("Job source is inactive.", result.Error);
        Assert.Equal(0, f.Provider.Calls);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.RunAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ConcurrentRunAndEditAreRejectedAndGuardIsReleased()
    {
        using var f = new JobSourceFixture();
        var runner = new BlockingRunner();
        var service = f.CreateService(runner);
        var first = service.RunAsync(f.Source.Id);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Assert.ThrowsAsync<ConflictException>(() => service.RunAsync(f.Source.Id));
            await Assert.ThrowsAsync<ConflictException>(() => f.Service.UpdateAsync(f.Source.Id, f.Request));
            await Assert.ThrowsAsync<ConflictException>(() => f.Service.DeleteAsync(f.Source.Id));
        }
        finally { runner.Complete.SetResult(); }
        Assert.True((await first).Succeeded);
        using var lease = f.Guard.Acquire(f.Source.Id);
    }

    [Fact]
    public async Task CancellationPropagatesAndReleasesGuard()
    {
        using var f = new JobSourceFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.RunAsync(f.Source.Id, cancellation.Token));
        using var lease = f.Guard.Acquire(f.Source.Id);
    }

    private sealed class BlockingRunner : IJobSourceRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JobSourceRunResult> RunAsync(Guid jobSourceId, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Complete.Task.WaitAsync(cancellationToken);
            return new() { JobSourceId = jobSourceId, Succeeded = true };
        }
    }
}

public sealed class JobSourceAuthorizationTests
{
    [Theory]
    [InlineData("Administrator", true)]
    [InlineData("Candidate", false)]
    [InlineData("Employer", false)]
    [InlineData(null, false)]
    public async Task EveryEndpointRequiresExistingAdministratorAuthorization(string? role, bool allowed)
    {
        var controller = typeof(AdminJobSourcesController);
        var attributes = controller.GetCustomAttributes<AuthorizeAttribute>().ToArray();
        Assert.Equal("Administrator", Assert.Single(attributes).Roles);
        Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>());
        var actions = controller.GetMethods().Where(x => x.DeclaringType == controller && x.GetCustomAttributes<HttpMethodAttribute>().Any()).ToArray();
        Assert.Equal(6, actions.Length);
        Assert.All(actions, action => Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>()));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var policy = await AuthorizationPolicy.CombineAsync(provider.GetRequiredService<IAuthorizationPolicyProvider>(), attributes);
        Assert.NotNull(policy);
        var principal = role is null ? new ClaimsPrincipal(new ClaimsIdentity()) :
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, policy);
        Assert.Equal(allowed, result.Succeeded);
    }
}

internal sealed class JobSourceFixture : IDisposable
{
    public static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    private readonly ServiceProvider optionsProvider;
    private readonly IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
    public JobPortalDbContext Context { get; } = new(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public Company Company { get; } = new() { Name = "Acme", NormalizedName = "ACME", Slug = "acme" };
    public Category Category { get; } = new() { Name = "Engineering", Slug = "engineering" };
    public JobSource Source { get; }
    public JobSourceRepository Repository { get; }
    public JobSourceCategoryResolver Resolver { get; }
    public JobSourceRunGuard Guard { get; } = new();
    public AuditWriterTestDouble Audit { get; } = new();
    public FixtureProvider Provider { get; } = new();
    public JobSourceRunner Runner { get; }
    public JobSourceManagementService Service { get; }
    public SaveJobSourceRequest Request => new(Company.Id, "https://example.test/careers", AtsType.Greenhouse, "acme");

    public JobSourceFixture()
    {
        Source = new() { Company = Company, CompanyId = Company.Id, CareerPageUrl = Request.CareerPageUrl, AtsType = AtsType.Greenhouse, AtsIdentifier = "acme" };
        Context.AddRange(Company, Category, Source);
        Context.SaveChanges();
        Repository = new(Context);
        var services = new ServiceCollection();
        services.Configure<JobAggregationOptions>(configuration.GetSection(JobAggregationOptions.SectionName));
        optionsProvider = services.BuildServiceProvider();
        Resolver = new(optionsProvider.GetRequiredService<IOptionsMonitor<JobAggregationOptions>>(), new CategoryManagementRepository(Context));
        var jobs = new JobRepository(Context);
        var fingerprints = new JobFingerprintService();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(Context), new CategoryManagementRepository(Context),
            new JobDeduplicationService(jobs, fingerprints, new UrlCanonicalizer()), fingerprints, new UnitOfWork(Context), new FixedClock());
        Runner = new(Repository, [Provider], ingestion, new UnitOfWork(Context), new FixedClock(), Resolver);
        Service = CreateService();
    }

    public JobSourceManagementService CreateService(IJobSourceRunner? runner = null, IUnitOfWork? unitOfWork = null) =>
        new(Repository, new CompanyManagementRepository(Context), new CategoryManagementRepository(Context), Resolver,
            runner ?? Runner, Guard, unitOfWork ?? new UnitOfWork(Context), Audit,
            new SaveJobSourceRequestValidator(), new JobSourceSearchQueryValidator());

    public void Map(string? value)
    {
        configuration[$"JobAggregation:SourceCategories:{Source.Id:D}"] = value;
        configuration.Reload();
    }

    public void Dispose()
    {
        optionsProvider.Dispose();
        Context.Dispose();
        (configuration as IDisposable)?.Dispose();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    internal sealed class FixtureProvider : IExternalJobProvider
    {
        public AtsType AtsType => AtsType.Greenhouse;
        public Exception? Exception { get; set; }
        public int Calls { get; private set; }
        public IReadOnlyCollection<RawExternalJob> Jobs { get; } =
            [new() { Title = "Software Engineer", CompanyName = "Acme", Location = "Pune", ApplicationUrl = "https://example.test/jobs/1" }];
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Exception is not null) throw Exception;
            return Task.FromResult(Jobs);
        }
    }
}
