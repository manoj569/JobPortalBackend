using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class WorkdayIngestionDiagnosticsTests
{
    [Fact]
    public void GeographyReasonsAreSeparateFromValidationAndKeepSafetyDecisions()
    {
        var policy = new JobSourcePublicationPolicy(Options.Create(new JobAggregationOptions()), TimeProvider.System);
        var selected = policy.Select(new JobSource(), new([
            new RawExternalJob { ExternalId = "india", CountryCodes = ["IN"] },
            new RawExternalJob { ExternalId = "foreign", CountryCodes = ["US"] },
            new RawExternalJob { ExternalId = "unknown", Location = "Unknown" },
            new RawExternalJob { ExternalId = "remote", Location = "Remote", WorkplaceTypeText = "remote" }
        ], 0, true));
        Assert.Equal("india", Assert.Single(selected.Jobs).ExternalId);
        Assert.Equal(1, selected.SelectionReasonCounts!["NonIndiaLocation"]);
        Assert.Equal(1, selected.SelectionReasonCounts["UnknownIndiaEligibility"]);
        Assert.Equal(1, selected.SelectionReasonCounts["AmbiguousRemoteEligibility"]);
        Assert.False(selected.IsComplete);
        Assert.Equal(0, selected.Skipped);
    }

    [Fact]
    public async Task CompleteWorkdayRunReportsCumulativeFilteringWithoutDuplicateTallies()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Workday;
        f.Map(f.Category.Id.ToString());
        var provider = new Batches([
            new([Job("india", "IN"), Job("foreign", "US")], 0, false),
            new([Job("india2", "IN")], 0, false),
            new([], 0, true)
        ]);
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context),
            new CategoryManagementRepository(f.Context), new JobDeduplicationService(jobs, fingerprints, canonical),
            fingerprints, unit, TimeProvider.System, f.Locks, canonical);
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit,
            TimeProvider.System, f.Resolver, new ExternalJobNormalizer(),
            jobRepository: jobs,
            publicationPolicy: new JobSourcePublicationPolicy(Options.Create(new JobAggregationOptions()), TimeProvider.System));
        var first = await runner.RunAsync(f.Source.Id);
        Assert.True(first.Succeeded);
        Assert.Equal(2, first.Created);
        Assert.Equal(0, first.Rejected);
        Assert.Equal(1, first.SelectionReasonCounts!["NonIndiaLocation"]);
        var repeated = await runner.RunAsync(f.Source.Id);
        Assert.True(repeated.Succeeded);
        Assert.Equal(0, repeated.Created);
        Assert.Equal(2, repeated.Unchanged);
        Assert.Equal(1, repeated.SelectionReasonCounts!["NonIndiaLocation"]);
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task JobLevelCategoriesReuseExistingTaxonomyWithoutSourceFallback()
    {
        using var f = new JobSourceFixture();
        var finance = new Category { Name = "Finance & Accounting", Slug = "finance-accounting" };
        f.Context.Categories.Add(finance);
        await f.Context.SaveChangesAsync();
        var resolver = new JobSourceCategoryResolver(new Monitor(new()), new CategoryManagementRepository(f.Context),
            new ExternalJobCategoryClassifier());
        Assert.Equal(finance.Id, await resolver.ResolveCategoryIdAsync(f.Source, new RawExternalJob { Title = "Financial Analyst" }));
        Assert.Equal(f.Category.Id, await resolver.ResolveCategoryIdAsync(f.Source,
            new RawExternalJob { Title = "Financial Analyst", CategoryId = f.Category.Id }));
        Assert.Null(await resolver.ResolveCategoryIdAsync(f.Source, new RawExternalJob { Title = "Unclassified professional" }));
        Assert.Equal(2, await f.Context.Categories.CountAsync());
    }

    private static RawExternalJob Job(string id, string country) => new()
    {
        ExternalId = id, Title = $"Engineer {id}", CompanyName = "Acme", Location = country == "IN" ? "Pune" : "Boston",
        CountryCodes = [country], Description = "Build reliable software.", ApplicationUrl = $"https://example.test/jobs/{id}"
    };

    private sealed class Monitor(JobAggregationOptions value) : IOptionsMonitor<JobAggregationOptions>
    {
        public JobAggregationOptions CurrentValue => value;
        public JobAggregationOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<JobAggregationOptions, string?> listener) => null;
    }

    private sealed class Batches(ExternalJobSourceSnapshot[] batches) : IBatchedExternalJobProvider
    {
        public AtsType AtsType => AtsType.Workday;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<RawExternalJob>>(batches.SelectMany(x => x.Jobs).ToArray());
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalJobSourceSnapshot(batches.SelectMany(x => x.Jobs).ToArray(), 0, true));
        public async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(JobSource source,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return batch;
            }
            await Task.CompletedTask;
        }
    }
}
