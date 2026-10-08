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

public sealed class JobSourceInterruptedRunTests
{
    [Theory]
    [InlineData(true, 59, false)]
    [InlineData(true, 60, true)]
    [InlineData(true, 61, true)]
    [InlineData(false, 59, false)]
    [InlineData(false, 60, true)]
    [InlineData(false, 61, true)]
    public async Task AutomaticSchedulerUsesCompletionIntervalOrInterruptedCooldown(bool succeeded, int elapsed, bool due)
    {
        using var scheduler = new SchedulerFixture();
        var lastOutcome = JobSourceFixture.Now.AddMinutes(-elapsed);
        scheduler.Store.Sources.Add(new JobSource
        {
            ScanIntervalMinutes = succeeded ? 60 : 5,
            LastRunAtUtc = succeeded ? lastOutcome.AddMinutes(-20) : lastOutcome,
            LastSuccessfulRunAtUtc = succeeded ? lastOutcome : null
        });
        await scheduler.Scheduler.RunOnceAsync();
        Assert.Equal(due ? 1 : 0, scheduler.Store.Ran.Count);
    }

    [Fact]
    public async Task AttemptIsDurableBeforeFetchAndCancellationPreservesPreviousOutcome()
    {
        using var f = new JobSourceFixture();
        var previous = JobSourceFixture.Now.AddDays(-2);
        f.Source.LastSuccessfulRunAtUtc = previous;
        f.Source.LastError = "Previous safe failure";
        f.Source.ConsecutiveFailures = 2;
        await f.Context.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var provider = new SnapshotProvider(async (_, token) =>
        {
            var saved = await f.Context.JobSources.AsNoTracking().SingleAsync(token);
            Assert.Equal(JobSourceFixture.Now, saved.LastRunAtUtc);
            Assert.Equal(previous, saved.LastSuccessfulRunAtUtc);
            Assert.Equal(2, saved.ConsecutiveFailures);
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return new([], 0, true);
        });
        var runner = Runner(f, provider, new Clock(JobSourceFixture.Now));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.CreateService(runner).RunAsync(f.Source.Id, cancellation.Token));
        var persisted = await f.Context.JobSources.AsNoTracking().SingleAsync();
        Assert.Equal(JobSourceFixture.Now, persisted.LastRunAtUtc);
        Assert.Equal(previous, persisted.LastSuccessfulRunAtUtc);
        Assert.Equal("Previous safe failure", persisted.LastError);
        Assert.Equal(2, persisted.ConsecutiveFailures);
        Assert.Equal(1, f.Locks.SourceReleases);
        var status = await f.Service.GetByIdAsync(f.Source.Id);
        Assert.Equal(persisted.LastRunAtUtc, status.LastRunAtUtc);
    }

    [Fact]
    public async Task CompletionTimestampDrivesSuccessfulScheduleNotAttemptStart()
    {
        using var f = new JobSourceFixture();
        f.Source.ScanIntervalMinutes = 1440;
        await f.Context.SaveChangesAsync();
        var clock = new Clock(JobSourceFixture.Now);
        var provider = new SnapshotProvider((_, _) =>
        {
            clock.Now = clock.Now.AddMinutes(20);
            return Task.FromResult(new ExternalJobSourceSnapshot([], 0, true));
        });
        Assert.True((await f.CreateService(Runner(f, provider, clock)).RunAsync(f.Source.Id)).Succeeded);
        var saved = await f.Context.JobSources.AsNoTracking().SingleAsync();
        Assert.Equal(JobSourceFixture.Now, saved.LastRunAtUtc);
        Assert.Equal(JobSourceFixture.Now.AddMinutes(20), saved.LastSuccessfulRunAtUtc);
        Assert.Empty(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now.AddMinutes(1440), 25));
        Assert.Single(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now.AddMinutes(1460), 25));
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public async Task RestartCooldownAppliesDespiteOldOrMissingSuccess(int elapsedMinutes, bool due)
    {
        using var f = new JobSourceFixture();
        f.Source.ScanIntervalMinutes = 5;
        f.Source.LastRunAtUtc = JobSourceFixture.Now;
        await f.Context.SaveChangesAsync();
        var now = JobSourceFixture.Now.AddMinutes(elapsedMinutes);
        Assert.Equal(due ? 1 : 0, (await f.Repository.GetDueSourcesAsync(now, 25)).Count);
        Assert.Equal(due, JobSourceSchedule.DuePredicate(now, 60).Compile()(f.Source));
        f.Source.LastSuccessfulRunAtUtc = JobSourceFixture.Now.AddDays(-1);
        await f.Context.SaveChangesAsync();
        Assert.Equal(due ? 1 : 0, (await f.Repository.GetDueSourcesAsync(now, 25)).Count);
    }

    [Fact]
    public async Task ConfiguredCooldownAndExistingFailureScanIntervalAreBothRespected()
    {
        using var f = new JobSourceFixture();
        f.Source.LastRunAtUtc = JobSourceFixture.Now;
        f.Source.ConsecutiveFailures = 9;
        f.Source.ScanIntervalMinutes = 120;
        await f.Context.SaveChangesAsync();
        Assert.Empty(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now.AddMinutes(60), 25));
        Assert.Single(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now.AddMinutes(120), 25));
        var repository = new JobSourceRepository(f.Context, Options.Create(new JobAggregationOptions
            { Scheduler = new() { InterruptedRunCooldownMinutes = 180 } }));
        Assert.Empty(await repository.GetDueSourcesAsync(JobSourceFixture.Now.AddMinutes(179), 25));
        Assert.Single(await repository.GetDueSourcesAsync(JobSourceFixture.Now.AddMinutes(180), 25));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualRunWorksDuringCooldownRegardlessOfSchedulerFlag(bool enabled)
    {
        using var f = new JobSourceFixture();
        f.Source.LastRunAtUtc = JobSourceFixture.Now;
        f.Source.ScanIntervalMinutes = 1440;
        await f.Context.SaveChangesAsync();
        using var scheduler = new SchedulerFixture(enabled: enabled);
        scheduler.Store.Sources.Add(f.Source);
        await scheduler.Scheduler.RunOnceAsync();
        Assert.Empty(scheduler.Store.Ran);
        var provider = new SnapshotProvider((_, _) => Task.FromResult(new ExternalJobSourceSnapshot([], 0, true)));
        Assert.True((await f.CreateService(Runner(f, provider, new Clock(JobSourceFixture.Now))).RunAsync(f.Source.Id)).Succeeded);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task CancellationDuringReconciliationDiscardsStagedClosuresAndSuccess()
    {
        using var f = new JobSourceFixture();
        var job = new Job { CompanyId = f.Company.Id, CategoryId = f.Category.Id, JobSourceId = f.Source.Id,
            ExternalJobId = "old", Title = "Existing imported job", Description = "Test", Status = JobStatus.Published };
        f.Context.Add(job);
        await f.Context.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var jobs = LargeSourceIngestionTests.MeasureRepository(new JobRepository(f.Context), () => cancellation.Cancel());
        var provider = new SnapshotProvider((_, _) => Task.FromResult(new ExternalJobSourceSnapshot([], 0, true)));
        var runner = Runner(f, provider, new Clock(JobSourceFixture.Now), jobs);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.CreateService(runner).RunAsync(f.Source.Id, cancellation.Token));
        var saved = await f.Context.Jobs.AsNoTracking().SingleAsync();
        Assert.Equal(JobStatus.Published, saved.Status);
        Assert.Null((await f.Context.JobSources.AsNoTracking().SingleAsync()).LastSuccessfulRunAtUtc);
        Assert.Equal(0, (await f.Context.JobSources.AsNoTracking().SingleAsync()).ConsecutiveFailures);
    }

    private static JobSourceRunner Runner(JobSourceFixture f, SnapshotProvider provider, TimeProvider clock,
        JobPortal.Application.Abstractions.Persistence.IJobRepository? jobs = null) =>
        new(f.Repository, [provider], new NeverIngest(), new UnitOfWork(f.Context), clock,
            f.Resolver, new ExternalJobNormalizer(), jobRepository: jobs ?? new JobRepository(f.Context));
    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class SnapshotProvider(Func<JobSource, CancellationToken, Task<ExternalJobSourceSnapshot>> fetch) : ICompleteExternalJobProvider
    {
        public int Calls;
        public AtsType AtsType => AtsType.Greenhouse;
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default)
        { Calls++; return fetch(source, cancellationToken); }
        public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) =>
            (await FetchSnapshotAsync(source, cancellationToken)).Jobs;
    }
    private sealed class NeverIngest : IJobIngestionService
    {
        public Task<JobIngestionResult> IngestAsync(RawExternalJob rawJob, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Empty snapshots must not ingest jobs.");
    }
}
