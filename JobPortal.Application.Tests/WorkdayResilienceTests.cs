using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class WorkdayResilienceTests
{
    [Theory]
    [InlineData(174, 174, 174, false, "Complete", true)]
    [InlineData(174, 174, 174, true, "Complete", true)]
    [InlineData(21, 21, 21, false, "Complete", true)]
    [InlineData(1086, 1085, 1085, true, "ListingTotalDrift", false)]
    [InlineData(1085, 1085, 1086, true, "ListingTotalDrift", false)]
    [InlineData(1086, 1085, 1086, true, "ListingEndedEarly", false)]
    [InlineData(1086, 1050, 1050, true, "ListingTotalDrift", false)]
    [InlineData(1086, 1100, 1100, true, "ListingTotalDrift", false)]
    public async Task CountsAndFinalAnchorsNeverAuthorizeDriftedMembership(int initial, int observed, int final,
        bool omitted, string reason, bool complete)
    {
        using var handler = new Board(initial, observed, final) { OmitContinuationTotal = omitted };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var snapshot = await provider.FetchSnapshotAsync(Source());
        Assert.Equal(observed, snapshot.Jobs.Count);
        Assert.Equal(complete, snapshot.IsComplete);
        var evidence = Assert.IsType<WorkdayListingDiagnostics>(snapshot.ListingDiagnostics);
        Assert.Equal(initial, evidence.InitialReportedTotal);
        Assert.Equal(observed, evidence.RawSlots);
        Assert.Equal(observed, evidence.UniquePaths);
        Assert.Equal(0, evidence.DuplicatePaths);
        Assert.Equal(0, evidence.PlaceholderCount);
        Assert.True(evidence.IngestionSafe);
        Assert.Equal(complete, evidence.ReconciliationSafe);
        Assert.Equal(reason, evidence.ReasonCode);
        Assert.Equal(!complete, evidence.RetryRecommended);
        var anchorRequired = omitted || observed != initial;
        Assert.Equal(anchorRequired ? (int?)final : null, evidence.FinalReportedTotal);
        Assert.Equal((observed + 19) / 20 + (anchorRequired ? 1 : 0), handler.ListingRequests);
        Assert.Equal(observed, handler.DetailRequests);
        Assert.Equal(observed, snapshot.Jobs.Select(job => job.ExternalId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task LiterallyMissingContinuationTotalStillUsesTrustedInitialAnchor()
    {
        using var handler = new Board(41, 41, 41) { MissingContinuationTotal = true };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var snapshot = await provider.FetchSnapshotAsync(Source());
        Assert.True(snapshot.IsComplete);
        Assert.Equal(41, snapshot.Jobs.Count);
        Assert.Equal(4, handler.ListingRequests);
    }

    [Fact]
    public async Task MissingInitialTotalCannotCertifyEvenAnEmptyBoard()
    {
        using var handler = new Board(0, 0, 0) { MissingInitialTotal = true };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(Source()));
        Assert.Equal(0, handler.DetailRequests);
    }

    [Theory]
    [InlineData("repeated", "RepeatedPage")]
    [InlineData("duplicate", "DuplicateListingPaths")]
    [InlineData("placeholder", "ListingPlaceholders")]
    [InlineData("anchorHttp", "AnchorRevalidationFailed")]
    [InlineData("membership", "ListingMembershipChanged")]
    [InlineData("multipleChanges", "ListingTotalDrift")]
    public async Task StructuralAndAnchorFailuresRemainBoundedAndUnsafe(string mutation, string reason)
    {
        var total = mutation == "repeated" ? 240 : 41;
        using var handler = new Board(total, total, total) { Mutation = mutation, OmitContinuationTotal = true };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var snapshot = await provider.FetchSnapshotAsync(Source());
        Assert.False(snapshot.IsComplete);
        Assert.False(snapshot.ListingDiagnostics!.ReconciliationSafe);
        Assert.Equal(reason, snapshot.ListingDiagnostics.ReasonCode);
        Assert.True(snapshot.ListingDiagnostics.RetryRecommended);
        Assert.InRange(handler.ListingRequests, 2, (total + 19) / 20 + 1);
        if (mutation == "repeated") Assert.Equal(200, snapshot.ListingDiagnostics.LastOffset);
        if (mutation == "duplicate") Assert.Equal(1, snapshot.ListingDiagnostics.DuplicatePaths);
        if (mutation == "placeholder") Assert.Equal(1, snapshot.ListingDiagnostics.PlaceholderCount);
    }

    [Fact]
    public async Task TinyPlaceholderAnomalyAllowsIngestionButNeverReconciliation()
    {
        // 1 / 200 = 0.5%, exactly the conservative ingestion-only tolerance.
        using var handler = new Board(200, 200, 200) { Mutation = "placeholder" };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var snapshot = await provider.FetchSnapshotAsync(Source());

        Assert.False(snapshot.IsComplete);
        Assert.Equal(199, snapshot.Jobs.Count);
        Assert.Equal(199, handler.DetailRequests);
        Assert.True(snapshot.ListingDiagnostics!.IngestionSafe);
        Assert.False(snapshot.ListingDiagnostics.ReconciliationSafe);
        Assert.False(snapshot.ListingDiagnostics.SnapshotStable);
        Assert.Equal(1, snapshot.ListingDiagnostics.PlaceholderCount);
        Assert.Equal("ListingPlaceholders", snapshot.ListingDiagnostics.ReasonCode);
        Assert.True(snapshot.ListingDiagnostics.RetryRecommended);
    }

    [Fact]
    public async Task PlaceholderAnomalyAboveToleranceStillBlocksIngestion()
    {
        // 1 / 199 is slightly above 0.5%, so the existing fail-closed behavior remains.
        using var handler = new Board(199, 199, 199) { Mutation = "placeholder" };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var snapshot = await provider.FetchSnapshotAsync(Source());

        Assert.False(snapshot.IsComplete);
        Assert.False(snapshot.ListingDiagnostics!.IngestionSafe);
        Assert.False(snapshot.ListingDiagnostics.ReconciliationSafe);
        Assert.Equal(1, snapshot.ListingDiagnostics.PlaceholderCount);
        Assert.Equal("ListingPlaceholders", snapshot.ListingDiagnostics.ReasonCode);
        Assert.True(snapshot.ListingDiagnostics.RetryRecommended);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringPaginationOrDetailsStopsWithoutCompleteness(bool details)
    {
        using var cancelled = new CancellationTokenSource();
        using var handler = new Board(41, 41, 41) { Cancel = cancelled, CancelDuringDetails = details };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.FetchSnapshotAsync(Source(), cancelled.Token));
        Assert.InRange(handler.ListingRequests, 1, 3);
    }

    [Fact]
    public async Task EmptyUnexpectedPageAndMalformedPathsAreRejectedBeforeDetails()
    {
        foreach (var mutation in new[] { "empty", "invalidPath" })
        {
            using var handler = new Board(41, 41, 41) { Mutation = mutation };
            using var http = new HttpClient(handler);
            using var provider = Provider(http);
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(Source()));
            Assert.Equal(0, handler.DetailRequests);
        }
    }

    [Fact]
    public async Task DriftAndFailedDetailsPreservePublishedInventoryAndRepeatWithoutDuplicates()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Workday;
        f.Source.AtsIdentifier = "fixture/Careers";
        f.Source.CareerPageUrl = "https://fixture.wd1.myworkdayjobs.com/Careers";
        f.Map(f.Category.Id.ToString());
        await f.Context.SaveChangesAsync();
        using var handler = new Board(43, 43, 43);
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, f.Locks, canonical);
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit, TimeProvider.System, f.Resolver,
            new ExternalJobNormalizer(), jobRepository: jobs);
        Assert.True((await runner.RunAsync(f.Source.Id)).Succeeded);
        foreach (var job in f.Context.Jobs) { job.Status = JobStatus.Published; job.PublishedAtUtc = DateTime.UtcNow; }
        await f.Context.SaveChangesAsync();
        var before = await f.Context.Jobs.ToDictionaryAsync(job => job.ExternalJobId!, job => job.Id);
        var successfulAt = f.Source.LastSuccessfulRunAtUtc;
        handler.Reset(43, 42, 42);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            handler.AnchorReads = 0;
            var result = await runner.RunAsync(f.Source.Id);
            Assert.False(result.Succeeded);
            Assert.False(result.ReconciliationSafe);
            Assert.Equal("ListingTotalDrift", result.StopReason);
            Assert.Equal(42, result.Existing);
            Assert.Equal(0, result.Created);
            Assert.Equal(0, result.Closed);
            Assert.Equal(successfulAt, f.Source.LastSuccessfulRunAtUtc);
        }
        handler.Reset(43, 43, 43);
        handler.FailDetail = true;
        var incomplete = await runner.RunAsync(f.Source.Id);
        Assert.False(incomplete.ReconciliationSafe);
        Assert.Equal("FailedDetails", incomplete.StopReason);
        Assert.Equal(0, incomplete.Closed);
        Assert.All(f.Context.Jobs, job => { Assert.Equal(before[job.ExternalJobId!], job.Id); Assert.Equal(JobStatus.Published, job.Status); });
        Assert.Equal(43, await f.Context.Jobs.CountAsync());
        handler.Reset(43, 43, 43);
        handler.Mutation = "duplicate";
        var structural = await runner.RunAsync(f.Source.Id);
        Assert.False(structural.ReconciliationSafe);
        Assert.Equal("DuplicateListingPaths", structural.StopReason);
        Assert.Equal(0, structural.Created);
        Assert.Equal(0, structural.Existing);
        Assert.Equal(0, structural.Closed);
        Assert.False(structural.ListingDiagnostics!.IngestionSafe);
        Assert.All(f.Context.Jobs, job => Assert.Equal(JobStatus.Published, job.Status));
    }

    private static WorkdayJobSourceProvider Provider(HttpClient http) => new(new Factory(http),
        options: Options.Create(new JobAggregationOptions { Workday = new() { MaximumAttempts = 1 } }),
        timeProvider: new FastClock());

    [Fact]
    public async Task LegacyCheckpointsRemainReadableButVersionTwoRequiresTheSafetyRestriction()
    {
        using var f = new JobSourceFixture();
        var checkpoint = new JobSourceRunCheckpoint(f.Context, TimeProvider.System);
        await checkpoint.BindAsync(new JobSourceRun { CheckpointJson = """{"Version":1,"ListingTotal":0,"Cursor":0,"ObservedJobs":[]}""" }, default);
        Assert.Equal(1, checkpoint.State!.Version);
        Assert.Null(checkpoint.State.ListingDiagnostics);
        var invalid = await Assert.ThrowsAsync<JobSourceSnapshotUnsafeException>(() => checkpoint.BindAsync(
            new JobSourceRun { CheckpointJson = """{"Version":2,"ListingTotal":0,"Cursor":0,"ObservedJobs":[]}""" }, default));
        Assert.Equal("InvalidCheckpoint", invalid.ReasonCode);
    }
    [Theory]
    [InlineData(21, false, false)]
    [InlineData(22, false, false)]
    [InlineData(20, false, false)]
    [InlineData(21, true, false)]
    [InlineData(21, false, true)]
    public async Task DurableRunnerRebasesChangedCensusAndKeepsPositiveJobs(int replacementTotal, bool reverse, bool drift)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Workday; f.Source.AtsIdentifier = "fixture/Careers";
        f.Source.CareerPageUrl = "https://fixture.wd1.myworkdayjobs.com/Careers";
        f.Map(f.Category.Id.ToString()); await f.Context.SaveChangesAsync();
        var journal = new MemoryCheckpoint(); journal.Enable(f.Source);
        using var board = new Board(21, 21, drift ? 22 : 21) { OmitContinuationTotal = drift };
        using var http = new HttpClient(board);
        using var provider = new WorkdayJobSourceProvider(new Factory(http), checkpoint: journal, timeProvider: new FastClock(),
            options: Options.Create(new JobAggregationOptions { Workday = new() { JobsPerExecution = 5, BatchSize = 5, MaximumAttempts = 1 } }));
        var jobs = new JobRepository(f.Context); var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService(); var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, f.Locks, canonical, journal);
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit, TimeProvider.System, f.Resolver,
            new ExternalJobNormalizer(), jobRepository: jobs, checkpoint: journal);
        var first = await runner.RunAsync(f.Source.Id);
        Assert.True(first.ContinuationPending); Assert.Equal(5, first.Created); Assert.False(first.ReconciliationSafe);
        board.Reset(replacementTotal, replacementTotal, replacementTotal); board.ReverseOrder = reverse;
        var changed = replacementTotal != 21 || reverse || drift;
        JobSourceRunResult result;
        var executions = 0;
        do { result = await runner.RunAsync(f.Source.Id); Assert.Equal(0, result.Failed); executions++; }
        while (result.ContinuationPending && executions < 12);
        Assert.False(result.ContinuationPending);
        Assert.True(result.Succeeded); Assert.True(result.ReconciliationSafe);
        Assert.Equal(replacementTotal, result.ObservedIdentities); Assert.Equal(replacementTotal, result.CheckpointCursor);
        Assert.Equal(replacementTotal, result.Created); Assert.Equal(changed ? 5 : 0, result.Existing);
        Assert.Equal(changed ? 1 : 0, journal.State!.RebaseCount);
        Assert.Equal(replacementTotal, await f.Context.Jobs.CountAsync());
        Assert.Equal(replacementTotal + (changed ? 5 : 0), board.DetailRequests);
    }

    [Fact]
    public async Task Production1094To1095DriftFetchesReplacementInsteadOfStoppingBeforeDetails()
    {
        var source = Source(); var journal = new MemoryCheckpoint(); journal.Enable(source);
        using var board = new Board(1094, 1094, 1095) { OmitContinuationTotal = true, Mutation = "membership" };
        using var http = new HttpClient(board);
        using var provider = new WorkdayJobSourceProvider(new Factory(http), checkpoint: journal, timeProvider: new FastClock(),
            options: Options.Create(new JobAggregationOptions
            {
                Workday = new()
                {
                    JobsPerExecution = 200,
                    BatchSize = 100,
                    MaximumAttempts = 1,
                    DetailConcurrency = 4,
                    RunBudgetSeconds = 300
                }
            }));
        var committedIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        async Task<ExternalJobSourceSnapshot> Execute()
        {
            var observed = journal.State?.ObservedJobs.ToList() ?? [];
            var rebases = journal.State?.RebaseCount ?? 0;
            ExternalJobSourceSnapshot? last = null;
            await foreach (var batch in provider.FetchBatchesAsync(source, default))
            {
                if (journal.State!.RebaseCount != rebases) { observed.Clear(); rebases = journal.State.RebaseCount; }
                observed.AddRange(batch.Jobs);
                foreach (var raw in batch.Jobs)
                {
                    var externalId = Assert.IsType<string>(raw.ExternalId);
                    var id = int.Parse(externalId.AsSpan(2), CultureInfo.InvariantCulture);
                    if (id >= 94) continue; // Production fixture's 94 accepted positive ingestions.
                    var existed = committedIds.TryGetValue(externalId, out var jobId);
                    if (!existed) { jobId = Guid.NewGuid(); committedIds.Add(externalId, jobId); }
                    await journal.CompleteItemAsync(raw, new()
                    {
                        JobId = jobId,
                        Outcome = existed ? JobIngestionOutcome.Unchanged : JobIngestionOutcome.Created
                    },
                        new() { Outcome = !existed && id < 80 ? JobAutoPublishOutcome.Published : JobAutoPublishOutcome.AlreadyPublished }, default);
                }
                await journal.CommitAsync(batch.CheckpointCursor!.Value, batch.Skipped, observed, default);
                last = batch;
            }
            return last!;
        }
        var first = await Execute();
        Assert.True(first.ContinuationPending); Assert.False(first.IsComplete);
        Assert.True(first.ListingDiagnostics!.FirstPageChanged);
        Assert.Equal(200, journal.State!.Cursor); Assert.Equal(1094, journal.State.ListingTotal);
        Assert.Equal(94, journal.CommittedCounters.Created); Assert.Equal(80, journal.CommittedCounters.Published);
        board.Reset(1095, 1095, 1095); board.Mutation = null;
        var second = await Execute();
        Assert.True(second.ContinuationPending); Assert.Equal(400, board.DetailRequests);
        Assert.Equal(1095, journal.State.ListingTotal); Assert.Equal(200, journal.State.Cursor);
        Assert.Equal(1, journal.State.RebaseCount); Assert.False(second.IsComplete);
        Assert.Equal(94, journal.CommittedCounters.Created); Assert.Equal(94, journal.CommittedCounters.Existing);
        ExternalJobSourceSnapshot completed = second;
        var executions = 0;
        while (completed.ContinuationPending && executions++ < 8) completed = await Execute();
        Assert.False(completed.ContinuationPending);
        Assert.True(completed.IsComplete); Assert.True(completed.ListingDiagnostics!.ReconciliationSafe);
        Assert.Equal(1095, journal.State.Cursor); Assert.Equal(94, committedIds.Count);
        Assert.Equal(94, journal.CommittedCounters.Created); Assert.Equal(80, journal.CommittedCounters.Published);
        Assert.Equal(1295, board.DetailRequests);
    }

    [Fact]
    public async Task ProductionDriftReplacementArchives94CreationsAnd80PublicationsAndRestartsAtZero()
    {
        var evidence = new WorkdayListingDiagnostics
        {
            IngestionSafe = true,
            SnapshotStable = true,
            ReconciliationSafe = true,
            InitialReportedTotal = 1095,
            FinalReportedTotal = 1095,
            UniquePaths = 1095,
            RawSlots = 1095,
            ReasonCode = "Complete"
        };
        var old = new WorkdayRunCheckpoint
        {
            Version = 2,
            SourceIdentity = "source",
            ListingIdentity = "old",
            ListingTotal = 1094,
            Cursor = 200,
            ObservedJobs = Enumerable.Range(0, 200).Select(i => new RawExternalJob { ExternalId = $"R-{i}" }).ToArray(),
            ListingDiagnostics = evidence with
            {
                SnapshotStable = false,
                ReconciliationSafe = false,
                InitialReportedTotal = 1094,
                UniquePaths = 1094,
                RawSlots = 1094,
                FirstPageChanged = true,
                ReasonCode = "ListingTotalDrift"
            }
        };
        Assert.True(WorkdayCheckpointRebasePolicy.RequiresRebase(old, "source", "new", 1095, evidence));
        var reset = WorkdayCheckpointRebasePolicy.Restart(old, "new", 1095, 55, evidence,
            new JobSourceRunResult { Created = 94, Published = 80, TotalReceived = 94 }, DateTime.UtcNow);
        var restored = JsonSerializer.Deserialize<WorkdayRunCheckpoint>(JsonSerializer.Serialize(reset))!;
        Assert.Equal(0, restored.Cursor); Assert.Empty(restored.ObservedJobs); Assert.Equal(1095, restored.ListingTotal);
        Assert.Equal(3, restored.Version); Assert.Equal(1, restored.RebaseCount); Assert.Null(restored.CompletedResult);
        var counters = WorkdayCheckpointRebasePolicy.AddCounters(new() { Unchanged = 94 }, restored.ArchivedCounters);
        Assert.Equal(94, counters.Created); Assert.Equal(94, counters.Existing); Assert.Equal(80, counters.Published);
        Assert.False(counters.ReconciliationSafe); Assert.True(restored.ListingDiagnostics!.SnapshotStable);
        using var fixture = new JobSourceFixture();
        var journal = new JobSourceRunCheckpoint(fixture.Context, TimeProvider.System);
        await journal.BindAsync(new JobSourceRun { CheckpointJson = JsonSerializer.Serialize(restored) }, default);
        Assert.Equal(94, journal.CommittedCounters.Created); Assert.Equal(80, journal.CommittedCounters.Published);
        Assert.False(WorkdayCheckpointRebasePolicy.RequiresRebase(restored, "source", "new", 1095, evidence));
        var twice = WorkdayCheckpointRebasePolicy.Restart(restored, "newer", 1096, 55,
            evidence with { UniquePaths = 1096, RawSlots = 1096 }, counters, DateTime.UtcNow);
        Assert.Equal(2, twice.RebaseCount); Assert.Equal(94, twice.ArchivedCounters!.Created);
        Assert.Equal(94, twice.ArchivedCounters.Existing); Assert.Equal(80, twice.ArchivedCounters.Published);
    }

    [Theory]
    [InlineData("DuplicateListingPaths")]
    [InlineData("ListingEndedEarly")]
    [InlineData("ListingTotalDrift")]
    [InlineData("FailedDetails")]
    public void UnsafeReplacementCannotRebaseOrUpgradeAuthority(string reason)
    {
        var state = new WorkdayRunCheckpoint { SourceIdentity = "source", ListingIdentity = "old", ListingTotal = 21, Cursor = 5 };
        var evidence = new WorkdayListingDiagnostics { IngestionSafe = true, ReconciliationSafe = false, ReasonCode = reason };
        Assert.Throws<JobSourceSnapshotUnsafeException>(() => WorkdayCheckpointRebasePolicy.RequiresRebase(state, "source", "new", 22, evidence));
        Assert.Throws<JobSourceSnapshotUnsafeException>(() => WorkdayCheckpointRebasePolicy.RequiresRebase(state, "different-source", "old", 21, evidence));
        Assert.Equal(5, state.Cursor);
    }

    // Tests provider + real ingestion/runner; PostgreSQL tests cover atomic persistence and lease fencing.
    private sealed class MemoryCheckpoint : IJobSourceRunCheckpoint
    {
        private readonly Dictionary<int, (string? Id, JobIngestionResult Result)> receipts = [];
        private readonly Dictionary<int, JobAutoPublishResult> publications = [];
        public WorkdayDetailProgress Details { get; } = new();
        public Guid? RunId { get; } = Guid.NewGuid();
        public bool Enabled { get; private set; }
        public WorkdayRunCheckpoint? State { get; private set; }
        public JobSourceRunResult CommittedCounters => WorkdayCheckpointRebasePolicy.AddCounters(new()
        {
            TotalReceived = receipts.Count,
            Created = receipts.Values.Count(x => x.Result.Outcome == JobIngestionOutcome.Created),
            Published = publications.Values.Count(x => x.Outcome == JobAutoPublishOutcome.Published),
            Updated = receipts.Values.Count(x => x.Result.Outcome == JobIngestionOutcome.Updated),
            Unchanged = receipts.Values.Count(x => x.Result.Outcome == JobIngestionOutcome.Unchanged),
            Matched = receipts.Values.Count(x => x.Result.Outcome is JobIngestionOutcome.MatchedByUrl or JobIngestionOutcome.MatchedByFingerprint or JobIngestionOutcome.MatchedByFuzzy),
            Failed = receipts.Values.Count(x => x.Result.Outcome == JobIngestionOutcome.Failed)
        }, State?.ArchivedCounters);
        public void Enable(JobSource source) => Enabled = source.AtsType == AtsType.Workday;
        public Task BindAsync(JobSourceRun run, CancellationToken ct) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken ct) => Task.CompletedTask;
        public Task PrepareAsync(string sourceIdentity, string listingIdentity, int total, int pages, CancellationToken ct,
            WorkdayListingDiagnostics? listingDiagnostics = null)
        {
            if (State is not null && WorkdayCheckpointRebasePolicy.RequiresRebase(State, sourceIdentity, listingIdentity, total, listingDiagnostics))
            {
                State = WorkdayCheckpointRebasePolicy.Restart(State, listingIdentity, total, pages, listingDiagnostics!, CommittedCounters, DateTime.UtcNow);
                receipts.Clear(); publications.Clear();
            }
            State ??= new()
            {
                SourceIdentity = sourceIdentity,
                ListingIdentity = listingIdentity,
                ListingTotal = total,
                ListingPages = pages,
                ListingDiagnostics = listingDiagnostics
            };
            return Task.CompletedTask;
        }
        public Task CommitAsync(int cursor, int providerSkipped, IReadOnlyCollection<RawExternalJob> observed, CancellationToken ct)
        {
            Assert.Equal(cursor, observed.Count + providerSkipped);
            State = State! with { Cursor = cursor, ProviderSkipped = providerSkipped, ObservedJobs = observed.ToArray() };
            return Task.CompletedTask;
        }
        public JobIngestionResult? Replay(RawExternalJob raw)
        {
            if (raw.SourceOrdinal is not { } ordinal || !receipts.TryGetValue(ordinal, out var receipt)) return null;
            Assert.Equal(receipt.Id, raw.ExternalId); return receipt.Result;
        }
        public JobAutoPublishResult? ReplayedPublication(RawExternalJob raw) => raw.SourceOrdinal is { } ordinal ? publications.GetValueOrDefault(ordinal) : null;
        public void SetCurrentItem(RawExternalJob raw) { }
        public void StageIngestion(RawExternalJob raw, JobIngestionResult result)
        { if (raw.SourceOrdinal is { } ordinal) receipts.TryAdd(ordinal, (raw.ExternalId, result)); }
        public void ConfirmSaved() { }
        public void StageCompletedResult(JobSourceRunResult result) { }
        public void StagePublication(Guid jobId, JobAutoPublishResult result) { }
        public Task CompleteItemAsync(RawExternalJob raw, JobIngestionResult result, JobAutoPublishResult? publication, CancellationToken ct)
        { StageIngestion(raw, result); if (publication is not null && raw.SourceOrdinal is { } ordinal) publications.TryAdd(ordinal, publication); return Task.CompletedTask; }
    }

    private static JobSource Source() => new()
    {
        AtsType = AtsType.Workday,
        AtsIdentifier = "fixture/Careers",
        CareerPageUrl = "https://fixture.wd1.myworkdayjobs.com/Careers",
        Company = new Company { Name = "Fixture" }
    };
    private sealed class Factory(HttpClient http) : IHttpClientFactory { public HttpClient CreateClient(string name) => http; }
    private sealed class FastClock : TimeProvider
    { private long seconds; public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddSeconds(Interlocked.Increment(ref seconds)); }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private sealed class Board(int initial, int observed, int final) : HttpMessageHandler
    {
        public bool ReverseOrder { get; set; }
        public bool OmitContinuationTotal { get; init; }
        public bool MissingContinuationTotal { get; init; }
        public bool MissingInitialTotal { get; init; }
        public string? Mutation { get; set; }
        public CancellationTokenSource? Cancel { get; init; }
        public bool CancelDuringDetails { get; init; }
        public bool FailDetail { get; set; }
        public int AnchorReads { get; set; }
        public int ListingRequests { get; private set; }
        public int DetailRequests;
        public void Reset(int newInitial, int newObserved, int newFinal)
        { initial = newInitial; observed = newObserved; final = newFinal; AnchorReads = 0; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post)
            {
                ListingRequests++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var offset = body.RootElement.GetProperty("offset").GetInt32();
                if (Cancel is not null && !CancelDuringDetails && offset > 0) { Cancel.Cancel(); token.ThrowIfCancellationRequested(); }
                if (offset == 0) AnchorReads++;
                if (offset == 0 && AnchorReads > 1 && Mutation == "anchorHttp") return new(HttpStatusCode.ServiceUnavailable);
                var start = Mutation == "repeated" && offset == 200 ? 0 : offset;
                var count = Math.Min(20, Math.Max(0, observed - offset));
                if (Mutation == "empty" && offset == 20) count = 0;
                var entries = Enumerable.Range(start, count).Select(i => ReverseOrder ? observed - 1 - i : i).Select(i => new
                {
                    title = $"Software Engineer {i}",
                    externalPath = Mutation == "invalidPath" ? "/../invalid" :
                        $"/job/Pune/Engineer_{(Mutation == "duplicate" && i == 20 ? 19 : i)}",
                    locationsText = "Pune, India",
                    bulletFields = new[] { $"R-{i}" }
                }).Cast<object>().ToList();
                if (offset == 0 && AnchorReads > 1 && Mutation == "membership")
                    entries[0] = new { title = "New Engineer", externalPath = "/job/Pune/Engineer_999", locationsText = "Pune, India" };
                if (Mutation == "placeholder" && offset == 20) entries[0] = new { bulletFields = Array.Empty<string>() };
                var document = new Dictionary<string, object> { ["jobPostings"] = entries };
                if (!(offset > 0 && MissingContinuationTotal) && !(offset == 0 && MissingInitialTotal))
                    document["total"] = offset == 0 ? AnchorReads > 1 ? final : initial :
                        Mutation == "multipleChanges" ? initial + 2 : OmitContinuationTotal ? 0 : initial;
                return Json(JsonSerializer.Serialize(document));
            }
            Interlocked.Increment(ref DetailRequests);
            if (Cancel is not null && CancelDuringDetails) { Cancel.Cancel(); token.ThrowIfCancellationRequested(); }
            var id = int.Parse(request.RequestUri!.AbsolutePath.Split('_').Last(), CultureInfo.InvariantCulture);
            if (FailDetail && id == 0) return new(HttpStatusCode.ServiceUnavailable);
            return Json(JsonSerializer.Serialize(new
            {
                jobPostingInfo = new
                {
                    title = $"Software Engineer {id}",
                    jobReqId = $"R-{id}",
                    location = "Pune, India",
                    timeType = "Full time",
                    country = new { alpha2Code = "IN" },
                    jobDescription = $"Build and maintain reliable software product {id}. Minimum 3 years experience required."
                }
            }));
        }
    }
}
