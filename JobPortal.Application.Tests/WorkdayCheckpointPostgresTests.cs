using System.Net;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Services;
using JobPortal.API.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

// Every network response is a fixture; all database access is an explicitly disposable localhost database.
public sealed class WorkdayCheckpointPostgresTests(WorkdayCheckpointDatabase database, ITestOutputHelper output)
    : IClassFixture<WorkdayCheckpointDatabase>
{
    [WorkdayLocalPostgresFact]
    public async Task LargeInventoryProcesses5001VacanciesAcrossSixExecutionsAndRepeatsWithoutDuplicates()
    {
        var f = await Harness.CreateAsync(database, 5001);
        f.IndiaCount = 64;
        var run = await f.EnqueueAsync();
        JobSourceRunResult? result = null;
        var executions = 0;
        do
        {
            result = await f.ExecuteAsync(run.Id, 1000);
            executions++;
            output.WriteLine($"Execution {executions}: StopReason={result.StopReason}; Cursor={result.CheckpointCursor}; Continuation={result.ContinuationPending}; Failed={result.Failed}; Error={result.Error}");
            if (result.ContinuationPending)
            {
                Assert.False(result.ReconciliationSafe);
                Assert.Equal(0, result.Closed);
            }
        } while (result.ContinuationPending && executions < 20);
        Assert.Equal(6, executions);
        Assert.True(result.Succeeded);
        Assert.False(result.ReconciliationSafe);
        Assert.Equal(5001, result.CheckpointCursor);
        Assert.Equal(5001, result.ObservedIdentities);
        Assert.Equal(64, result.Created);
        Assert.Equal(0, result.Existing);
        Assert.Equal(0, result.Failed);
        Assert.Equal(64, result.Published);
        Assert.Equal(4937, result.SelectionSkipped);
        Assert.Equal(5001, f.DetailRequests);
        await using var db = database.Open();
        Assert.Equal(64, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId));
        Assert.Equal(64, await db.Set<JobSourceRunItem>().CountAsync(x => x.RunId == run.Id));
        var status = JobSourceRunResponse.From((await new JobSourceRunStore(db).GetAsync(f.Source.Id, run.Id, default))!);
        Assert.Equal("Succeeded", status.Status);
        Assert.Equal(64, status.Created);
        Assert.Equal(0, status.Diagnostics!.RemainingWork);
        var repeatedRun = await f.EnqueueAsync();
        do { result = await f.ExecuteAsync(repeatedRun.Id, 1000); } while (result.ContinuationPending);
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(64, result.Existing);
        Assert.Equal(0, result.Published);
        Assert.Equal(4937, result.SelectionSkipped);
        Assert.Equal(64, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId));
        output.WriteLine($"5001 fixture vacancies with real PostgreSQL: executions={executions}; first Created=64 Published=64 GeographySkipped=4937; repeat Created=0 Existing=64 Published=0.");
    }

    [WorkdayLocalPostgresFact]
    public async Task Ptc176Observed44IndiaExistingReconcilesAndOptionalCategoryPublishesIntoDiscover()
    {
        var f = await Harness.CreateAsync(database, 176);
        f.IndiaCount = 44;
        var run = await f.EnqueueAsync();
        var result = await f.ExecuteAsync(run.Id, 40);
        Assert.True(result.ContinuationPending);
        Assert.Equal(40, result.Created);
        while (result.ContinuationPending) result = await f.ExecuteAsync(run.Id, 40);
        Assert.False(result.ReconciliationSafe);
        Assert.True(result.Succeeded);
        Assert.Equal(44, result.Created);
        Assert.Equal(132, result.SelectionSkipped);
        Assert.Equal(176, result.ObservedIdentities);
        await using var db = database.Open();
        var jobs = await db.Jobs.Where(x => x.CompanyId == f.Source.CompanyId).ToArrayAsync();
        Assert.Equal(44, jobs.Length);
        Assert.All(jobs, x => Assert.Equal(JobStatus.Published, x.Status));
        Assert.All(jobs, x => Assert.Null(x.CategoryId));
        var publicRepository = new PublicJobRepository(db, TimeProvider.System);
        var discover = await publicRepository.SearchAsync(new(CompanyId: f.Source.CompanyId));
        Assert.NotEmpty(discover.Items);
        Assert.All(discover.Items, x => Assert.Contains(jobs, j => j.Id == x.Id));
        var publicService = new JobPortal.Application.Features.PublicJobs.PublicJobService(publicRepository,
            new JobPortal.Application.Features.PublicJobs.PublicJobQueryValidator());
        var publicApi = await new JobPortal.API.Controllers.PublicJobsController(publicService)
            .Search(new(CompanyId: f.Source.CompanyId, PageSize: 100), default);
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(publicApi.Result);
        var response = Assert.IsType<JobPortal.Shared.Models.ApiResponse<JobPortal.Application.Features.PublicJobs.PublicJobSearchResponse>>(ok.Value);
        Assert.Equal(44, response.Data.TotalCount);
        Assert.Equal(44, response.Data.Items.Count);
        Assert.All(response.Data.Items, x => Assert.Contains(jobs, j => j.Id == x.Id));
        var repeated = await f.EnqueueAsync();
        do { result = await f.ExecuteAsync(repeated.Id, 40); } while (result.ContinuationPending);
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(44, result.Existing);
        Assert.Equal(132, result.SelectionSkipped);
        Assert.Equal(0, result.Published);
        Assert.Empty(result.ReconciliationBlockReasons);
    }

    [WorkdayLocalPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrashBeforeOrAfterDataCommitNeverAdvancesCursorAndRecoveryCountsActualCreations(bool afterCommit)
    {
        var f = await Harness.CreateAsync(database, 3);
        var run = await f.EnqueueAsync();
        using var shutdown = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.ExecuteAsync(run.Id, 3, shutdown,
            crash: afterCommit ? CrashPoint.AfterSave : CrashPoint.BeforeSave));
        await using (var db = database.Open())
        {
            var checkpoint = JsonSerializer.Deserialize<WorkdayRunCheckpoint>((await db.JobSourceRuns.SingleAsync(x => x.Id == run.Id)).CheckpointJson!)!;
            Assert.Equal(0, checkpoint.Cursor);
            Assert.Equal(afterCommit ? 3 : 0, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId));
            Assert.Equal(afterCommit ? 3 : 0, await db.Set<JobSourceRunItem>().CountAsync(x => x.RunId == run.Id));
            Assert.Equal(0, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId && x.Status == JobStatus.Closed));
        }
        var resumed = await f.ExecuteAsync(run.Id, 3);
        Assert.True(resumed.Succeeded);
        Assert.Equal(3, resumed.Created);
        Assert.Equal(3, resumed.Published);
        Assert.Equal(0, resumed.Existing);
        Assert.Equal(0, resumed.Failed);
        await using var check = database.Open();
        Assert.Equal(3, await check.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId));
    }

    [WorkdayLocalPostgresFact]
    public async Task ShutdownAfterCommittedCheckpointResumesOnlyRemainingDetails()
    {
        var f = await Harness.CreateAsync(database, 7);
        var run = await f.EnqueueAsync();
        var first = await f.ExecuteAsync(run.Id, 2);
        Assert.Equal(2, first.CheckpointCursor);
        using var shutdown = new CancellationTokenSource();
        f.BeforeDetail = (_, token) => { shutdown.Cancel(); return Task.Delay(Timeout.InfiniteTimeSpan, token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.ExecuteAsync(run.Id, 2, shutdown));
        f.BeforeDetail = null;
        JobSourceRunResult result;
        do { result = await f.ExecuteAsync(run.Id, 2); } while (result.ContinuationPending);
        Assert.True(result.Succeeded);
        Assert.Equal(7, result.Created);
        Assert.Equal(7, result.Published);
        Assert.Equal(0, result.Closed);
        await using var db = database.Open();
        Assert.Equal(7, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId));
        Assert.Equal(7, await db.Set<JobSourceRunItem>().CountAsync(x => x.RunId == run.Id));
    }

    [WorkdayLocalPostgresFact]
    public async Task ProviderBudgetPauseIsBoundedAndStableSnapshotChangesRestartOnResume()
    {
        var f = await Harness.CreateAsync(database, 3);
        var run = await f.EnqueueAsync();
        var first = await f.ExecuteAsync(run.Id, 1);
        Assert.Equal(1, first.Created);
        f.BeforeDetail = (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token);
        var timedOut = await f.ExecuteAsync(run.Id, 1, providerBudget: 1);
        Assert.True(timedOut.ContinuationPending);
        Assert.False(timedOut.ReconciliationSafe);
        Assert.Equal("ProviderRunBudgetExceeded", timedOut.StopReason);
        Assert.Equal(2, timedOut.DetailAttempted);
        Assert.Equal(1, timedOut.DetailSucceeded);
        Assert.Equal(1, timedOut.DetailInFlight);
        Assert.Equal(1, timedOut.Created);
        Assert.Equal(1, timedOut.CheckpointCursor);
        f.BeforeDetail = null;
        f.Total = 4;
        var changed = await f.ExecuteAsync(run.Id, 1);
        Assert.False(changed.Succeeded);
        Assert.True(changed.ContinuationPending);
        Assert.False(changed.ReconciliationSafe);
        Assert.Equal("ExecutionSliceComplete", changed.StopReason);
        Assert.Equal(1, changed.RebaseCount);
        Assert.Equal(0, changed.Closed);
        await using var db = database.Open();
        Assert.Equal(JobSourceRunStatus.Paused, (await db.JobSourceRuns.SingleAsync(x => x.Id == run.Id)).Status);
        Assert.Equal(1, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId));
    }

    [WorkdayLocalPostgresFact]
    public async Task FailedDetailsCannotReconcileOrCloseJobsAndRetryAttemptsAreBounded()
    {
        var f = await Harness.CreateAsync(database, 3);
        var first = await f.EnqueueAsync();
        Assert.True((await f.ExecuteAsync(first.Id, 3)).Succeeded);
        var second = await f.EnqueueAsync();
        f.FailDetails = true;
        var failed = await f.ExecuteAsync(second.Id, 3, requestAttempts: 2);
        Assert.False(failed.Succeeded);
        Assert.False(failed.ReconciliationSafe);
        Assert.Equal(3, failed.ProviderSkipped);
        Assert.Equal(3, failed.DetailFailed);
        Assert.Equal(0, failed.Closed);
        Assert.Equal(9, f.DetailRequests); // Three initial successful GETs + exactly two attempts for each failing detail.
        await using var db = database.Open();
        Assert.Equal(3, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId && x.Status == JobStatus.Published));
    }

    [WorkdayLocalPostgresFact]
    public async Task CompleteInventoryNeverClosesMissingJobButPartialSlicesCannot()
    {
        var f = await Harness.CreateAsync(database, 4);
        var first = await f.EnqueueAsync();
        Assert.True((await f.ExecuteAsync(first.Id, 4)).Succeeded);
        f.Total = 3;
        var second = await f.EnqueueAsync();
        var partial = await f.ExecuteAsync(second.Id, 2);
        Assert.False(partial.ReconciliationSafe);
        Assert.Equal(0, partial.Closed);
        await using var db = database.Open();
        Assert.Equal(0, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId && x.Status == JobStatus.Closed));
        var complete = await f.ExecuteAsync(second.Id, 2);
        Assert.True(complete.Succeeded);
        Assert.Equal(0, complete.Closed);
        Assert.Equal(0, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId && x.Status == JobStatus.Closed));
    }

    [WorkdayLocalPostgresFact]
    public async Task PausedRunsCoalesceRequestsClaimsAreExclusiveAndThreeNoProgressAttemptsStop()
    {
        var f = await Harness.CreateAsync(database, 4);
        var run = await f.EnqueueAsync();
        Assert.True((await f.ExecuteAsync(run.Id, 1)).ContinuationPending);
        Assert.Equal(run.Id, (await f.EnqueueAsync()).Id);
        f.BeforeDetail = (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var result = await f.ExecuteAsync(run.Id, 1, providerBudget: 1);
            Assert.True(result.ContinuationPending);
        }
        await using var db = database.Open();
        var stored = await db.JobSourceRuns.SingleAsync(x => x.Id == run.Id);
        Assert.Equal(JobSourceRunStatus.Failed, stored.Status);
        Assert.Equal(3, stored.AttemptCount);
        Assert.NotNull(stored.CompletedAtUtc);
        Assert.False(JobSourceRunResponse.From(stored).Diagnostics!.ContinuationPending);
        Assert.Equal("NoProgressAttemptsExhausted", JobSourceRunResponse.From(stored).Diagnostics!.StopReason);
        Assert.False(await new JobSourceRunStore(db).HasActiveAsync(f.Source.Id, default));
        var next = await f.EnqueueAsync();
        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var claimDb = database.Open();
            var now = DateTime.UtcNow;
            return await new JobSourceRunStore(claimDb).TryClaimAsync(next.Id, Guid.NewGuid(), now, now.AddMinutes(2), default);
        }));
        Assert.Single(claims, x => x is not null);
    }

    [WorkdayLocalPostgresFact]
    public async Task CrashAfterNonDestructiveCompletionCommitRecoversExactCompletionWithoutAnotherFetchOrClosure()
    {
        var f = await Harness.CreateAsync(database, 5);
        var first = await f.EnqueueAsync();
        Assert.True((await f.ExecuteAsync(first.Id, 5)).Succeeded);
        f.Total = 3;
        var run = await f.EnqueueAsync();
        using var shutdown = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.ExecuteAsync(run.Id, 3, shutdown, CrashPoint.AfterFinalSave));
        var beforeResume = f.DetailRequests;
        var result = await f.ExecuteAsync(run.Id, 3);
        Assert.True(result.Succeeded);
        Assert.False(result.ReconciliationSafe);
        Assert.Equal(0, result.Closed);
        Assert.Equal(0, result.Created);
        Assert.Equal(3, result.Existing);
        Assert.Equal(beforeResume, f.DetailRequests);
        await using var db = database.Open();
        Assert.Equal(0, await db.Jobs.CountAsync(x => x.CompanyId == f.Source.CompanyId && x.Status == JobStatus.Closed));
    }

    [WorkdayLocalPostgresFact]
    public async Task StableReplacementRestartsDriftedMembershipBeforeGrantingReconciliationAuthority()
    {
        var f = await Harness.CreateAsync(database, 43);
        f.IndiaCount = 43;
        var initial = await f.EnqueueAsync();
        Assert.True((await f.ExecuteAsync(initial.Id, 43)).Succeeded);
        DateTime? previousSuccess;
        await using (var before = database.Open())
            previousSuccess = await before.JobSources.Where(x => x.Id == f.Source.Id).Select(x => x.LastSuccessfulRunAtUtc).SingleAsync();
        f.Total = 42;
        f.AdvertisedTotal = 43;
        f.FinalAnchorTotal = 42;
        f.OmitContinuationTotal = true;
        f.AnchorReads = 0;
        var run = await f.EnqueueAsync();
        JobSourceRunResult result;
        var executions = 0;
        do
        {
            result = await f.ExecuteAsync(run.Id, 10);
            executions++;
            if (result.ContinuationPending)
            {
                Assert.False(result.Succeeded);
                Assert.False(result.ReconciliationSafe);
                Assert.Equal(0, result.Closed);
            }
            // Stable replacement is a NEW traversal, including the already committed prefix.
            if (executions == 1) f.AdvertisedTotal = 42;
        } while (result.ContinuationPending && executions < 10);
        Assert.Equal(6, executions);
        Assert.False(result.ContinuationPending);
        Assert.Equal("Complete", result.StopReason);
        Assert.False(result.ReconciliationSafe);
        Assert.Equal(0, result.Closed);
        Assert.Equal(42, result.CheckpointCursor);
        Assert.Equal(52, result.Existing);
        Assert.Equal(0, result.Created);
        await using (var db = database.Open())
        {
            var stored = await db.JobSourceRuns.SingleAsync(x => x.Id == run.Id);
            Assert.Equal(JobSourceRunStatus.Succeeded, stored.Status);
            var state = JsonSerializer.Deserialize<WorkdayRunCheckpoint>(stored.CheckpointJson!)!;
            Assert.Equal(4, state.Version);
            Assert.False(state.ListingDiagnostics!.ReconciliationSafe);
            Assert.Equal(42, state.ListingDiagnostics.InitialReportedTotal);
            Assert.Equal(42, state.ListingDiagnostics.FinalReportedTotal);
            Assert.NotNull(state.CompletedResult);
            Assert.Equal(42, await db.Set<JobSourceRunItem>().CountAsync(x => x.RunId == run.Id));
            Assert.Equal(42, await db.Jobs.CountAsync(x => x.JobSourceId == f.Source.Id && x.Status == JobStatus.Published));
            Assert.NotEqual(previousSuccess, await db.JobSources.Where(x => x.Id == f.Source.Id).Select(x => x.LastSuccessfulRunAtUtc).SingleAsync());
        }
        // A later independent stable run is also idempotent.
        f.AdvertisedTotal = null;
        f.FinalAnchorTotal = null;
        f.OmitContinuationTotal = false;
        f.AnchorReads = 0;
        var stable = await f.EnqueueAsync();
        var complete = await f.ExecuteAsync(stable.Id, 42);
        Assert.True(complete.Succeeded);
        Assert.False(complete.ReconciliationSafe);
        Assert.Equal(0, complete.Created);
        Assert.Equal(0, complete.Closed);
    }

    [WorkdayLocalPostgresTheory]
    [InlineData(8, false)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public async Task ReplacementTraversalKeepsReceiptsCountersAndDefersClosure(int replacement, bool reverse)
    {
        var f = await Harness.CreateAsync(database, 7);
        var baseline = await f.EnqueueAsync();
        Assert.True((await f.ExecuteAsync(baseline.Id, 7)).Succeeded);
        var run = await f.EnqueueAsync();
        var first = await f.ExecuteAsync(run.Id, 2);
        Assert.Equal(2, first.Existing);
        f.Total = replacement; f.ReverseOrder = reverse;
        var resumed = await f.ExecuteAsync(run.Id, 2);
        Assert.True(resumed.ContinuationPending); Assert.False(resumed.ReconciliationSafe);
        Assert.Equal(0, resumed.Closed); Assert.Equal(1, resumed.RebaseCount); Assert.Equal(2, resumed.CheckpointCursor);
        Assert.Equal(4, resumed.Existing + resumed.Created);
        JobSourceRunResult result = resumed;
        while (result.ContinuationPending) result = await f.ExecuteAsync(run.Id, 2);
        Assert.True(result.Succeeded); Assert.False(result.ReconciliationSafe);
        Assert.Equal(replacement, result.ObservedIdentities); Assert.Equal(replacement + 2, result.Created + result.Existing);
        Assert.Equal(Math.Max(0, replacement - 7), result.Created);
        Assert.Equal(0, result.Closed);
        await using var db = database.Open();
        Assert.Equal(Math.Max(7, replacement), await db.Jobs.CountAsync(x => x.JobSourceId == f.Source.Id));
        Assert.Equal(replacement, await db.Set<JobSourceRunItem>().CountAsync(x => x.RunId == run.Id));
        var restored = new JobSourceRunCheckpoint(db, TimeProvider.System);
        await restored.BindAsync((await db.JobSourceRuns.SingleAsync(x => x.Id == run.Id)), default);
        Assert.Equal(result.Created, restored.CommittedCounters.Created);
        Assert.Equal(result.Existing, restored.CommittedCounters.Existing);
        Assert.Equal(result.Published, restored.CommittedCounters.Published);
    }

    [WorkdayLocalPostgresFact]
    public async Task RebaseWithLostLeaseCannotArchiveOrRetireReceipts()
    {
        var f = await Harness.CreateAsync(database, 3);
        var run = await f.EnqueueAsync(); await f.ExecuteAsync(run.Id, 2);
        await using var db = database.Open();
        var stored = await db.JobSourceRuns.SingleAsync(x => x.Id == run.Id);
        var before = stored.CheckpointJson;
        var journal = new JobSourceRunCheckpoint(db, TimeProvider.System);
        await journal.BindAsync(stored, default); journal.Enable(f.Source);
        var evidence = new WorkdayListingDiagnostics { IngestionSafe = true, ReconciliationSafe = true,
            SnapshotStable = true, UniquePaths = 4, RawSlots = 4, InitialReportedTotal = 4, ReasonCode = "Complete" };
        var failure = await Assert.ThrowsAsync<JobSourceSnapshotUnsafeException>(() => journal.PrepareAsync(
            journal.State!.SourceIdentity, "replacement", 4, 1, default, evidence));
        Assert.Equal("CheckpointLeaseLost", failure.ReasonCode); // Paused run cannot own the mutation.
        await using var check = database.Open();
        Assert.Equal(before, (await check.JobSourceRuns.SingleAsync(x => x.Id == run.Id)).CheckpointJson);
        Assert.Equal(2, await check.Set<JobSourceRunItem>().CountAsync(x => x.RunId == run.Id));
        Assert.Equal(0, journal.State!.RebaseCount); Assert.Equal(2, journal.CommittedCounters.Created);
    }

    private enum CrashPoint { None, BeforeSave, AfterSave, AfterFinalSave }

    [WorkdayLocalPostgresFact]
    public async Task ResumeRetainsIndiaRemoteUnspecifiedForeignAndReviewGeographyFacts()
    {
        var f = await Harness.CreateAsync(database, 6);
        f.UseGeographyMatrix = true;
        var run = await f.EnqueueAsync();
        JobSourceRunResult result;
        do { result = await f.ExecuteAsync(run.Id, 2); } while (result.ContinuationPending);
        Assert.True(result.Succeeded);
        Assert.False(result.ReconciliationSafe);
        Assert.Equal(6, result.ObservedIdentities);
        Assert.Equal(3, result.Created);
        Assert.Equal(2, result.Published);
        Assert.Equal(1, result.NeedsReview);
        Assert.Equal(2, result.IndiaEligible);
        Assert.Equal(1, result.MissingLocationAccepted);
        Assert.Equal(1, result.NonIndiaRejected);
        Assert.Equal(2, result.AmbiguousLocationHeld);
        Assert.Equal(3, result.SelectionSkipped);
        Assert.Equal(0, result.Failed);
    }

    [WorkdayLocalPostgresFact]
    public async Task SameTotalListingChangeAtCompletionCannotCertifyReconciliation()
    {
        var f = await Harness.CreateAsync(database, 3);
        f.ChangeListingAfterDetails = true;
        var run = await f.EnqueueAsync();
        var result = await f.ExecuteAsync(run.Id, 3);
        Assert.False(result.Succeeded);
        Assert.False(result.ReconciliationSafe);
        Assert.Equal("ListingSnapshotChangedAtCompletion", result.StopReason);
        Assert.Equal(3, result.Created);
        Assert.Equal(0, result.Closed);
        await using var db = database.Open();
        Assert.Equal(JobSourceRunStatus.Unsafe, (await db.JobSourceRuns.SingleAsync(x => x.Id == run.Id)).Status);
    }

    [WorkdayLocalPostgresFact]
    public async Task ActualWorkersEnforceGlobalSlotsPreventSourceOverlapAndYieldToQueuedCompanies()
    {
        var fixtures = new[] { await Harness.CreateAsync(database, 10), await Harness.CreateAsync(database, 10), await Harness.CreateAsync(database, 10) };
        var runs = new List<JobSourceRun>();
        foreach (var fixture in fixtures) runs.Add(await fixture.EnqueueAsync());
        var calls = new System.Collections.Concurrent.ConcurrentDictionary<Guid, int>();
        var started = new System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskCompletionSource>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var fixture in fixtures) started[fixture.Source.Id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = Options.Create(new JobAggregationOptions { Scheduler = new() { MaxConcurrentSources = 2 },
            Workday = new() { ContinuationDelaySeconds = 5, MaximumExecutions = 1 } });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = database.Connection }).Build();
        var clock = new WorkerClock();
        var services = new ServiceCollection();
        services.AddScoped(_ => database.Open());
        services.AddScoped<IJobSourceRepository, JobSourceRepository>();
        services.AddScoped<IJobSourceRunStore, JobSourceRunStore>();
        services.AddScoped<IJobSourceRunCheckpoint>(sp => new JobSourceRunCheckpoint(sp.GetRequiredService<JobPortalDbContext>(), clock));
        services.AddScoped<IJobSourceRunProgress, JobSourceRunProgress>();
        services.AddSingleton<IJobSourceExecutionLock>(new PostgresJobSourceExecutionLock(configuration));
        services.AddScoped<IJobSourceRunner>(sp => new WorkerRunner(async (id, token) =>
        {
            calls.AddOrUpdate(id, 1, (_, previous) => previous + 1);
            var checkpoint = sp.GetRequiredService<IJobSourceRunCheckpoint>();
            await checkpoint.PrepareAsync(id.ToString(), "fixture-inventory", 10, 1, token);
            started[id].TrySetResult();
            await release.Task.WaitAsync(token);
            await checkpoint.CommitAsync(1, 0, [new() { ExternalId = "fixture", SourceOrdinal = 0, Location = "India" }], token);
            return new() { JobSourceId = id, RunId = checkpoint.RunId, ContinuationPending = true,
                CheckpointCursor = 1, ListingTotal = 10, RemainingWork = 9, StopReason = "ExecutionSliceComplete" };
        }));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        JobSourceRunWorker Worker() => new(provider.GetRequiredService<IServiceScopeFactory>(), new(), clock, settings, NullLogger<JobSourceRunWorker>.Instance);
        var first = Worker().RunOnceAsync(default);
        await started[fixtures[0].Source.Id].Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = Worker().RunOnceAsync(default);
        await started[fixtures[1].Source.Id].Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Worker().RunOnceAsync(default); // Both global slots occupied; no third source or overlapping same-source execution.
        Assert.Equal(2, calls.Count);
        Assert.All(calls.Values, x => Assert.Equal(1, x));
        release.TrySetResult();
        await Task.WhenAll(first, second);
        await Worker().RunOnceAsync(default); // Paused companies have a delay; queued third company executes next.
        Assert.Equal(3, calls.Count);
        await using var db = database.Open();
        var store = new JobSourceRunStore(db);
        foreach (var run in runs)
        {
            var stored = (await store.GetAsync(run.JobSourceId, run.Id, default))!;
            Assert.Equal(JobSourceRunStatus.Paused, stored.Status);
            Assert.True(JobSourceRunResponse.From(stored).RetryPending);
            Assert.Equal(0, stored.AttemptCount);
        }
        clock.Advance(TimeSpan.FromSeconds(10));
        await Worker().RunOnceAsync(default); // MaximumExecutions=1 forbids another provider execution.
        Assert.Equal(3, calls.Values.Sum());
        var exhausted = Assert.Single(await db.JobSourceRuns.Where(x => runs.Select(r => r.Id).Contains(x.Id) && x.Status == JobSourceRunStatus.Failed).ToArrayAsync());
        Assert.False(JobSourceRunResponse.From(exhausted).Diagnostics!.ContinuationPending);
        var unavailable = (await store.CandidatesAsync(clock.GetUtcNow().UtcDateTime, default))[0];
        var inactiveSource = await db.JobSources.SingleAsync(x => x.Id == unavailable.JobSourceId);
        inactiveSource.IsActive = false;
        await db.SaveChangesAsync();
        await Worker().RunOnceAsync(default);
        var cancelled = (await store.GetAsync(unavailable.JobSourceId, unavailable.Id, default))!;
        Assert.Equal(JobSourceRunStatus.Cancelled, cancelled.Status);
        var cancelledDiagnostics = JobSourceRunResponse.From(cancelled).Diagnostics!;
        Assert.Equal(1, cancelledDiagnostics.CheckpointCursor);
        Assert.False(cancelledDiagnostics.ContinuationPending);
        Assert.Equal("SourceUnavailable", cancelledDiagnostics.StopReason);
        Assert.Equal(3, calls.Values.Sum());
    }

    private sealed class WorkerRunner(Func<Guid, CancellationToken, Task<JobSourceRunResult>> run) : IJobSourceRunner
    { public Task<JobSourceRunResult> RunAsync(Guid jobSourceId, CancellationToken cancellationToken = default) => run(jobSourceId, cancellationToken); }
    private sealed class WorkerClock : TimeProvider
    { private DateTimeOffset current = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => current; public void Advance(TimeSpan duration) => current += duration; }

    private sealed class Harness(WorkdayCheckpointDatabase database, JobSource source, int total)
    {
        public JobSource Source { get; } = source;
        public int Total { get; set; } = total;
        public int IndiaCount { get; set; } = int.MaxValue;
        public int DetailRequests;
        public bool FailDetails;
        public bool ChangeListingAfterDetails;
        public bool UseGeographyMatrix;
        public int? AdvertisedTotal;
        public int? FinalAnchorTotal;
        public int AnchorReads;
        public bool OmitContinuationTotal;
        public bool ReverseOrder;
        public Func<int, CancellationToken, Task>? BeforeDetail;

        public static async Task<Harness> CreateAsync(WorkdayCheckpointDatabase database, int total)
        {
            await using var db = database.Open();
            var email = $"{Guid.NewGuid():N}@example.test";
            var owner = new User { Email = email, NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "local-test",
                FirstName = "Test", LastName = "Owner", RoleId = await db.Roles.Select(x => x.Id).FirstAsync() };
            var name = "Workday test " + Guid.NewGuid().ToString("N");
            var company = new Company { Name = name, NormalizedName = name.ToUpperInvariant(), Slug = name.Replace(' ', '-'), OwnerUserId = owner.Id };
            var source = new JobSource { CompanyId = company.Id, Company = company, AtsType = AtsType.Workday,
                AtsIdentifier = "ptc/PTC", CareerPageUrl = "https://ptc.wd1.myworkdayjobs.com/PTC", IsActive = true, AutoPublishEnabled = true };
            db.Add(owner); db.Add(source); await db.SaveChangesAsync();
            return new(database, source, total);
        }

        public async Task<JobSourceRun> EnqueueAsync()
        {
            await using var db = database.Open();
            return await new JobSourceRunStore(db).EnqueueAsync(Source.Id, null, DateTime.UtcNow, default);
        }

        public async Task<JobSourceRunResult> ExecuteAsync(Guid runId, int slice, CancellationTokenSource? shutdown = null,
            CrashPoint crash = CrashPoint.None, int providerBudget = 300, int requestAttempts = 1)
        {
            await using var db = database.Open();
            var store = new JobSourceRunStore(db);
            var stored = (await store.GetAsync(Source.Id, runId, default))!;
            var now = stored.NextAttemptAtUtc > DateTime.UtcNow ? stored.NextAttemptAtUtc : DateTime.UtcNow;
            var claimed = await store.TryClaimAsync(runId, Guid.NewGuid(), now, now.AddMinutes(10), default);
            Assert.NotNull(claimed);
            var checkpoint = new JobSourceRunCheckpoint(db, TimeProvider.System);
            await checkpoint.BindAsync(claimed, default);
            checkpoint.Enable(Source);
            using var http = new HttpClient(new Handler(SendAsync));
            using var provider = new WorkdayJobSourceProvider(new Factory(http), options: Options.Create(new JobAggregationOptions
            { Workday = new() { JobsPerExecution = slice, BatchSize = 100, MaximumAttempts = requestAttempts,
                RunBudgetSeconds = providerBudget, RequestTimeoutSeconds = 3 } }), timeProvider: new FastClock(), checkpoint: checkpoint);
            IUnitOfWork unit = crash == CrashPoint.None ? new UnitOfWork(db) : new CrashUnit(db, shutdown!, crash);
            var jobs = new JobRepository(db); var companies = new CompanyManagementRepository(db);
            var categories = new CategoryManagementRepository(db); var sources = new JobSourceRepository(db);
            var fingerprints = new JobFingerprintService(); var canonical = new UrlCanonicalizer();
            var deduplication = new JobDeduplicationService(jobs, fingerprints, canonical);
            var locks = new PostgresExternalJobCreationLock(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database.Connection }).Build());
            var service = new JobService(jobs, unit, new AuditWriterTestDouble(), new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
                new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), TimeProvider.System, companies, categories,
                sources, locks, deduplication);
            var publisher = new JobAutoPublishService(jobs, new JobQualityGate(), service, Options.Create(new JobAggregationOptions()),
                TimeProvider.System, sources, locks, checkpoint);
            var resolver = new JobSourceCategoryResolver(new Monitor(), categories, new ExternalJobCategoryClassifier());
            var ingestion = new JobIngestionService(jobs, companies, categories, deduplication, fingerprints, unit,
                TimeProvider.System, locks, canonical, checkpoint);
            var progress = new JobSourceRunProgress();
            var runner = new JobSourceRunner(sources, [provider], ingestion, unit, TimeProvider.System, resolver,
                new ExternalJobNormalizer(), autoPublishService: publisher, enricher: new ExternalJobMetadataEnricher(),
                jobRepository: jobs, runProgress: progress, checkpoint: checkpoint);
            try
            {
                var result = await runner.RunAsync(Source.Id, shutdown?.Token ?? default);
                var status = result.ContinuationPending ? JobSourceRunStatus.Paused : result.Succeeded ? JobSourceRunStatus.Succeeded : JobSourceRunStatus.Unsafe;
                Assert.True(await store.FinishAsync(claimed, status, now, now, new("Finalizing", progress.Snapshot.Processed, result), default));
                return result;
            }
            catch (OperationCanceledException)
            {
                await checkpoint.RefreshAsync(default);
                var counters = checkpoint.CommittedCounters with { ContinuationPending = true,
                    CheckpointCursor = checkpoint.State?.Cursor ?? 0, StopReason = "HostShutdown" };
                Assert.True(await store.FinishAsync(claimed, JobSourceRunStatus.Paused, now, now, new("Paused", counters.TotalReceived, counters), default));
                throw;
            }
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var offset = body.RootElement.GetProperty("offset").GetInt32();
                Assert.True(offset < Total);
                if (offset == 0) AnchorReads++;
                var reported = offset == 0 ? AnchorReads > 1 ? FinalAnchorTotal ?? AdvertisedTotal ?? Total : AdvertisedTotal ?? Total :
                    OmitContinuationTotal ? 0 : AdvertisedTotal ?? Total;
                return Json(JsonSerializer.Serialize(new { total = reported, jobPostings = Enumerable.Range(offset, Math.Min(20, Total - offset))
                    .Select(i => ReverseOrder ? Total - 1 - i : i)
                    .Select(i => new { title = $"{(ChangeListingAfterDetails && DetailRequests >= Total ? "Changed" : "Unmapped")} vacancy {Source.Id:N} {i}", externalPath = $"/job/Pune/Vacancy_{Source.Id:N}_{i}",
                        locationsText = UseGeographyMatrix && i == 3 ? null : "Multiple locations" }) }));
            }
            var id = int.Parse(request.RequestUri!.AbsolutePath.Split('_').Last(), System.Globalization.CultureInfo.InvariantCulture);
            Interlocked.Increment(ref DetailRequests);
            if (BeforeDetail is not null) await BeforeDetail(id, token);
            if (FailDetails) return new(HttpStatusCode.ServiceUnavailable);
            return Json(JsonSerializer.Serialize(new { jobPostingInfo = new { title = $"Unmapped vacancy {Source.Id:N} {id}",
                jobReqId = $"{Source.Id:N}-{id}", location = UseGeographyMatrix ? id switch
                { 1 => "Remote - India", 2 => "Remote", 3 => null, 4 => "Multiple locations", _ => "Pune, India" } : id < IndiaCount ? "Pune, India" : "London, United Kingdom",
                country = UseGeographyMatrix && id is 2 or 5 ? new { alpha2Code = "US" } : null,
                timeType = "Full time", remoteType = UseGeographyMatrix ? id switch { 1 or 2 => "Remote", 3 => null, _ => "On-site" } : "On-site",
                startDate = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                jobDescription = $"<p>This full time role supports vacancy {id}. 2 years of experience required. " +
                    "Coordinate reliable delivery and documented operations, support stakeholders and maintain accurate records.</p>" } }));
        }
    }

    private sealed class CrashUnit(JobPortalDbContext db, CancellationTokenSource shutdown, CrashPoint point) : IUnitOfWork
    {
        private bool fired;
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var crash = !fired && db.ChangeTracker.Entries<Job>().Any(x => x.State == EntityState.Added);
            if (crash && point == CrashPoint.BeforeSave) { fired = true; shutdown.Cancel(); throw new OperationCanceledException(cancellationToken); }
            var result = await db.SaveChangesAsync(cancellationToken);
            if (crash && point == CrashPoint.AfterSave) { fired = true; shutdown.Cancel(); throw new OperationCanceledException(cancellationToken); }
            if (!fired && point == CrashPoint.AfterFinalSave && db.ChangeTracker.Entries<JobSourceRun>()
                .Any(x => x.Entity.CheckpointJson is { } json && JsonSerializer.Deserialize<WorkdayRunCheckpoint>(json)?.CompletedResult is not null))
            { fired = true; shutdown.Cancel(); throw new OperationCanceledException(cancellationToken); }
            return result;
        }
        public void ResetAfterFailure() => db.ChangeTracker.Clear();
    }

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, System.Text.Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class FastClock : TimeProvider
    { private long seconds; public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddSeconds(Interlocked.Increment(ref seconds)); }
    private sealed class Monitor : IOptionsMonitor<JobAggregationOptions>
    { public JobAggregationOptions CurrentValue { get; } = new(); public JobAggregationOptions Get(string? name) => CurrentValue;
      public IDisposable? OnChange(Action<JobAggregationOptions, string?> listener) => null; }
    private sealed class WorkdayLocalPostgresFactAttribute : FactAttribute
    { public WorkdayLocalPostgresFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
        Skip = "Requires explicitly disposable localhost careerharbor_workday_test PostgreSQL; never uses API/Neon configuration."; } }
    private sealed class WorkdayLocalPostgresTheoryAttribute : TheoryAttribute
    { public WorkdayLocalPostgresTheoryAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
        Skip = "Requires explicitly disposable localhost careerharbor_workday_test PostgreSQL; never uses API/Neon configuration."; } }
}

public sealed class WorkdayCheckpointDatabase : IAsyncLifetime
{
    private static bool Available => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES"));
    public string Connection { get; private set; } = string.Empty;
    private string schema = string.Empty;
    private string adminConnection = string.Empty;
    public JobPortalDbContext Open() => new(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(Connection,
        o => o.MigrationsAssembly(typeof(PostgresMigrationMarker).Assembly.FullName)).Options);
    public async Task InitializeAsync()
    {
        if (!Available) return;
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES"));
        Assert.Equal("127.0.0.1", settings.Host); Assert.Equal("careerharbor_workday_test", settings.Database);
        settings.IncludeErrorDetail = false; settings.Timeout = 5; settings.CommandTimeout = 60;
        adminConnection = settings.ConnectionString;
        schema = "checkpoint_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(adminConnection); await connection.OpenAsync();
        // Extensions belong to the database, not a disposable fixture schema. Serialize their setup
        // so parallel classes cannot race CREATE EXTENSION or drop each other's operator classes.
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(74107410)", connection, transaction))
                await guard.ExecuteNonQueryAsync();
            await using (var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS btree_gist WITH SCHEMA public", connection, transaction))
                await extension.ExecuteNonQueryAsync();
            await transaction.CommitAsync(); // Release the lock only after the extension is visible to other fixtures.
        }
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await create.ExecuteNonQueryAsync();
        settings.SearchPath = schema + ",public";
        Connection = settings.ConnectionString;
        await using var db = Open(); await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
    public async Task DisposeAsync()
    {
        if (schema.Length == 0) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnection); await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection); await drop.ExecuteNonQueryAsync();
    }
}
