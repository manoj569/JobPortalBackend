using System.Diagnostics;
using System.Reflection;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace JobPortal.Application.Tests;

public sealed class WorkdayPersistencePerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(500, 50)]
    [InlineData(500, 100)]
    [InlineData(500, 200)]
    [InlineData(1000, 50)]
    [InlineData(1000, 100)]
    [InlineData(1000, 200)]
    public async Task MeasureActualRepositoryFirstAndRepeatBatches(int count, int batchSize)
    {
        await WarmUpQueryPathsAsync();
        using var f = new JobSourceFixture();
        var (service, unit, probe) = Service(f);
        var raw = Jobs(f, count);
        var watch = Stopwatch.StartNew();
        foreach (var batch in raw.Chunk(batchSize))
            Assert.All(await service.IngestBatchAsync(batch), x => Assert.Equal(JobIngestionOutcome.Created, x.Outcome));
        Report("BATCH FIRST", count, batchSize, watch, unit, probe);
        Assert.Equal((count + batchSize - 1) / batchSize, unit.Saves);
        Assert.False(probe.Calls.ContainsKey(nameof(IJobRepository.FindByExternalUrlAsync)));
        Assert.False(probe.Calls.ContainsKey(nameof(IJobRepository.FindByFingerprintHashAsync)));
        Assert.False(probe.Calls.ContainsKey(nameof(IJobRepository.FindBySourceIdentityAsync)));
        Assert.Equal(count, probe.Calls[nameof(IJobRepository.FindCandidatesForFuzzyMatchAsync)]);
        Assert.InRange(unit.PeakTracked, 1, batchSize);
        unit.Saves = 0; probe.Calls.Clear(); watch.Restart();
        foreach (var batch in raw.Chunk(batchSize))
            Assert.All(await service.IngestBatchAsync(batch), x => Assert.Equal(JobIngestionOutcome.Unchanged, x.Outcome));
        Report("BATCH REPEAT", count, batchSize, watch, unit, probe);
        Assert.Equal((count + batchSize - 1) / batchSize, unit.Saves);
        Assert.False(probe.Calls.ContainsKey(nameof(IJobRepository.FindCandidatesForFuzzyMatchAsync)));
        Assert.Equal(count, await f.Context.Jobs.CountAsync());
        Assert.Equal(f.Locks.CreationAcquisitions, f.Locks.CreationReleases);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    public async Task MeasureExistingItemCommitBaseline(int count)
    {
        await WarmUpQueryPathsAsync();
        using var f = new JobSourceFixture();
        var (service, unit, probe) = Service(f);
        var watch = Stopwatch.StartNew();
        foreach (var batch in Jobs(f, count).Chunk(100))
        {
            await service.PrepareRunAsync(batch);
            foreach (var raw in batch) await service.IngestAsync(raw);
        }
        Report("ITEM FIRST", count, 100, watch, unit, probe);
        Assert.Equal(count, unit.Saves);
        await service.CompleteRunAsync();
    }

    [Fact]
    public async Task Partial475RerunMixedUpdatesAndDuplicateIdentitiesRemainIdempotent()
    {
        using var f = new JobSourceFixture();
        var (service, _, _) = Service(f);
        var raw = Jobs(f, 1000);
        foreach (var batch in raw.Take(475).Chunk(100)) await service.IngestBatchAsync(batch);
        var revised = raw.Select(x => x with { Description = "Revised factual description" }).ToArray();
        var results = new List<JobIngestionResult>();
        foreach (var batch in revised.Concat([revised[0]]).Chunk(100)) results.AddRange(await service.IngestBatchAsync(batch));
        Assert.Equal(525, results.Count(x => x.Outcome == JobIngestionOutcome.Created));
        Assert.Equal(475, results.Count(x => x.Outcome == JobIngestionOutcome.Updated));
        Assert.Equal(1, results.Count(x => x.Outcome == JobIngestionOutcome.Unchanged));
        Assert.Equal(1000, await f.Context.Jobs.CountAsync());
        Assert.Equal(1000, await f.Context.Jobs.Select(x => x.ExternalJobId).Distinct().CountAsync());
        Assert.All(await f.Context.Jobs.ToArrayAsync(), x => Assert.Equal("Revised factual description", x.Description));
    }

    [Fact]
    public async Task DistinctSourceRequisitionsWithTheSameFingerprintRemainDistinctAndLockedUntilSave()
    {
        using var f = new JobSourceFixture();
        var (service, unit, _) = Service(f);
        var raw = Jobs(f, 1)[0];
        Task<IAsyncDisposable>? competitor = null;
        unit.BeforeSave = () =>
        {
            competitor = f.Locks.AcquireAsync(raw.ApplicationUrl, new JobFingerprintService()
                .GenerateFingerprint(raw.Title!, f.Company.Name, raw.Location));
            Assert.False(competitor.IsCompleted);
        };
        var results = await service.IngestBatchAsync([raw, raw with { ExternalId = "second", ApplicationUrl = "https://synthetic.example/second" }]);
        Assert.Equal(JobIngestionOutcome.Created, results[0].Outcome);
        Assert.Equal(JobIngestionOutcome.Created, results[1].Outcome);
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
        await using var released = await competitor!;
    }

    [Fact]
    public async Task SameTitleLocationRequisitionsAreIdempotentAcrossBatchesAndReruns()
    {
        using var f = new JobSourceFixture();
        var (service, _, _) = Service(f);
        var jobs = Jobs(f, 30).Select(x => x with { Title = "Application Developer" }).ToArray();
        foreach (var batch in jobs.Chunk(10))
            Assert.All(await service.IngestBatchAsync(batch), x => Assert.Equal(JobIngestionOutcome.Created, x.Outcome));
        foreach (var batch in jobs.Chunk(10))
            Assert.All(await service.IngestBatchAsync(batch), x => Assert.Equal(JobIngestionOutcome.Unchanged, x.Outcome));
        Assert.Equal(30, await f.Context.Jobs.CountAsync());
        var sameUrl = await service.IngestAsync(jobs[0] with { ExternalId = "alias" });
        Assert.Equal(JobIngestionOutcome.MatchedByUrl, sameUrl.Outcome);
        Assert.Equal(30, await f.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task PublishedSourceRequisitionCannotAbsorbADifferentRequisitionByFuzzyMatching()
    {
        using var f = new JobSourceFixture();
        var (service, _, _) = Service(f);
        const string title = "Application Developer Enterprise Business Technology Services";
        var original = Jobs(f, 1)[0] with { Title = title };
        await service.IngestAsync(original);
        var existing = await f.Context.Jobs.SingleAsync();
        existing.Status = JobStatus.Published;
        existing.PublishedAtUtc = DateTime.UtcNow;
        await f.Context.SaveChangesAsync();
        var repo = new JobRepository(f.Context);
        Assert.NotNull(Assert.Single(await repo.FindCandidatesForFuzzyMatchAsync(f.Company.Id, title, "Pune", 100)).Company);
        var fuzzy = await new JobDeduplicationService(repo, new JobFingerprintService(), new UrlCanonicalizer())
            .FindDuplicateAsync(title + "s", f.Company.Name, "Pune", "https://synthetic.example/new", f.Company.Id);
        Assert.Equal(JobPortal.Application.Abstractions.Jobs.MatchType.Fuzzy, fuzzy.MatchTypeEnum); // Reproduce the old false match.
        var result = await service.IngestAsync(original with { ExternalId = "different-requisition",
            Title = title + "s", ApplicationUrl = "https://synthetic.example/new" });
        Assert.Equal(JobIngestionOutcome.Created, result.Outcome);
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task FailedBatchDoesNotClaimOutcomesOrLeaveTrackedBusinessChanges()
    {
        using var f = new JobSourceFixture();
        var (service, unit, _) = Service(f);
        unit.BeforeSave = () => throw new InvalidOperationException("Synthetic commit failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestBatchAsync(Jobs(f, 50)));
        Assert.Empty(await f.Context.Jobs.ToArrayAsync());
        Assert.Equal(f.Locks.CreationAcquisitions, f.Locks.CreationReleases);
        unit.BeforeSave = null;
        Assert.Equal(50, (await service.IngestBatchAsync(Jobs(f, 50))).Count(x => x.Outcome == JobIngestionOutcome.Created));
    }

    [Fact]
    public async Task ReviewOnlyBatchUsesOneReadAndCannotBypassPublicationAuthority()
    {
        using var f = new JobSourceFixture();
        var (service, _, probe) = Service(f);
        var saved = await service.IngestBatchAsync(Jobs(f, 50));
        var repository = DispatchProxy.Create<IJobRepository, LargeSourceIngestionTests.Probe<IJobRepository>>();
        var reviewsProbe = (LargeSourceIngestionTests.Probe<IJobRepository>)(object)repository;
        reviewsProbe.Inner = new JobRepository(f.Context);
        var publisher = new JobAutoPublishService(repository, new JobQualityGate(), null!,
            Options.Create(new JobAggregationOptions { AutoPublishEnabled = true }), TimeProvider.System);
        var decisions = await publisher.ReviewBatchAsync(saved.Select(x => x.JobId!.Value).ToArray());
        Assert.Equal(50, decisions.Count);
        Assert.All(decisions.Values, x => Assert.Equal(JobAutoPublishOutcome.NeedsReview, x.Outcome));
        Assert.Equal(1, reviewsProbe.Calls[nameof(IJobRepository.FindAggregationReviewJobsAsync)]);
        Assert.False(reviewsProbe.Calls.ContainsKey(nameof(IJobRepository.GetByIdAsync)));
        Assert.Equal(50, probe.Calls[nameof(IJobRepository.AddAsync)]);
        var eligible = await f.Context.Jobs.FirstAsync();
        eligible.ExpiresAtUtc = DateTime.UtcNow.AddDays(10);
        eligible.EmploymentType = (EmploymentType)1;
        eligible.WorkplaceType = (WorkplaceType)1;
        eligible.ExperienceLevel = (ExperienceLevel)1;
        await f.Context.SaveChangesAsync();
        Assert.Equal(JobQualityDecision.Eligible, new JobQualityGate().Evaluate(eligible, DateTime.UtcNow).Decision);
        // Eligible snapshots never produce Published. The caller must invoke the
        // existing authoritative TryPublishAsync path with fresh validation.
        Assert.Empty(await publisher.ReviewBatchAsync([eligible.Id]));
    }

    [Fact]
    public async Task ManualUrlMatchPreservesOwnershipAndCuratedBusinessFields()
    {
        using var f = new JobSourceFixture();
        var (service, _, probe) = Service(f, metadataOnlySql: true);
        var raw = Jobs(f, 1)[0];
        var manual = await service.IngestAsync(raw with { JobSourceId = null, ExternalId = null, Description = "Curated manual description" });
        var originalUpdatedAt = (await f.Context.Jobs.SingleAsync()).UpdatedAtUtc;
        probe.Calls.Clear();
        var result = Assert.Single(await service.IngestBatchAsync([raw]));
        Assert.Equal(JobIngestionOutcome.MatchedByUrl, result.Outcome);
        Assert.Equal(manual.JobId, result.JobId);
        var stored = Assert.Single(await f.Context.Jobs.ToArrayAsync());
        Assert.Null(stored.JobSourceId);
        Assert.Null(stored.ExternalJobId);
        Assert.Equal("Curated manual description", stored.Description);
        Assert.Equal(originalUpdatedAt, stored.UpdatedAtUtc);
        Assert.True(probe.Calls.ContainsKey(nameof(IJobRepository.TouchAggregationMetadataAsync)));
        Assert.False(probe.Calls.ContainsKey(nameof(IJobRepository.TrackAggregationJob)));
    }

    [Fact]
    public async Task LegacyFuzzyMatchRepairsItsStoredFingerprintAndReusesItWithinTheBatch()
    {
        using var f = new JobSourceFixture();
        const string title = "Senior Software Engineer Reliable Distributed Platform Engineering Services";
        var manual = new Job { Title = title, Company = f.Company, Category = f.Category, Location = "Pune",
            Description = "Curated legacy description", ApplicationUrl = "https://synthetic.example/legacy",
            Status = JobStatus.Published, PublishedAtUtc = DateTime.UtcNow.AddDays(-1) };
        f.Context.Add(manual);
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        var (service, _, _) = Service(f);
        var raw = Jobs(f, 1)[0] with { Title = title };
        var results = await service.IngestBatchAsync([raw,
            raw with { ExternalId = "second", ApplicationUrl = "https://synthetic.example/second" }]);
        Assert.Equal(JobIngestionOutcome.MatchedByFuzzy, results[0].Outcome);
        Assert.Equal(JobIngestionOutcome.MatchedByFingerprint, results[1].Outcome);
        var stored = Assert.Single(await f.Context.Jobs.ToArrayAsync());
        Assert.Equal(new JobFingerprintService().GenerateFingerprint(title, f.Company.Name, "Pune"), stored.FingerprintHash);
        Assert.Null(stored.JobSourceId);
        Assert.Equal("Curated legacy description", stored.Description);
        Assert.Equal(JobStatus.Published, stored.Status);
    }

    [Fact]
    public async Task CancellationBeforeNextBatchCommitPreservesEarlierBatchAndAllowsReplay()
    {
        using var f = new JobSourceFixture();
        var (service, unit, _) = Service(f);
        var raw = Jobs(f, 200);
        await service.IngestBatchAsync(raw.Take(100).ToArray());
        using var cancellation = new CancellationTokenSource();
        unit.BeforeSave = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.IngestBatchAsync(raw.Skip(100).ToArray(), cancellation.Token));
        Assert.Equal(100, await f.Context.Jobs.CountAsync());
        Assert.Equal(f.Locks.CreationAcquisitions, f.Locks.CreationReleases);
        unit.BeforeSave = null;
        foreach (var batch in raw.Chunk(100)) await service.IngestBatchAsync(batch);
        Assert.Equal(200, await f.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task AdminCountPrecedesPagingAndDraftStatusSourceAndSearchFiltersAreExplicit()
    {
        using var f = new JobSourceFixture();
        var (service, unit, _) = Service(f);
        foreach (var batch in Jobs(f, 560).Chunk(100)) await service.IngestBatchAsync(batch);
        var repository = new JobRepository(f.Context);
        var first = await repository.SearchAsync(new(PageSize: 25, JobSourceId: f.Source.Id));
        var second = await repository.SearchAsync(new(PageNumber: 2, PageSize: 25, JobSourceId: f.Source.Id));
        Assert.Equal(560, first.TotalCount);
        Assert.Equal(560, second.TotalCount);
        Assert.Equal(25, first.Items.Count);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.All(first.Items, x => Assert.Equal(JobStatus.Draft, x.Status));
        Assert.Equal(0, (await repository.SearchAsync(new(Status: JobStatus.Published, JobSourceId: f.Source.Id))).TotalCount);
        Assert.Equal(560, (await repository.SearchAsync(new(Status: JobStatus.Draft, JobSourceId: f.Source.Id))).TotalCount);
        Assert.Equal(0, (await repository.SearchAsync(new(JobSourceId: Guid.NewGuid()))).TotalCount);
        Assert.Equal(1, (await repository.SearchAsync(new(Search: "unique-description-559", JobSourceId: f.Source.Id))).TotalCount);
        var last = await repository.SearchAsync(new(PageNumber: 23, PageSize: 25, JobSourceId: f.Source.Id));
        Assert.Equal(10, last.Items.Count);
        var adminJobs = new JobService(repository, unit, f.Audit, new CreateJobRequestValidator(),
            new UpdateJobRequestValidator(), new JobSearchQueryValidator.UpdateRecruiterContactRequestValidator(),
            new JobSearchQueryValidator(), TimeProvider.System, sources: f.Repository);
        var response = await adminJobs.SearchAsync(new(PageSize: 25, JobSourceId: f.Source.Id));
        Assert.Equal(560, response.TotalCount);
        Assert.Equal(25, response.Items.Count);
        Assert.Equal(23, response.TotalPages);
    }

    private void Report(string label, int count, int size, Stopwatch watch, CountingUnit unit,
        LargeSourceIngestionTests.Probe<IJobRepository> probe)
    {
        output.WriteLine($"{label} jobs={count} batch={size} elapsedMs={watch.Elapsed.TotalMilliseconds:F0} jobsPerSecond={count / watch.Elapsed.TotalSeconds:F1} saves={unit.Saves} peakTracked={unit.PeakTracked}");
        output.WriteLine(string.Join(", ", probe.Calls.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")));
    }

    [Fact]
    public async Task ChangedOwnedFingerprintDoesNotKeepAnObsoleteBatchMatch()
    {
        using var f = new JobSourceFixture();
        var (service, _, _) = Service(f);
        var original = Jobs(f, 1)[0];
        await service.IngestBatchAsync([original]);
        var results = await service.IngestBatchAsync([
            original with { Title = "Updated factual title" },
            original with { ExternalId = "new", ApplicationUrl = "https://synthetic.example/new" }]);
        Assert.Equal(JobIngestionOutcome.Updated, results[0].Outcome);
        Assert.Equal(JobIngestionOutcome.Created, results[1].Outcome);
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task PublishedOwnedJobsRetainIndividualCommitOrdering()
    {
        using var f = new JobSourceFixture();
        var (service, unit, _) = Service(f);
        var raw = Jobs(f, 2);
        var inserted = await service.IngestBatchAsync(raw);
        var published = await f.Context.Jobs.SingleAsync(x => x.Id == inserted[0].JobId);
        published.Status = JobStatus.Published;
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        unit.Saves = 0;
        Assert.Empty(await service.IngestBatchAsync(raw));
        var results = new List<JobIngestionResult>();
        foreach (var job in raw) results.Add(await service.IngestAsync(job));
        Assert.Equal(2, unit.Saves);
        Assert.All(results, x => Assert.Equal(JobIngestionOutcome.Unchanged, x.Outcome));
        Assert.Equal(f.Locks.CreationAcquisitions, f.Locks.CreationReleases);
    }

    [Fact]
    public async Task IndependentContextsWithOverlappingReversedBatchesCannotDuplicateVacancies()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var seed = new JobPortalDbContext(options);
        var company = new Company { Name = "Acme", Slug = "acme" };
        var category = new Category { Name = "Engineering", Slug = "engineering" };
        var sourceA = new JobSource { Company = company, CompanyId = company.Id, AtsType = AtsType.Workday };
        var sourceB = new JobSource { Company = company, CompanyId = company.Id, AtsType = AtsType.Workday };
        seed.AddRange(company, category, sourceA, sourceB);
        await seed.SaveChangesAsync();
        await using var contextA = new JobPortalDbContext(options);
        await using var contextB = new JobPortalDbContext(options);
        var locks = new TestAggregationLocks();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        JobIngestionService Create(JobPortalDbContext context, IUnitOfWork unit)
        {
            var jobs = new JobRepository(context);
            var fingerprints = new JobFingerprintService();
            var canonical = new UrlCanonicalizer();
            return new(jobs, new CompanyManagementRepository(context), new CategoryManagementRepository(context),
                new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, locks, canonical);
        }
        var raw = Enumerable.Range(0, 50).Select(i => new RawExternalJob
        {
            JobSourceId = sourceA.Id, ExternalId = $"job-{i}", CompanyId = company.Id, CompanyName = company.Name,
            Title = $"Engineer {i}", Location = "Pune", CategoryId = category.Id, Description = "Factual description",
            ApplicationUrl = $"https://synthetic.example/concurrent/{i}"
        }).ToArray();
        var first = Create(contextA, new PausedUnit(contextA, started, release)).IngestBatchAsync(raw);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = Create(contextB, new UnitOfWork(contextB))
            .IngestBatchAsync(raw.Reverse().Select(x => x with { JobSourceId = sourceB.Id }).ToArray());
        Assert.False(second.IsCompleted);
        release.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(results[0], x => Assert.Equal(JobIngestionOutcome.Created, x.Outcome));
        Assert.All(results[1], x => Assert.Equal(JobIngestionOutcome.MatchedByUrl, x.Outcome));
        Assert.Equal(50, await seed.Jobs.CountAsync());
        Assert.All(await seed.Jobs.ToArrayAsync(), x => Assert.Equal(sourceA.Id, x.JobSourceId));
        Assert.Equal(locks.CreationAcquisitions, locks.CreationReleases);
    }

    [Fact]
    public async Task OwnedFingerprintDoesNotHideAManualMatchAfterOwnedUpdate()
    {
        using var f = new JobSourceFixture();
        var raw = Jobs(f, 1)[0];
        var fingerprint = new JobFingerprintService().GenerateFingerprint(raw.Title!, f.Company.Name, raw.Location);
        var owned = new Job { Title = raw.Title!, Company = f.Company, Category = f.Category, Location = raw.Location,
            Description = "Original", ApplicationUrl = raw.ApplicationUrl!, CanonicalApplicationUrlHash = ApplicationUrlIdentity.Hash(raw.ApplicationUrl),
            FingerprintHash = fingerprint, JobSourceId = f.Source.Id, ExternalJobId = raw.ExternalId, Status = JobStatus.Draft };
        var manual = new Job { Title = raw.Title!, Company = f.Company, Category = f.Category, Location = raw.Location,
            Description = "Manual", ApplicationUrl = "https://synthetic.example/manual", FingerprintHash = fingerprint, Status = JobStatus.Draft };
        f.Context.AddRange(owned, manual);
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        var (service, _, _) = Service(f);
        RawExternalJob[] changes = [raw with { Title = "Changed title" },
            raw with { ExternalId = "new", ApplicationUrl = "https://synthetic.example/new" }];
        var results = await service.IngestBatchAsync(changes);
        Assert.Equal(JobIngestionOutcome.Updated, results[0].Outcome);
        Assert.Equal(JobIngestionOutcome.MatchedByFingerprint, results[1].Outcome);
        Assert.Equal(manual.Id, results[1].JobId);
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
    }

    private sealed class PausedUnit(JobPortalDbContext context, TaskCompletionSource started, TaskCompletionSource release) : IUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await context.SaveChangesAsync(cancellationToken);
        }
        public void ResetAfterFailure() => context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ShutdownImmediatelyAfterCommitStillReportsEveryDurableOutcome()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Workday;
        await f.Context.SaveChangesAsync();
        var (ingestion, unit, _) = Service(f);
        using var cancellation = new CancellationTokenSource();
        unit.AfterSave = () =>
        {
            if (f.Context.ChangeTracker.Entries<Job>().Any()) cancellation.Cancel();
        };
        var progress = new JobSourceRunProgress();
        var runner = new JobSourceRunner(f.Repository, [new SingleSnapshotFeed(Jobs(f, 100))], ingestion,
            unit, TimeProvider.System, f.Resolver, new ExternalJobNormalizer(),
            jobRepository: new JobRepository(f.Context), runProgress: progress);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(f.Source.Id, cancellation.Token));
        Assert.Equal(100, await f.Context.Jobs.CountAsync());
        Assert.Equal(100, progress.Snapshot.Processed);
        Assert.Equal(100, progress.Snapshot.Counters.Created);
        Assert.Null((await f.Repository.GetByIdAsync(f.Source.Id))!.LastSuccessfulRunAtUtc);
        Assert.Equal(f.Locks.CreationAcquisitions, f.Locks.CreationReleases);
    }

    private sealed class SingleSnapshotFeed(RawExternalJob[] jobs) : ICompleteExternalJobProvider
    {
        public AtsType AtsType => AtsType.Workday;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<RawExternalJob>>(jobs);
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExternalJobSourceSnapshot(jobs, 0, true));
    }

    private static (JobIngestionService Service, CountingUnit Unit, LargeSourceIngestionTests.Probe<IJobRepository> Probe) Service(JobSourceFixture f,
        bool metadataOnlySql = false)
    {
        var proxy = metadataOnlySql ? DispatchProxy.Create<IJobRepository, MetadataSqlProbe>()
            : DispatchProxy.Create<IJobRepository, LargeSourceIngestionTests.Probe<IJobRepository>>();
        var probe = (LargeSourceIngestionTests.Probe<IJobRepository>)(object)proxy;
        probe.Inner = new JobRepository(f.Context);
        var unit = new CountingUnit(f);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        return (new(proxy, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(proxy, fingerprints, canonical), fingerprints, unit, TimeProvider.System, f.Locks, canonical), unit, probe);
    }

    public class MetadataSqlProbe : LargeSourceIngestionTests.Probe<IJobRepository>
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            // PostgreSQL ExecuteUpdate changes only the explicit timestamp columns
            // and bypasses EF auditing/tracking. InMemory cannot execute that SQL,
            // so this focused test spies on the selected write path instead.
            if (targetMethod?.Name == nameof(IJobRepository.TouchAggregationMetadataAsync))
            {
                Calls[targetMethod.Name] = Calls.GetValueOrDefault(targetMethod.Name) + 1;
                return Task.FromResult(args![0] is IReadOnlyCollection<Guid> ids ? ids.Count : 1);
            }
            return base.Invoke(targetMethod, args);
        }
    }

    private static RawExternalJob[] Jobs(JobSourceFixture f, int count) => Enumerable.Range(0, count).Select(i => new RawExternalJob
    {
        JobSourceId = f.Source.Id, ExternalId = $"perf-{i}", CompanyId = f.Company.Id,
        CompanyName = f.Company.Name, CategoryId = f.Category.Id, Title = $"Engineer {i}", Location = "Pune",
        Description = $"unique-description-{i}", ApplicationUrl = $"https://synthetic.example/jobs/{i}"
    }).ToArray();

    private static async Task WarmUpQueryPathsAsync()
    {
        // Separate disposable fixture: JIT/EF query compilation is not charged to
        // just whichever strategy xUnit happens to run first. No measured rows/calls
        // are shared, and these timings still do not represent PostgreSQL latency.
        using var f = new JobSourceFixture();
        var (service, _, _) = Service(f);
        var raw = Jobs(f, 8);
        await service.IngestBatchAsync(raw);
        foreach (var job in raw)
            await service.IngestAsync(job with { ExternalId = "warm-" + job.ExternalId,
                Title = "Warm " + job.Title, ApplicationUrl = job.ApplicationUrl + "/warm" });
        await service.IngestBatchAsync(raw);
        await service.CompleteRunAsync();
    }

    private sealed class CountingUnit(JobSourceFixture f) : IUnitOfWork
    {
        public int Saves;
        public int PeakTracked;
        public Action? BeforeSave;
        public Action? AfterSave;
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            BeforeSave?.Invoke();
            Saves++;
            PeakTracked = Math.Max(PeakTracked, f.Context.ChangeTracker.Entries<Job>().Count());
            var saved = await f.Context.SaveChangesAsync(cancellationToken);
            AfterSave?.Invoke();
            return saved;
        }
        public void ResetAfterFailure() => f.Context.ChangeTracker.Clear();
    }
}
