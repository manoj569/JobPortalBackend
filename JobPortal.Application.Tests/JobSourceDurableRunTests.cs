using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using JobPortal.API.Controllers;
using JobPortal.API.HostedServices;
using JobPortal.API.Services;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobSourceDurableRunTests
{
    [Fact]
    public async Task HttpRunReturns202AndLocationWithoutCallingProviderAndDisconnectCannotCancelWorker()
    {
        using var f = new Fixture();
        using var old = new JobSourceFixture();
        using var disconnected = new CancellationTokenSource();
        var actor = Guid.NewGuid();
        var controller = new AdminJobSourcesController(old.Service, f.Service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "test")) } }
        };
        var action = await controller.Run(f.Source.Id, disconnected.Token);
        var accepted = Assert.IsType<AcceptedAtActionResult>(action.Result);
        Assert.Equal(202, accepted.StatusCode);
        Assert.Equal(nameof(AdminJobSourcesController.GetRun), accepted.ActionName);
        var data = Assert.IsType<ApiResponse<JobSourceRunResponse>>(accepted.Value).Data;
        Assert.Equal("Queued", data.Status);
        Assert.Equal(data.RunId, accepted.RouteValues!["runId"]);
        Assert.Equal(actor, f.Store.Rows.Single().RequestedByUserId);
        Assert.Equal(0, f.Calls);
        disconnected.Cancel();
        await f.Worker().RunOnceAsync(default);
        Assert.Equal("Succeeded", (await f.Service.GetAsync(f.Source.Id, data.RunId, default)).Status);
        Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task DuplicateRequestsReuseQueuedAndRunningRun()
    {
        using var f = new Fixture();
        var requests = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => f.Service.EnqueueAsync(f.Source.Id, null, default)));
        var runId = Assert.Single(requests.Select(x => x.RunId).Distinct());
        var started = Signal(); var release = Signal();
        f.Run = async (_, ct) => { started.TrySetResult(); await release.Task.WaitAsync(ct); return Success(f.Source.Id); };
        var worker = f.Worker().RunOnceAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var duplicate = await f.Service.EnqueueAsync(f.Source.Id, null, default);
            Assert.Equal(runId, duplicate.RunId); Assert.Equal("Running", duplicate.Status);
            Assert.Single(f.Store.Rows);
        }
        finally { release.TrySetResult(); }
        await worker;
    }

    [Fact]
    public async Task MultipleInstancesCannotExecuteSameRunEvenWithDifferentLocalGuards()
    {
        using var f = new Fixture();
        await f.Enqueue();
        var started = Signal(); var release = Signal();
        f.Run = async (_, ct) => { started.TrySetResult(); await release.Task.WaitAsync(ct); return Success(f.Source.Id); };
        var first = f.Worker(new()).RunOnceAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await f.Worker(new()).RunOnceAsync(default); Assert.Equal(1, f.Calls); }
        finally { release.TrySetResult(); }
        await first;
        Assert.Equal(JobSourceRunStatus.Succeeded, Assert.Single(f.Store.Rows).Status);
    }

    [Fact]
    public async Task ExpiredRunningRecoveryRequiresSourceLockPersistsInterruptedAndWaitsForCooldown()
    {
        using var f = new Fixture();
        var run = await f.Enqueue();
        var claim = await f.Store.TryClaimAsync(run.RunId, Guid.NewGuid(), f.Clock.Now, f.Clock.Now.AddSeconds(120), default);
        f.Clock.Now = f.Clock.Now.AddSeconds(121);
        f.Locks.Busy = true;
        await f.Worker().RunOnceAsync(default);
        Assert.Equal(JobSourceRunStatus.Running, f.Store.Rows.Single().Status);
        f.Locks.Busy = false;
        await f.Worker().RunOnceAsync(default);
        var interrupted = f.Store.Rows.Single();
        Assert.Equal(JobSourceRunStatus.Interrupted, interrupted.Status);
        Assert.Equal(f.Clock.Now.AddMinutes(60), interrupted.NextAttemptAtUtc);
        Assert.Equal(0, f.Calls);
        Assert.False(await f.Store.HeartbeatAsync(claim!, f.Clock.Now, f.Clock.Now.AddMinutes(2), new("Ingestion", 0, new()), default));
        Assert.False(await f.Store.FinishAsync(claim!, JobSourceRunStatus.Succeeded, f.Clock.Now, f.Clock.Now, new("Finalizing", 0, new()), default));
        await f.Worker().RunOnceAsync(default); Assert.Equal(0, f.Calls);
        f.Clock.Now = interrupted.NextAttemptAtUtc;
        await f.Worker(new()).RunOnceAsync(default);
        Assert.Equal(JobSourceRunStatus.Succeeded, interrupted.Status);
        Assert.Equal(2, interrupted.AttemptCount);
        Assert.NotNull(interrupted.InterruptedAtUtc);
    }

    [Fact]
    public async Task RepeatedCrashesExhaustThreeAttemptsWithoutAnImmediateInfiniteRetry()
    {
        using var f = new Fixture();
        var response = await f.Enqueue();
        for (var i = 1; i <= JobSourceRun.MaximumAttempts; i++)
        {
            Assert.NotNull(await f.Store.TryClaimAsync(response.RunId, Guid.NewGuid(), f.Clock.Now, f.Clock.Now.AddSeconds(120), default));
            f.Clock.Now = f.Clock.Now.AddSeconds(121);
            await f.Worker().RunOnceAsync(default);
            Assert.Equal(i, f.Store.Rows.Single().AttemptCount);
            f.Clock.Now = f.Store.Rows.Single().NextAttemptAtUtc;
        }
        await f.Worker().RunOnceAsync(default);
        Assert.Equal(0, f.Calls);
        var exhausted = await f.Service.GetAsync(f.Source.Id, response.RunId, default);
        Assert.Equal("Interrupted", exhausted.Status); Assert.False(exhausted.RetryPending);
        Assert.NotNull(exhausted.CompletedAtUtc);
        Assert.NotEqual(response.RunId, (await f.Enqueue()).RunId); // Explicit admin retry remains possible.
    }

    [Fact]
    public async Task ShutdownPersistsProgressAndRetryReusesPreviouslyCommittedIdentities()
    {
        using var f = new Fixture();
        await f.Enqueue();
        var persistedIdentities = new HashSet<int>();
        var started = Signal();
        f.Run = async (progress, ct) =>
        {
            foreach (var id in Enumerable.Range(1, 475)) persistedIdentities.Add(id);
            progress.Report(new("Ingestion", 475, new() { TotalReceived = 1500, Created = 475 }));
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Success(f.Source.Id);
        };
        using var stop = new CancellationTokenSource();
        var worker = f.Worker().RunOnceAsync(stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel(); await worker;
        var row = Assert.Single(f.Store.Rows);
        Assert.Equal(JobSourceRunStatus.Interrupted, row.Status);
        Assert.Equal(475, row.Created); Assert.Equal(475, row.Processed);
        Assert.Equal(1500, row.TotalReceived); Assert.Null(row.LeaseOwner);
        f.Clock.Now = row.NextAttemptAtUtc;
        f.Run = (progress, _) =>
        {
            var inserted = Enumerable.Range(1, 1500).Count(persistedIdentities.Add);
            var result = Success(f.Source.Id) with { TotalReceived = 1500, Created = inserted, Updated = 475 };
            progress.Report(new("Ingestion", 1500, result));
            return Task.FromResult(result);
        };
        await f.Worker(new()).RunOnceAsync(default);
        Assert.Equal(1500, persistedIdentities.Count);
        Assert.Equal(JobSourceRunStatus.Succeeded, row.Status);
        Assert.Equal(1025, row.Created); Assert.Equal(475, row.Updated);
    }

    [Fact]
    public async Task HeartbeatRenewsDuringLongFetchAndLostLeaseCancelsExecution()
    {
        using var f = new Fixture();
        await f.Enqueue();
        var started = Signal();
        f.Run = async (_, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return Success(f.Source.Id); };
        var worker = f.Worker().RunOnceAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var timer = await f.Clock.Timers.Reader.ReadAsync();
        Assert.Equal(TimeSpan.FromSeconds(20), timer.Due);
        f.Clock.Now = f.Clock.Now.AddSeconds(20); timer.Fire();
        var next = await f.Clock.Timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(f.Clock.Now.AddSeconds(120), f.Store.Rows.Single().LeaseExpiresAtUtc);
        f.Store.HeartbeatLost = true;
        next.Fire();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(JobSourceRunStatus.Interrupted, f.Store.Rows.Single().Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedResultOrExceptionIsTerminalAndNeverPersistsSensitiveError(bool throws)
    {
        using var f = new Fixture();
        await f.Enqueue();
        f.Run = (_, _) => throws ? throw new InvalidDataException("SECRET HTML token") :
            Task.FromResult(new JobSourceRunResult { Error = "SECRET HTML token", Succeeded = false });
        await f.Worker().RunOnceAsync(default);
        var row = f.Store.Rows.Single();
        Assert.Equal(JobSourceRunStatus.Failed, row.Status); Assert.Equal("source_run_failed", row.ErrorCode);
        Assert.NotNull(row.CompletedAtUtc);
        f.Clock.Now = f.Clock.Now.AddDays(1);
        await f.Worker().RunOnceAsync(default); Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task InfrastructureFailureResultPreservesCountersFromAlreadyCommittedItems()
    {
        using var f = new Fixture(); await f.Enqueue();
        f.Run = (progress, _) =>
        {
            progress.Report(new("Ingestion", 475, new() { TotalReceived = 1500, Created = 475 }));
            return Task.FromResult(new JobSourceRunResult { Succeeded = false, Error = "External job source run failed." });
        };
        await f.Worker().RunOnceAsync(default);
        var row = f.Store.Rows.Single();
        Assert.Equal(JobSourceRunStatus.Failed, row.Status);
        Assert.Equal(475, row.Processed); Assert.Equal(475, row.Created); Assert.Equal(1500, row.TotalReceived);
    }

    [Fact]
    public async Task SchedulerSkipsQueuedManualRunAfterAcquiringSharedLockEvenForStaleDueSelection()
    {
        using var f = new Fixture(); await f.Enqueue();
        var scheduler = new JobAggregationScheduler(f.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new JobAggregationOptions { Scheduler = new() { Enabled = true } }), new(), f.Clock,
            NullLogger<JobAggregationScheduler>.Instance);
        await scheduler.RunOnceAsync(); Assert.Equal(0, f.Calls);
        // Manual worker has no Scheduler.Enabled check (default is false).
        await f.Worker().RunOnceAsync(default); Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task CancelledEnqueueDoesNotPersistAndStatusCannotUseWrongSourceId()
    {
        using var f = new Fixture();
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.EnqueueAsync(f.Source.Id, null, ct.Token));
        Assert.Empty(f.Store.Rows);
        var row = await f.Enqueue();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(Guid.NewGuid(), row.RunId, default));
        f.Source.IsActive = false;
        await Assert.ThrowsAsync<BadRequestException>(() => f.Enqueue());
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.EnqueueAsync(Guid.NewGuid(), null, default));
    }

    [Fact]
    public async Task RealDueRepositoryExcludesQueuedInterruptedAndRunningManualSources()
    {
        using var f = new JobSourceFixture();
        var row = new JobSourceRun { JobSourceId = f.Source.Id, QueuedAtUtc = JobSourceFixture.Now, NextAttemptAtUtc = JobSourceFixture.Now };
        f.Context.JobSourceRuns.Add(row); await f.Context.SaveChangesAsync();
        foreach (var status in new[] { JobSourceRunStatus.Queued, JobSourceRunStatus.Running, JobSourceRunStatus.Interrupted })
        {
            row.Status = status; row.AttemptCount = 1; await f.Context.SaveChangesAsync();
            Assert.Empty(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, 25));
        }
        row.AttemptCount = 3; await f.Context.SaveChangesAsync();
        Assert.Single(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, 25));
    }

    [Fact]
    public void DatabaseModelHasActiveUniquenessLeaseGuardsAndBoundedServerSideClaimCandidates()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=translation_only").Options);
        var entity = db.Model.FindEntityType(typeof(JobSourceRun))!;
        var active = Assert.Single(entity.GetIndexes(), x => x.GetDatabaseName() == "UX_JobSourceRuns_ActiveSource");
        Assert.True(active.IsUnique); Assert.Contains("\"Status\" IN (0, 1)", active.GetFilter());
        var sql = new JobSourceRunStore(db).CandidatesQuery(JobSourceFixture.Now).ToQueryString();
        Assert.Contains("LIMIT", sql); Assert.Contains("LeaseExpiresAtUtc", sql); Assert.Contains("ORDER BY", sql);
        Assert.Equal(2, entity.GetForeignKeys().Count());
    }

    private static JobSourceRunResult Success(Guid id) => new() { JobSourceId = id, Succeeded = true };
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IDisposable
    {
        public JobSource Source { get; } = new() { IsActive = true, ScanIntervalMinutes = 5 };
        public Store Store { get; } = new();
        public Clock Clock { get; } = new();
        public TestAggregationLocks Locks { get; } = new();
        public ServiceProvider Services { get; }
        public JobSourceRunService Service { get; }
        public int Calls;
        public Func<IJobSourceRunProgress, CancellationToken, Task<JobSourceRunResult>> Run { get; set; }
        public Fixture()
        {
            Run = (_, _) => Task.FromResult(Success(Source.Id));
            var repo = new Sources(Source);
            Service = new(repo, Store, Clock);
            var services = new ServiceCollection();
            services.AddSingleton<IJobSourceRunStore>(Store);
            services.AddSingleton<IJobSourceRepository>(repo);
            services.AddSingleton<IJobSourceExecutionLock>(Locks);
            services.AddScoped<IJobSourceRunProgress, JobSourceRunProgress>();
            services.AddScoped<IJobSourceRunner>(sp => new Runner(async ct =>
            { Interlocked.Increment(ref Calls); return await Run(sp.GetRequiredService<IJobSourceRunProgress>(), ct); }));
            Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
        public Task<JobSourceRunResponse> Enqueue() => Service.EnqueueAsync(Source.Id, null, default);
        public JobSourceRunWorker Worker(JobSourceRunGuard? guard = null) => new(Services.GetRequiredService<IServiceScopeFactory>(),
            guard ?? new(), Clock, Options.Create(new JobAggregationOptions()), NullLogger<JobSourceRunWorker>.Instance);
        public void Dispose() => Services.Dispose();
    }
    private sealed class Runner(Func<CancellationToken, Task<JobSourceRunResult>> run) : IJobSourceRunner
    { public Task<JobSourceRunResult> RunAsync(Guid jobSourceId, CancellationToken cancellationToken = default) => run(cancellationToken); }
    private sealed class Sources(JobSource source) : IJobSourceRepository
    {
        public Task<JobSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(id == source.Id ? source : null); }
        public Task<IReadOnlyCollection<JobSource>> GetDueSourcesAsync(DateTime nowUtc, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<JobSource>>([source]);
        public void Update(JobSource value) { }
    }
    // Test double only. Actual PostgreSQL claim/unique/fencing SQL has a separate opt-in local integration test.
    private sealed class Store : IJobSourceRunStore
    {
        private readonly object gate = new();
        public List<JobSourceRun> Rows { get; } = [];
        public bool HeartbeatLost;
        private static bool Active(JobSourceRun r) => r.Status is JobSourceRunStatus.Queued or JobSourceRunStatus.Running ||
            r.Status == JobSourceRunStatus.Interrupted && r.AttemptCount < 3;
        private static JobSourceRun Copy(JobSourceRun row) => JsonSerializer.Deserialize<JobSourceRun>(JsonSerializer.Serialize(row))!;
        public Task<JobSourceRun> EnqueueAsync(Guid sourceId, Guid? requestedBy, DateTime now, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (gate)
            {
                var row = Rows.SingleOrDefault(x => x.JobSourceId == sourceId && Active(x));
                if (row is null) { row = new() { JobSourceId = sourceId, RequestedByUserId = requestedBy, QueuedAtUtc = now, NextAttemptAtUtc = now }; Rows.Add(row); }
                return Task.FromResult(Copy(row));
            }
        }
        public Task<JobSourceRun?> GetAsync(Guid sourceId, Guid runId, CancellationToken ct)
        { lock (gate) { var row = Rows.SingleOrDefault(x => x.Id == runId && x.JobSourceId == sourceId); return Task.FromResult(row is null ? null : Copy(row)); } }
        public Task<bool> HasActiveAsync(Guid sourceId, CancellationToken ct)
        { lock (gate) return Task.FromResult(Rows.Any(x => x.JobSourceId == sourceId && Active(x))); }
        public Task<IReadOnlyList<JobSourceRun>> CandidatesAsync(DateTime now, CancellationToken ct)
        {
            lock (gate) return Task.FromResult<IReadOnlyList<JobSourceRun>>(Rows.Where(x =>
                (x.Status == JobSourceRunStatus.Queued || x.Status == JobSourceRunStatus.Interrupted && x.AttemptCount < 3) && x.NextAttemptAtUtc <= now ||
                x.Status == JobSourceRunStatus.Running && x.LeaseExpiresAtUtc <= now).Take(25).Select(Copy).ToArray());
        }
        public Task<JobSourceRun?> TryClaimAsync(Guid runId, Guid owner, DateTime now, DateTime expiry, CancellationToken ct)
        {
            lock (gate)
            {
                var row = Rows.Single(x => x.Id == runId);
                if (row.Status is not (JobSourceRunStatus.Queued or JobSourceRunStatus.Interrupted) || row.AttemptCount >= 3 || row.NextAttemptAtUtc > now)
                    return Task.FromResult<JobSourceRun?>(null);
                row.Status = JobSourceRunStatus.Running; row.LeaseOwner = owner; row.LeaseExpiresAtUtc = expiry;
                row.StartedAtUtc = now; row.HeartbeatAtUtc = now; row.AttemptCount++; row.CompletedAtUtc = null;
                return Task.FromResult<JobSourceRun?>(Copy(row));
            }
        }
        private JobSourceRun? Owned(JobSourceRun run, DateTime now) => Rows.SingleOrDefault(x => x.Id == run.Id &&
            x.Status == JobSourceRunStatus.Running && x.LeaseOwner == run.LeaseOwner && x.LeaseExpiresAtUtc > now);
        public Task<bool> HeartbeatAsync(JobSourceRun run, DateTime now, DateTime expiry, JobSourceRunProgressSnapshot p, CancellationToken ct)
        { lock (gate) { var row = Owned(run, now); if (row is null || HeartbeatLost) return Task.FromResult(false); row.LeaseExpiresAtUtc = expiry; row.HeartbeatAtUtc = now; SaveProgress(row, p); return Task.FromResult(true); } }
        public Task RecoverAsync(Guid runId, DateTime now, DateTime retryAt, CancellationToken ct)
        {
            lock (gate)
            {
                var row = Rows.Single(x => x.Id == runId);
                if (row.Status == JobSourceRunStatus.Running && row.LeaseExpiresAtUtc <= now)
                { row.Status = JobSourceRunStatus.Interrupted; row.InterruptedAtUtc = now; row.NextAttemptAtUtc = retryAt; row.LeaseOwner = null; row.LeaseExpiresAtUtc = null; row.CompletedAtUtc = row.AttemptCount >= 3 ? now : null; }
                return Task.CompletedTask;
            }
        }
        public Task<bool> FinishAsync(JobSourceRun run, JobSourceRunStatus status, DateTime now, DateTime retryAt, JobSourceRunProgressSnapshot p, CancellationToken ct)
        {
            lock (gate)
            {
                var row = Owned(run, now); if (row is null) return Task.FromResult(false);
                SaveProgress(row, p); row.Status = status; row.NextAttemptAtUtc = retryAt;
                row.CompletedAtUtc = status == JobSourceRunStatus.Interrupted && row.AttemptCount < 3 ? null : now;
                if (status == JobSourceRunStatus.Interrupted) row.InterruptedAtUtc = now;
                row.ErrorCode = status == JobSourceRunStatus.Succeeded ? null : status == JobSourceRunStatus.Interrupted ? "worker_interrupted" : "source_run_failed";
                row.LeaseOwner = null; row.LeaseExpiresAtUtc = null; return Task.FromResult(true);
            }
        }
        private static void SaveProgress(JobSourceRun row, JobSourceRunProgressSnapshot p)
        { row.Phase = p.Phase; row.Processed = p.Processed; row.TotalReceived = p.Counters.TotalReceived; row.Created = p.Counters.Created; row.Updated = p.Counters.Updated; }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; } = JobSourceFixture.Now;
        public Channel<TestTimer> Timers { get; } = Channel.CreateUnbounded<TestTimer>();
        public override DateTimeOffset GetUtcNow() => new(Now);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new TestTimer(callback, state, dueTime); Timers.Writer.TryWrite(timer); return timer; }
    }
    private sealed class TestTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        private bool disposed;
        public TimeSpan Due { get; } = due;
        public void Fire() { if (!disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
