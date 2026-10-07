using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class DeloitteSourceReconciliationTests
{
    [Fact]
    public async Task ProviderUrlMatchDoesNotTakeOwnershipOrModifyManualJob()
    {
        using var f = new JobSourceFixture();
        const string url = "https://source.test/jobs/req-manual";
        var manual = new Job
        {
            ReferenceNumber = "MANUAL-URL", Title = "Manually curated title", Slug = "manual-url",
            Description = "Admin curated", ApplicationUrl = url, CanonicalApplicationUrlHash = ApplicationUrlIdentity.Hash(url),
            CompanyId = f.Company.Id, Company = f.Company,
            CategoryId = f.Category.Id, Category = f.Category, Status = JobStatus.Published
        };
        f.Context.Jobs.Add(manual);
        await f.Context.SaveChangesAsync();
        var raw = External(f, "req-manual") with { ApplicationUrl = url };
        var provider = new SnapshotProvider(new ExternalJobSourceSnapshot([raw], 0, true));
        var (runner, _) = CreateRunner(f, provider);

        var result = await runner.RunAsync(f.Source.Id);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Matched);
        Assert.Equal("Manually curated title", manual.Title);
        Assert.Equal("Admin curated", manual.Description);
        Assert.Null(manual.JobSourceId);
        Assert.Null(manual.ExternalJobId);
    }

    [Fact]
    public async Task RepeatedSyncUpdatesOnlyOwnedJobsAndClosesMissingOnlyAfterCompleteSnapshot()
    {
        using var f = new JobSourceFixture();
        var manual = new Job
        {
            ReferenceNumber = "MANUAL-1", Title = "Manual job", Slug = "manual-job", Description = "Curated",
            ApplicationUrl = "https://example.test/manual", CompanyId = f.Company.Id, Company = f.Company,
            CategoryId = f.Category.Id, Category = f.Category, Status = JobStatus.Published, PublishedAtUtc = JobSourceFixture.Now
        };
        f.Context.Jobs.Add(manual);
        await f.Context.SaveChangesAsync();

        var external = External(f, "req-1");
        var provider = new SnapshotProvider(new ExternalJobSourceSnapshot([external], 0, true));
        var (runner, repo) = CreateRunner(f, provider);

        var first = await runner.RunAsync(f.Source.Id);
        Assert.Equal(1, first.Created);
        Assert.Equal(0, first.Updated);
        Assert.Equal(0, first.Closed);
        var imported = Assert.Single(f.Context.Jobs.Where(x => x.JobSourceId == f.Source.Id));
        Assert.Equal("req-1", imported.ExternalJobId);
        Assert.Equal(ApplicationUrlIdentity.Hash(external.ApplicationUrl), imported.CanonicalApplicationUrlHash);

        var second = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Unchanged);

        var changedSourceJob = external with
        {
            Title = "Updated external engineer", Description = "Source changed",
            ApplicationUrl = "https://source.test/jobs/req-1-revised"
        };
        provider.Snapshot = new ExternalJobSourceSnapshot([changedSourceJob], 0, true);
        var changed = await runner.RunAsync(f.Source.Id);
        Assert.Equal(1, changed.Updated);
        Assert.Equal("Source changed", imported.Description);
        Assert.Equal(ApplicationUrlIdentity.Hash(changedSourceJob.ApplicationUrl), imported.CanonicalApplicationUrlHash);

        provider.Snapshot = new ExternalJobSourceSnapshot([], 0, false);
        var partial = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, partial.Closed);
        Assert.Equal(JobStatus.Draft, imported.Status);

        provider.Snapshot = new ExternalJobSourceSnapshot([external with { ExternalId = null }], 0, true);
        var missingIdentity = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, missingIdentity.Closed);
        Assert.Equal(JobStatus.Draft, imported.Status);

        provider.Snapshot = new ExternalJobSourceSnapshot([], 0, true);
        var complete = await runner.RunAsync(f.Source.Id);
        Assert.Equal(1, complete.Closed);
        Assert.Equal(JobStatus.Closed, imported.Status);
        Assert.Equal(JobStatus.Published, manual.Status);
        Assert.Null(manual.JobSourceId);
    }

    private static (JobSourceRunner Runner, JobRepository Repository) CreateRunner(JobSourceFixture f, SnapshotProvider provider)
    {
        var repo = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(repo, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(repo, fingerprints, canonicalizer), fingerprints, unit,
            new TestClock(), f.Locks, canonicalizer);
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit, new TestClock(), f.Resolver,
            new ExternalJobNormalizer(), jobRepository: repo);
        return (runner, repo);
    }

    private static RawExternalJob External(JobSourceFixture f, string id) => new()
    {
        ExternalId = id, Title = "External engineer", CompanyName = f.Company.Name,
        Description = "Source description", ApplicationUrl = $"https://source.test/jobs/{id}",
        CategoryId = f.Category.Id, ExpiresAtUtc = JobSourceFixture.Now.AddDays(5)
    };

    private sealed class SnapshotProvider(ExternalJobSourceSnapshot snapshot) : ICompleteExternalJobProvider
    {
        public ExternalJobSourceSnapshot Snapshot { get; set; } = snapshot;
        public AtsType AtsType => AtsType.Greenhouse;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<RawExternalJob>>(Snapshot.Jobs);
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot);
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(JobSourceFixture.Now);
    }
}
