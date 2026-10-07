using System.Diagnostics;
using System.Reflection;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Services;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace JobPortal.Application.Tests;

public sealed class LargeSourceIngestionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Measure1500FirstAndRepeatScans()
    {
        using var f = new JobSourceFixture();
        var jobProbe = Measure<IJobRepository>(new JobRepository(f.Context));
        var companyProbe = Measure<ICompanyManagementRepository>(new CompanyManagementRepository(f.Context));
        var categoryProbe = Measure<ICategoryManagementRepository>(new CategoryManagementRepository(f.Context));
        var unit = new MeasuredUnit(f);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var runLocks = new RunLockFactory(f.Locks);
        var ingestion = new JobIngestionService(jobProbe, companyProbe, categoryProbe,
            new JobDeduplicationService(jobProbe, fingerprints, canonical), fingerprints, unit,
            TimeProvider.System, runLocks, canonical);
        var provider = new SnapshotFeed(Enumerable.Range(0, 1500).Select(i => new RawExternalJob
        {
            ExternalId = $"id-{i}", Title = $"Engineer {i}", CompanyName = f.Company.Name,
            Location = "Pune", Description = "Build reliable systems.",
            ApplicationUrl = $"https://synthetic.example/jobs/{i}", CategoryId = f.Category.Id
        }).ToArray());
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit, TimeProvider.System,
            f.Resolver, new ExternalJobNormalizer(), enricher: new ExternalJobMetadataEnricher(), jobRepository: jobProbe);
        var watch = Stopwatch.StartNew();
        var first = await runner.RunAsync(f.Source.Id);
        output.WriteLine($"FIRST elapsedMs={watch.Elapsed.TotalMilliseconds:F0} Saves={unit.Saves} PeakTrackedJobs={unit.PeakTrackedJobs} Locks={f.Locks.CreationAcquisitions}");
        Report("FIRST", jobProbe); Report("FIRST COMPANY", companyProbe); Report("FIRST CATEGORY", categoryProbe);
        Assert.Equal(1500, first.Created);
        Assert.Equal(0, first.Failed);
        Assert.Equal(1500, await f.Context.Jobs.CountAsync());
        Assert.Equal(1, runLocks.Created);
        Assert.Equal(1, runLocks.Disposed);
        Assert.InRange(unit.PeakTrackedJobs, 1, 2);
        Assert.Equal(1, ((Probe<ICategoryManagementRepository>)(object)categoryProbe).Calls[nameof(ICategoryManagementRepository.ExistsAsync)]);
        Reset(jobProbe); Reset(companyProbe); Reset(categoryProbe); unit.Reset();
        watch.Restart();
        var second = await runner.RunAsync(f.Source.Id);
        output.WriteLine($"REPEAT elapsedMs={watch.Elapsed.TotalMilliseconds:F0} Saves={unit.Saves} PeakTrackedJobs={unit.PeakTrackedJobs}");
        Report("REPEAT", jobProbe); Report("REPEAT COMPANY", companyProbe); Report("REPEAT CATEGORY", categoryProbe);
        Assert.Equal(0, second.Created);
        Assert.Equal(1500, second.Unchanged);
        Assert.Equal(0, second.Failed);
        Assert.Equal(1500, await f.Context.Jobs.CountAsync());
        Assert.Equal(2, runLocks.Created);
        Assert.Equal(2, runLocks.Disposed);
        Assert.InRange(unit.PeakTrackedJobs, 1, 2);
        Assert.False(((Probe<IJobRepository>)(object)jobProbe).Calls.ContainsKey(nameof(IJobRepository.FindBySourceIdentityAsync)));
        Assert.False(((Probe<IJobRepository>)(object)jobProbe).Calls.ContainsKey(nameof(IJobRepository.FindByExternalUrlAsync)));
    }

    [Fact]
    public async Task Interrupted475JobRunResumesWithoutDuplicatesAndOnlyCompleteRerunClosesStaleJobs()
    {
        using var f = new JobSourceFixture();
        var feed = new SnapshotFeed(Synthetic(f, 1500));
        var (runner, unit) = Runner(f, feed);
        using var cancellation = new CancellationTokenSource();
        unit.AfterSave = () => { if (unit.Saves == 475) cancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(f.Source.Id, cancellation.Token));
        Assert.Equal(475, await f.Context.Jobs.CountAsync());
        Assert.Null((await f.Repository.GetByIdAsync(f.Source.Id))!.LastSuccessfulRunAtUtc);
        Assert.Equal(f.Locks.CreationAcquisitions, f.Locks.CreationReleases);
        Assert.Equal(unit.RunLocks!.Created, unit.RunLocks.Disposed);

        unit.AfterSave = null;
        var resumed = await runner.RunAsync(f.Source.Id);
        Assert.True(resumed.Succeeded);
        Assert.Equal(1025, resumed.Created);
        Assert.Equal(475, resumed.Unchanged);
        Assert.Equal(1500, await f.Context.Jobs.CountAsync());
        Assert.Equal(1500, await f.Context.Jobs.Select(x => x.ExternalJobId).Distinct().CountAsync());

        feed.Jobs = feed.Jobs.Skip(20).ToArray();
        feed.Complete = false;
        var previousSuccess = (await f.Repository.GetByIdAsync(f.Source.Id))!.LastSuccessfulRunAtUtc;
        Assert.NotNull(previousSuccess);
        var incomplete = await runner.RunAsync(f.Source.Id);
        Assert.False(incomplete.Succeeded);
        Assert.Equal(0, incomplete.Closed);
        Assert.Equal(previousSuccess, (await f.Repository.GetByIdAsync(f.Source.Id))!.LastSuccessfulRunAtUtc);
        Assert.Equal(0, await f.Context.Jobs.CountAsync(x => x.Status == JobStatus.Closed));
        feed.Complete = true;
        Assert.Equal(20, (await runner.RunAsync(f.Source.Id)).Closed);
    }

    [Fact]
    public async Task MixedUpdatesInsertsManualMatchAndDuplicateIdentitiesRemainSafe()
    {
        using var f = new JobSourceFixture();
        var manual = new JobPortal.Domain.Entities.Job
        {
            CompanyId = f.Company.Id, Company = f.Company, CategoryId = f.Category.Id, Category = f.Category,
            Title = "Curated", Description = "Keep manual fields", ApplicationUrl = "https://synthetic.example/jobs/0",
            Status = JobStatus.Published
        };
        f.Context.Add(manual);
        await f.Context.SaveChangesAsync();
        var feed = new SnapshotFeed(Synthetic(f, 100));
        var (runner, _) = Runner(f, feed);
        var first = await runner.RunAsync(f.Source.Id);
        Assert.Equal(99, first.Created);
        Assert.Equal(1, first.Matched);
        feed.Jobs = Synthetic(f, 125).Select(x => x.ExternalId == "id-1" ? x with { Description = "Updated source facts" } : x).ToArray();
        var mixed = await runner.RunAsync(f.Source.Id);
        Assert.Equal(25, mixed.Created);
        Assert.Equal(1, mixed.Updated);
        Assert.Equal(98, mixed.Unchanged);
        var savedManual = await f.Context.Jobs.AsNoTracking().SingleAsync(x => x.Id == manual.Id);
        Assert.Null(savedManual.JobSourceId);
        Assert.Null(savedManual.ExternalJobId);
        Assert.Equal("Curated", savedManual.Title);
        Assert.Equal("Keep manual fields", savedManual.Description);
        Assert.Equal(JobStatus.Published, savedManual.Status);

        feed.Jobs = [feed.Jobs.First(x => x.ExternalId == "id-1"), feed.Jobs.First(x => x.ExternalId == "id-1")];
        var duplicate = await runner.RunAsync(f.Source.Id);
        Assert.False(duplicate.Succeeded);
        Assert.Equal(0, duplicate.Created);
        Assert.Equal(0, duplicate.Closed);
        Assert.Equal(125, await f.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task SaveFailureDiscardsDirtyCacheAndRerunRecoversWithoutClosingStaleJobs()
    {
        using var f = new JobSourceFixture();
        var feed = new SnapshotFeed(Synthetic(f, 50));
        var (runner, unit) = Runner(f, feed);
        unit.FailSave = 25;
        var failed = await runner.RunAsync(f.Source.Id);
        Assert.False(failed.Succeeded);
        Assert.Equal(1, failed.Failed);
        Assert.Equal(0, failed.Closed);
        Assert.Null(f.Source.LastSuccessfulRunAtUtc);
        Assert.Equal(49, await f.Context.Jobs.CountAsync());
        unit.FailSave = null;
        var recovered = await runner.RunAsync(f.Source.Id);
        Assert.True(recovered.Succeeded);
        Assert.Equal(1, recovered.Created);
        Assert.Equal(49, recovered.Unchanged);
        Assert.Equal(50, await f.Context.Jobs.CountAsync());
    }

    private static RawExternalJob[] Synthetic(JobSourceFixture f, int count) => Enumerable.Range(0, count).Select(i => new RawExternalJob
    {
        ExternalId = $"id-{i}", Title = $"Engineer {i}", CompanyName = f.Company.Name, Location = "Pune",
        Description = "Build reliable systems.", ApplicationUrl = $"https://synthetic.example/jobs/{i}", CategoryId = f.Category.Id
    }).ToArray();

    [Fact]
    public async Task UpdatingOwnedUrlInvalidatesOldPreloadedUrlMatchBeforeNewOpeningIsInserted()
    {
        using var f = new JobSourceFixture();
        var original = Synthetic(f, 1).Single();
        var feed = new SnapshotFeed([original]);
        var (runner, _) = Runner(f, feed);
        Assert.Equal(1, (await runner.RunAsync(f.Source.Id)).Created);
        feed.Jobs = [original with { ApplicationUrl = "https://synthetic.example/revised/0" },
            original with { ExternalId = "new-id", Title = "Independent opening" }];
        var mixed = await runner.RunAsync(f.Source.Id);
        Assert.True(mixed.Succeeded);
        Assert.Equal(1, mixed.Updated);
        Assert.Equal(1, mixed.Created);
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
        Assert.Equal(original.ApplicationUrl, (await f.Context.Jobs.AsNoTracking().SingleAsync(x => x.ExternalJobId == "new-id")).ApplicationUrl);
    }

    [Fact]
    public async Task ProviderSkippedDetailsPreventSuccessAndStaleClosure()
    {
        using var f = new JobSourceFixture();
        var feed = new SnapshotFeed(Synthetic(f, 30));
        var (runner, _) = Runner(f, feed);
        Assert.True((await runner.RunAsync(f.Source.Id)).Succeeded);
        var previous = f.Source.LastSuccessfulRunAtUtc;
        feed.Jobs = Synthetic(f, 10);
        feed.Skipped = 22;
        var result = await runner.RunAsync(f.Source.Id);
        Assert.False(result.Succeeded);
        Assert.Equal(22, result.Skipped);
        Assert.Equal(0, result.Closed);
        Assert.Equal(previous, f.Source.LastSuccessfulRunAtUtc);
        Assert.Equal(0, await f.Context.Jobs.CountAsync(x => x.Status == JobStatus.Closed));
    }

    private static (JobSourceRunner Runner, MeasuredUnit Unit) Runner(JobSourceFixture f, SnapshotFeed feed)
    {
        var jobs = new JobRepository(f.Context);
        var unit = new MeasuredUnit(f);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        unit.RunLocks = new RunLockFactory(f.Locks);
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, unit.RunLocks, canonical);
        return (new(f.Repository, [feed], ingestion, unit, TimeProvider.System, f.Resolver, new ExternalJobNormalizer(),
            enricher: new ExternalJobMetadataEnricher(), jobRepository: jobs), unit);
    }

    private void Report<T>(string label, T proxy) where T : class =>
        output.WriteLine(label + " " + string.Join(", ", ((Probe<T>)(object)proxy).Calls.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")));
    private static void Reset<T>(T proxy) where T : class => ((Probe<T>)(object)proxy).Calls.Clear();
    private static T Measure<T>(T inner) where T : class
    {
        var proxy = DispatchProxy.Create<T, Probe<T>>();
        ((Probe<T>)(object)proxy).Inner = inner;
        return proxy;
    }

    public class Probe<T> : DispatchProxy where T : class
    {
        public T Inner { get; set; } = null!;
        public Dictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            Calls[targetMethod.Name] = Calls.GetValueOrDefault(targetMethod.Name) + 1;
            return targetMethod.Invoke(Inner, args);
        }
    }

    private sealed class MeasuredUnit(JobSourceFixture fixture) : IUnitOfWork
    {
        public int Saves { get; private set; }
        public int PeakTrackedJobs { get; private set; }
        public Action? AfterSave { get; set; }
        public int? FailSave { get; set; }
        public RunLockFactory? RunLocks { get; set; }
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            if (Saves == FailSave) throw new InvalidOperationException("Synthetic save failure.");
            PeakTrackedJobs = Math.Max(PeakTrackedJobs, fixture.Context.ChangeTracker.Entries<JobPortal.Domain.Entities.Job>().Count());
            var result = await fixture.Context.SaveChangesAsync(cancellationToken);
            AfterSave?.Invoke();
            return result;
        }
        public void ResetAfterFailure() => fixture.Context.ChangeTracker.Clear();
        public void Reset() { Saves = 0; PeakTrackedJobs = 0; }
    }

    private sealed class RunLockFactory(TestAggregationLocks inner) : IExternalJobCreationLock, IExternalJobCreationLockRunFactory
    {
        public int Created;
        public int Disposed;
        public Task<IAsyncDisposable> AcquireAsync(string? canonicalUrl, string fingerprintHash, CancellationToken cancellationToken = default) =>
            inner.AcquireAsync(canonicalUrl, fingerprintHash, cancellationToken);
        public IExternalJobCreationLockRun CreateRun() { Created++; return new Run(this); }
        private sealed class Run(RunLockFactory owner) : IExternalJobCreationLockRun
        {
            private bool disposed;
            public Task<IAsyncDisposable> AcquireAsync(string? canonicalUrl, string fingerprintHash, CancellationToken cancellationToken = default) =>
                owner.AcquireAsync(canonicalUrl, fingerprintHash, cancellationToken);
            public ValueTask DisposeAsync() { if (!disposed) { disposed = true; owner.Disposed++; } return ValueTask.CompletedTask; }
        }
    }

    private sealed class SnapshotFeed(IReadOnlyCollection<RawExternalJob> jobs) : ICompleteExternalJobProvider
    {
        public IReadOnlyCollection<RawExternalJob> Jobs { get; set; } = jobs;
        public bool Complete { get; set; } = true;
        public int Skipped { get; set; }
        public AtsType AtsType => AtsType.Greenhouse;
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobPortal.Domain.Entities.JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalJobSourceSnapshot(Jobs, Skipped, Complete));
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobPortal.Domain.Entities.JobSource source, CancellationToken cancellationToken = default) => Task.FromResult(Jobs);
    }
}
