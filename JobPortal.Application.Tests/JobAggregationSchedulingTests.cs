using System.Collections.Concurrent;
using System.Threading.Channels;
using JobPortal.API.HostedServices;
using JobPortal.API.Services;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Infrastructure;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobAggregationDueSourceTests
{
    [Fact]
    public async Task UnsupportedProviderRecordsAttemptAndWaitsForScanInterval()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = JobPortal.Domain.Enums.AtsType.Custom;
        f.Source.ScanIntervalMinutes = 60;
        f.Source.LastSuccessfulRunAtUtc = JobSourceFixture.Now.AddDays(-1);
        await f.Context.SaveChangesAsync();
        var result = await f.Runner.RunAsync(f.Source.Id);
        Assert.False(result.Succeeded);
        Assert.Contains("No provider is registered", result.Error);
        Assert.Equal(JobSourceFixture.Now.AddDays(-1), f.Source.LastSuccessfulRunAtUtc);
        Assert.Empty(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now.AddMinutes(59), 25));
        Assert.Single(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now.AddMinutes(60), 25));
    }

    [Fact]
    public async Task NeverRunActiveSourceIsDueAndUntracked()
    {
        using var f = new JobSourceFixture();
        f.Context.ChangeTracker.Clear();
        var due = await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, 25);
        Assert.Equal(f.Source.Id, Assert.Single(due).Id);
        Assert.Empty(f.Context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(61, true)]
    [InlineData(60, true)]
    [InlineData(59, false)]
    public async Task DueBoundaryUsesLastAttemptRatherThanLastSuccess(int minutesAgo, bool due)
    {
        using var f = new JobSourceFixture();
        f.Source.ScanIntervalMinutes = 60;
        f.Source.LastRunAtUtc = JobSourceFixture.Now.AddMinutes(-minutesAgo);
        f.Source.LastSuccessfulRunAtUtc = null;
        f.Source.ConsecutiveFailures = 3;
        await f.Context.SaveChangesAsync();
        Assert.Equal(due ? 1 : 0, (await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, 25)).Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task InactiveOrSoftDeletedSourceIsExcluded(bool active, bool deleted)
    {
        using var f = new JobSourceFixture();
        f.Source.IsActive = active;
        f.Source.IsDeleted = deleted;
        await f.Context.SaveChangesAsync();
        Assert.Empty(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, 25));
    }

    [Fact]
    public async Task OrderingIsNeverRunThenOldestAttemptThenIdAndBatchIsBounded()
    {
        using var f = new JobSourceFixture();
        f.Context.JobSources.Remove(f.Source);
        var neverLow = Source(f.Company.Id, new Guid("00000000-0000-0000-0000-000000000001"));
        var neverHigh = Source(f.Company.Id, new Guid("00000000-0000-0000-0000-000000000002"));
        var oldest = Source(f.Company.Id, Guid.NewGuid(), JobSourceFixture.Now.AddHours(-3));
        var newer = Source(f.Company.Id, Guid.NewGuid(), JobSourceFixture.Now.AddHours(-2));
        f.Context.AddRange(newer, neverHigh, oldest, neverLow);
        await f.Context.SaveChangesAsync();
        var due = await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, 3);
        Assert.Equal(new[] { neverLow.Id, neverHigh.Id, oldest.Id }, due.Select(x => x.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvalidRepositoryBatchIsRejected(int batch)
    {
        using var f = new JobSourceFixture();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, batch));
    }

    [Fact]
    public void ActualDueQueryTranslatesToPostgresWithoutOpeningConnection()
    {
        using var context = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only").Options);
        var sql = new JobSourceRepository(context).DueSourcesQuery(JobSourceFixture.Now, 25).ToQueryString();
        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        var predicate = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.Contains("\"ScanIntervalMinutes\"", predicate, StringComparison.Ordinal);
        Assert.Contains("\"LastRunAtUtc\"", predicate, StringComparison.Ordinal);
        Assert.DoesNotContain("LastSuccessfulRunAtUtc", predicate, StringComparison.Ordinal);
    }

    private static JobSource Source(Guid companyId, Guid id, DateTime? lastRun = null) => new()
    {
        Id = id, CompanyId = companyId, CareerPageUrl = "https://example.test/careers",
        ScanIntervalMinutes = 60, LastRunAtUtc = lastRun
    };
}

public sealed class JobAggregationSchedulerTests
{
    [Fact]
    public async Task DisabledDoesNotQueryOrResolveRunner()
    {
        using var f = new SchedulerFixture(enabled: false);
        await f.Scheduler.RunOnceAsync();
        Assert.Equal(0, f.Store.QueryCalls);
        Assert.Empty(f.Store.Ran);
        Assert.Equal(0, f.Store.CreatedScopes);
    }

    [Fact]
    public async Task ProcessesBoundedDueBatchUsingScopedExistingRunner()
    {
        using var f = new SchedulerFixture(batchSize: 2);
        f.Store.AddSources(5);
        await f.Scheduler.RunOnceAsync();
        Assert.Equal(2, f.Store.RequestedBatch);
        Assert.Equal(JobSourceFixture.Now, f.Store.QueriedAt);
        Assert.Equal(2, f.Store.Ran.Count);
        Assert.Equal(3, f.Store.CreatedScopes); // query + one independent context per source
        Assert.Equal(f.Store.CreatedScopes, f.Store.DisposedScopes);
    }

    [Fact]
    public async Task EmptyIterationIsQuiet()
    {
        using var f = new SchedulerFixture();
        await f.Scheduler.RunOnceAsync();
        Assert.Equal(1, f.Store.QueryCalls);
        Assert.Empty(f.Store.Ran);
        Assert.Empty(f.Logger.Messages);
    }

    [Fact]
    public async Task SourceExceptionsAndFailureResultsDoNotStopOtherSourcesOrLeakSecrets()
    {
        using var f = new SchedulerFixture(concurrency: 1);
        f.Store.AddSources(3);
        var first = f.Store.Sources[0].Id;
        var second = f.Store.Sources[1].Id;
        f.Store.Run = (id, _) => id == first ? throw new InvalidOperationException("secret connection string") :
            Task.FromResult(new JobSourceRunResult { JobSourceId = id, Succeeded = id != second, Error = "secret response" });
        await f.Scheduler.RunOnceAsync();
        Assert.Equal(3, f.Store.Ran.Count);
        Assert.DoesNotContain(f.Logger.Messages, x => x.Contains("secret", StringComparison.Ordinal));
        Assert.All(f.Logger.Exceptions, Assert.Null);
        foreach (var source in f.Store.Sources) { using var lease = f.Guard.Acquire(source.Id); }
    }

    [Fact]
    public async Task QueryFailureIsContainedAndNextIterationRetries()
    {
        using var f = new SchedulerFixture();
        f.Store.AddSources(1);
        f.Store.ThrowQuery = true;
        await f.Scheduler.RunOnceAsync();
        Assert.Empty(f.Store.Ran);
        Assert.DoesNotContain(f.Logger.Messages, x => x.Contains("secret", StringComparison.Ordinal));
        f.Store.ThrowQuery = false;
        await f.Scheduler.RunOnceAsync();
        Assert.Single(f.Store.Ran);
    }

    [Fact]
    public async Task ParallelismIsBoundedAndDifferentSourcesCanRunTogether()
    {
        using var f = new SchedulerFixture(concurrency: 2);
        f.Store.AddSources(5);
        var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var peak = 0;
        f.Store.Run = async (id, token) =>
        {
            var count = Interlocked.Increment(ref active);
            Interlocked.Exchange(ref peak, Math.Max(Volatile.Read(ref peak), count));
            if (count == 2) twoStarted.TrySetResult();
            try { await release.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref active); }
            return new() { JobSourceId = id, Succeeded = true };
        };
        var iteration = f.Scheduler.RunOnceAsync();
        try
        {
            await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, f.Store.Ran.Count);
            Assert.Equal(2, Volatile.Read(ref peak));
        }
        finally { release.TrySetResult(); }
        await iteration;
        Assert.Equal(5, f.Store.Ran.Count);
        Assert.InRange(peak, 2, 2);
    }

    [Fact]
    public async Task CancellationPropagatesReleasesGuardAndDisposesScopes()
    {
        using var f = new SchedulerFixture();
        f.Store.AddSources(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.Run = async (id, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new() { JobSourceId = id };
        };
        using var cancellation = new CancellationTokenSource();
        var iteration = f.Scheduler.RunOnceAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => iteration);
        using var lease = f.Guard.Acquire(f.Store.Sources[0].Id);
        Assert.Equal(f.Store.CreatedScopes, f.Store.DisposedScopes);
    }

    [Fact]
    public async Task ManualAndScheduledRunsShareGuardInBothDirections()
    {
        using var manual = new JobSourceFixture();
        using var f = new SchedulerFixture(guard: manual.Guard);
        f.Store.Sources.Add(manual.Source);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = new DelegateRunner(async (id, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return new() { JobSourceId = id, Succeeded = true };
        });
        var manualRun = manual.CreateService(blocking).RunAsync(manual.Source.Id);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Scheduler.RunOnceAsync();
        Assert.Empty(f.Store.Ran);
        release.SetResult();
        await manualRun;

        var scheduledStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduledRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.Run = async (id, token) =>
        {
            scheduledStarted.TrySetResult();
            await scheduledRelease.Task.WaitAsync(token);
            return new() { JobSourceId = id, Succeeded = true };
        };
        var scheduledRun = f.Scheduler.RunOnceAsync();
        try
        {
            await scheduledStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<ConflictException>(() => manual.Service.RunAsync(manual.Source.Id));
        }
        finally { scheduledRelease.TrySetResult(); }
        await scheduledRun;
        using var lease = manual.Guard.Acquire(manual.Source.Id);
    }

    [Theory]
    [InlineData("recent")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    public async Task StaleDueSelectionIsRecheckedAfterGuard(string change)
    {
        using var f = new SchedulerFixture();
        f.Store.AddSources(1);
        f.Store.BeforeReload = source =>
        {
            if (change == "recent") source.LastRunAtUtc = JobSourceFixture.Now;
            if (change == "inactive") source.IsActive = false;
            if (change == "deleted") source.IsDeleted = true;
        };
        await f.Scheduler.RunOnceAsync();
        Assert.Empty(f.Store.Ran);
    }

    private sealed class DelegateRunner(Func<Guid, CancellationToken, Task<JobSourceRunResult>> action) : IJobSourceRunner
    {
        public Task<JobSourceRunResult> RunAsync(Guid jobSourceId, CancellationToken cancellationToken = default) => action(jobSourceId, cancellationToken);
    }
}

public sealed class JobAggregationSchedulerConfigurationTests
{
    [Fact]
    public void DefaultsAreDisabledAndBounded()
    {
        using var provider = BuildOptions();
        var options = provider.GetRequiredService<IOptions<JobAggregationOptions>>().Value.Scheduler;
        Assert.False(options.Enabled);
        Assert.Equal(60, options.PollIntervalSeconds);
        Assert.Equal(25, options.BatchSize);
        Assert.Equal(3, options.MaxConcurrentSources);
    }

    [Theory]
    [InlineData("PollIntervalSeconds", "9")]
    [InlineData("PollIntervalSeconds", "3601")]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "101")]
    [InlineData("MaxConcurrentSources", "0")]
    [InlineData("MaxConcurrentSources", "11")]
    public void InvalidConfigurationIsRejected(string key, string value)
    {
        using var provider = BuildOptions(key, value);
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<JobAggregationOptions>>().Value);
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    private static ServiceProvider BuildOptions(string? key = null, string? value = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            key is null ? [] : new Dictionary<string, string?> { [$"JobAggregation:Scheduler:{key}"] = value }).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}

public sealed class JobAggregationSchedulerHostedServiceTests
{
    [Fact]
    public async Task DisabledHostNeverQueries()
    {
        using var f = new SchedulerFixture(enabled: false);
        using var host = f.Host();
        await host.StartAsync(default);
        await host.StopAsync(default);
        Assert.Equal(0, f.Store.QueryCalls);
    }

    [Fact]
    public async Task PollingUsesTimeProviderAndShutdownCancelsDelay()
    {
        using var f = new SchedulerFixture();
        using var host = f.Host();
        await host.StartAsync(default);
        var timer = await f.Clock.Timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(60), timer.DueTime);
        Assert.Equal(1, f.Store.QueryCalls);
        timer.Fire();
        _ = await f.Clock.Timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, f.Store.QueryCalls);
        await host.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }
}

internal sealed class SchedulerFixture : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IOptions<JobAggregationOptions> options;
    public SchedulerStore Store { get; } = new();
    public ControlledClock Clock { get; } = new();
    public JobSourceRunGuard Guard { get; }
    public SafeLogger Logger { get; } = new();
    public JobAggregationScheduler Scheduler { get; }

    public SchedulerFixture(bool enabled = true, int batchSize = 25, int concurrency = 3, JobSourceRunGuard? guard = null, IJobSourceExecutionLock? executionLock = null)
    {
        Guard = guard ?? new();
        options = Options.Create(new JobAggregationOptions { Scheduler = new() { Enabled = enabled, BatchSize = batchSize, MaxConcurrentSources = concurrency } });
        var services = new ServiceCollection();
        services.AddSingleton(Store);
        services.AddSingleton<IJobSourceExecutionLock>(executionLock ?? new TestAggregationLocks());
        services.AddScoped<ScopeMarker>();
        services.AddScoped<IJobSourceRepository, ScopedRepository>();
        services.AddScoped<IJobSourceRunner, ScopedRunner>();
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Scheduler = new(provider.GetRequiredService<IServiceScopeFactory>(), options, Guard, Clock, Logger);
    }

    public JobAggregationSchedulerHostedService Host() => new(Scheduler, options, Clock, NullLogger<JobAggregationSchedulerHostedService>.Instance);
    public void Dispose() => provider.Dispose();

    internal sealed class SchedulerStore
    {
        public List<JobSource> Sources { get; } = [];
        public ConcurrentBag<Guid> Ran { get; } = [];
        public int QueryCalls;
        public int CreatedScopes;
        public int DisposedScopes;
        public int RequestedBatch;
        public DateTime QueriedAt;
        public bool ThrowQuery { get; set; }
        public Action<JobSource>? BeforeReload { get; set; }
        public Func<Guid, CancellationToken, Task<JobSourceRunResult>> Run { get; set; } =
            (id, _) => Task.FromResult(new JobSourceRunResult { JobSourceId = id, Succeeded = true });
        public void AddSources(int count)
        {
            for (var i = 0; i < count; i++) Sources.Add(new JobSource { ScanIntervalMinutes = 60 });
        }
    }

    private sealed class ScopeMarker : IDisposable
    {
        private readonly SchedulerStore store;
        public ScopeMarker(SchedulerStore store) { this.store = store; Interlocked.Increment(ref store.CreatedScopes); }
        public void Dispose() { Interlocked.Increment(ref store.DisposedScopes); }
    }

    private sealed class ScopedRepository(SchedulerStore store, ScopeMarker marker) : IJobSourceRepository
    {
        public async Task<IReadOnlyCollection<JobSource>> GetDueSourcesAsync(DateTime nowUtc, int maxResults, CancellationToken cancellationToken = default)
        {
            _ = marker;
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref store.QueryCalls);
            store.RequestedBatch = maxResults;
            store.QueriedAt = nowUtc;
            if (store.ThrowQuery) throw new InvalidOperationException("secret database connection");
            return await Task.FromResult(store.Sources.Take(maxResults).ToArray());
        }
        public Task<JobSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var source = store.Sources.SingleOrDefault(x => x.Id == id);
            if (source is not null) store.BeforeReload?.Invoke(source);
            return Task.FromResult(source);
        }
        public void Update(JobSource source) => throw new InvalidOperationException("Scheduler must delegate state changes to the runner.");
    }

    private sealed class ScopedRunner(SchedulerStore store, ScopeMarker marker) : IJobSourceRunner
    {
        public Task<JobSourceRunResult> RunAsync(Guid jobSourceId, CancellationToken cancellationToken = default)
        {
            _ = marker;
            store.Ran.Add(jobSourceId);
            return store.Run(jobSourceId, cancellationToken);
        }
    }

    internal sealed class SafeLogger : ILogger<JobAggregationScheduler>
    {
        public ConcurrentBag<string> Messages { get; } = [];
        public ConcurrentBag<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Messages.Add(formatter(state, exception)); Exceptions.Add(exception); }
    }

    internal sealed class ControlledClock : TimeProvider
    {
        public Channel<ControlledTimer> Timers { get; } = Channel.CreateUnbounded<ControlledTimer>();
        public override DateTimeOffset GetUtcNow() => new(JobSourceFixture.Now);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(callback, state, dueTime);
            Timers.Writer.TryWrite(timer);
            return timer;
        }
    }

    internal sealed class ControlledTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        public TimeSpan DueTime { get; } = dueTime;
        public void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
