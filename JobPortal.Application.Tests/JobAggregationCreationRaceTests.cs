using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobAggregationCreationRaceTests
{
    [Theory]
    [InlineData("same-url")]
    [InlineData("same-fingerprint")]
    [InlineData("canonical-variants")]
    public async Task TwoInitialMissesRecheckInsideLockAndOnlyCreateOneJob(string scenario)
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var seed = new JobPortalDbContext(options);
        var company = new Company { Name = "Acme", Slug = "acme" };
        var category = new Category { Name = "Engineering", Slug = "engineering" };
        seed.AddRange(company, category);
        await seed.SaveChangesAsync();
        await using var firstContext = new JobPortalDbContext(options);
        await using var secondContext = new JobPortalDbContext(options);
        var locks = new TestAggregationLocks();
        var barrier = new InitialMissBarrier();
        var first = Service(firstContext, locks, barrier);
        var second = Service(secondContext, locks, barrier);
        var raw = new RawExternalJob { Title = "Engineer", CompanyName = "Acme", Location = "Pune",
            ApplicationUrl = "https://example.test/1?a=1&b=2", CategoryId = category.Id };
        var other = scenario switch
        {
            "same-url" => raw with { Title = "Changed title" },
            "same-fingerprint" => raw with { ApplicationUrl = "https://example.test/another" },
            _ => raw with { Title = "Changed title", ApplicationUrl = "https://EXAMPLE.test:443/1/?b=2&utm_source=x&a=1#fragment" }
        };
        var results = await Task.WhenAll(first.IngestAsync(raw), second.IngestAsync(other)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(results, x => x.Created);
        Assert.Single(results, x => x.MatchedExisting);
        Assert.Equal(results[0].JobId, results[1].JobId);
        var saved = await seed.Jobs.SingleAsync();
        Assert.Equal(JobStatus.Draft, saved.Status);
        Assert.Contains(saved.ApplicationUrl, new[] { raw.ApplicationUrl, other.ApplicationUrl });
        Assert.Equal(2, locks.CreationAcquisitions);
        Assert.Equal(2, locks.CreationReleases);
    }

    [Fact]
    public async Task DifferentIdentitiesRemainIndependentAndCancellationDoesNotLeak()
    {
        var locks = new TestAggregationLocks();
        await using var first = await locks.AcquireAsync("url1", "fp1");
        await using var independent = await locks.AcquireAsync("url2", "fp2").WaitAsync(TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        var blocked = locks.AcquireAsync("url1", "fp1", cancellation.Token);
        Assert.False(blocked.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
    }

    [Fact]
    public async Task ExistingMatchNeverAcquiresCreationLockAndPreservesReferralRecruiter()
    {
        using var f = new JobSourceFixture();
        var job = new Job { Company = f.Company, Category = f.Category, Title = "Curated", Description = "Keep",
            ApplicationUrl = f.Provider.Jobs.Single().ApplicationUrl!, Status = JobStatus.Published, MinimumSalary = 123 };
        job.Referral = new JobReferral { Job = job, ApprovalStatus = JobReferralApprovalStatus.Approved };
        job.RecruiterContact = new JobRecruiterContact { Job = job, ContactName = "Keep", IsSharingApproved = true };
        f.Context.Add(job);
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        var result = await f.Service.RunAsync(f.Source.Id);
        Assert.Equal(1, result.Matched);
        Assert.Equal(0, f.Locks.CreationAcquisitions);
        f.Context.ChangeTracker.Clear();
        var saved = await new JobRepository(f.Context).GetByIdAsync(job.Id);
        Assert.NotNull(saved);
        Assert.Equal("Curated", saved.Title);
        Assert.Equal("Keep", saved.Description);
        Assert.Equal(123, saved.MinimumSalary);
        Assert.Equal(JobStatus.Published, saved.Status);
        Assert.Equal(JobReferralApprovalStatus.Approved, saved.Referral!.ApprovalStatus);
        Assert.True(saved.RecruiterContact!.IsSharingApproved);
        Assert.Equal("Keep", saved.RecruiterContact.ContactName);
    }

    private static JobIngestionService Service(JobPortalDbContext context, TestAggregationLocks locks, InitialMissBarrier barrier)
    {
        var jobs = new JobRepository(context);
        var fingerprints = new JobFingerprintService();
        return new(jobs, new CompanyManagementRepository(context), new CategoryManagementRepository(context),
            new BarrierDedup(new JobDeduplicationService(jobs, fingerprints, new UrlCanonicalizer()), barrier),
            fingerprints, new UnitOfWork(context), TimeProvider.System, locks, new UrlCanonicalizer());
    }

    private sealed class InitialMissBarrier
    {
        private int arrivals;
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ArriveAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref arrivals) == 2) both.TrySetResult();
            await both.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        }
    }

    private sealed class BarrierDedup(IJobDeduplicationService inner, InitialMissBarrier barrier) : IJobDeduplicationService
    {
        private bool first = true;
        public async Task<DeduplicationResult> FindDuplicateAsync(string title, string companyName, string? location,
            string? externalUrl, Guid? companyId, CancellationToken cancellationToken = default)
        {
            var result = await inner.FindDuplicateAsync(title, companyName, location, externalUrl, companyId, cancellationToken);
            if (first) { first = false; Assert.False(result.IsDuplicate); await barrier.ArriveAsync(cancellationToken); }
            return result;
        }
    }
}
