using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

// Scoped with the runner/provider/ingestion/publisher and their one DbContext.
public sealed class JobSourceRunCheckpoint(JobPortalDbContext db, TimeProvider clock) : IJobSourceRunCheckpoint
{
    private JobSourceRun? run;
    private RawExternalJob? current;
    private bool rippleHire;
    private readonly Dictionary<string, JobSourceRunItem> rippleHireReceipts = new(StringComparer.Ordinal);
    private WorkdayRunCheckpoint? stagedState;
    private readonly Dictionary<int, JobSourceRunItem> committed = [];
    private readonly Dictionary<int, JobSourceRunItem> staged = [];
    private readonly Dictionary<JobIngestionOutcome, int> ingestionCounts = [];
    private readonly Dictionary<JobAutoPublishOutcome, int> publicationCounts = [];
    private readonly Dictionary<JobIngestionReasonCode, int> reasons = [];
    private readonly Dictionary<string, int> validation = new(StringComparer.Ordinal);
    private readonly Dictionary<JobQualityReasonCode, int> quality = [];
    public Guid? RunId => run?.Id;
    public WorkdayDetailProgress Details { get; } = new();
    public bool Enabled { get; private set; }
    public WorkdayRunCheckpoint? State { get; private set; }

    public async Task BindAsync(JobSourceRun run, CancellationToken ct)
    {
        this.run = run;
        using var input = run.CheckpointJson is null ? null : JsonDocument.Parse(run.CheckpointJson);
        var employerInput = input is not null && input.RootElement.TryGetProperty("kind", out var kind) && kind.GetString() == "employer-direct-v1";
        State = run.CheckpointJson is null || employerInput ? null : JsonSerializer.Deserialize<WorkdayRunCheckpoint>(run.CheckpointJson);
        if (State is { Version: not (1 or 2 or 3 or 4) } ||
            State is { Version: 3 } && (State.ListingDiagnostics is not { IngestionSafe: true } || State.RebaseCount < 1 || State.ArchivedCounters is null) ||
            State is { Version: 2 } && State.ListingDiagnostics is not { IngestionSafe: true, ReconciliationSafe: false } ||
            State is { Version: 1, ListingDiagnostics.ReconciliationSafe: false } ||
            State is { Cursor: < 0 } || State is { ListingTotal: > 10000 } ||
            State is not null && (State.Cursor > State.ListingTotal || State.ObservedJobs.Count + State.ProviderSkipped != State.Cursor))
            throw new JobSourceSnapshotUnsafeException("InvalidCheckpoint");
        await RefreshAsync(ct);
    }

    public void Enable(JobSource source)
    {
        rippleHire = source.AtsType == AtsType.RippleHire;
        Enabled = run is not null && run.JobSourceId == source.Id && (source.AtsType == AtsType.Workday || rippleHire);
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        stagedState = null;
        staged.Clear(); committed.Clear(); rippleHireReceipts.Clear();
        ingestionCounts.Clear(); publicationCounts.Clear(); reasons.Clear(); validation.Clear(); quality.Clear();
        if (run is null) return;
        var rows = await db.Set<JobSourceRunItem>().AsNoTracking().Where(x => x.RunId == run.Id).ToArrayAsync(ct);
        foreach (var row in rows) { committed.Add(row.Ordinal, row); rippleHireReceipts[row.ExternalId] = row; Tally(row, 1); }
    }

    public async Task PrepareAsync(string sourceIdentity, string listingIdentity, int total, int pages, CancellationToken ct,
        WorkdayListingDiagnostics? listingDiagnostics = null)
    {
        if (!Enabled) return;
        if (State is not null)
        {
            if (WorkdayCheckpointRebasePolicy.RequiresRebase(State, sourceIdentity, listingIdentity, total, listingDiagnostics))
            {
                var previous = State;
                var nextState = WorkdayCheckpointRebasePolicy.Restart(previous, listingIdentity, total, pages,
                    listingDiagnostics!, CommittedCounters, clock.GetUtcNow().UtcDateTime);
                // Both archive and receipt retirement commit under the same lease fence.
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    try
                    {
                        await SaveAsync(nextState, ct);
                        await db.Set<JobSourceRunItem>().Where(x => x.RunId == run!.Id).ExecuteDeleteAsync(ct);
                        await transaction.CommitAsync(ct);
                    }
                    catch { State = previous; throw; }
                });
                foreach (var row in db.Set<JobSourceRunItem>().Local.Where(x => x.RunId == run!.Id).ToArray())
                    db.Entry(row).State = EntityState.Detached;
                await RefreshAsync(ct);
                return;
            }
            await SaveAsync(State with { Version = 4, ListingDiagnostics = listingDiagnostics is null ? State.ListingDiagnostics :
                listingDiagnostics with { InfrastructureFailure = State.ListingDiagnostics?.InfrastructureFailure == true } }, ct);
            return;
        }
        var next = new WorkdayRunCheckpoint
        {
            // Older binaries must not resume permissive scans as authoritative snapshots.
            Version = 4,
            SourceIdentity = sourceIdentity, ListingIdentity = listingIdentity, ListingTotal = total,
            ListingPages = pages, CapturedAtUtc = clock.GetUtcNow().UtcDateTime, CommittedAtUtc = clock.GetUtcNow().UtcDateTime,
            ListingDiagnostics = listingDiagnostics
        };
        await SaveAsync(next, ct);
    }

    public async Task CommitAsync(int cursor, int providerSkipped, IReadOnlyCollection<RawExternalJob> observed, CancellationToken ct, WorkdayListingDiagnostics? diagnostics = null)
    {
        if (!Enabled || State is null) return;
        if (cursor < State.Cursor || cursor > State.ListingTotal || observed.Count + providerSkipped != cursor || staged.Count != 0)
            throw new JobSourceSnapshotUnsafeException("InvalidCheckpointAdvance");
        // Retain eligibility facts and identities, not descriptions/application payloads.
        var lean = observed.Select(x => new RawExternalJob
        {
            ExternalId = x.ExternalId, Location = x.Location, AdditionalLocations = x.AdditionalLocations,
            CountryCodes = x.CountryCodes, RemoteEligibilityText = x.RemoteEligibilityText,
            WorkplaceType = x.WorkplaceType, WorkplaceTypeText = x.WorkplaceTypeText,
            SourceOrdinal = x.SourceOrdinal
        }).ToArray();
        await SaveAsync(State with { Cursor = cursor, ProviderSkipped = providerSkipped, ObservedJobs = lean,
            ListingDiagnostics = diagnostics ?? State.ListingDiagnostics,
            CommittedAtUtc = clock.GetUtcNow().UtcDateTime }, ct);
    }

    private async Task SaveAsync(WorkdayRunCheckpoint state, CancellationToken ct)
    {
        var owner = run ?? throw new InvalidOperationException("Run is not bound.");
        var json = JsonSerializer.Serialize(state);
        var now = clock.GetUtcNow().UtcDateTime;
        var progressed = state.Cursor > (State?.Cursor ?? 0);
        // Atomic fence independent of pending job tracking. Never acknowledges before commits.
        var saved = await db.JobSourceRuns.Where(x => x.Id == owner.Id && x.Status == JobSourceRunStatus.Running &&
            x.LeaseOwner == owner.LeaseOwner && x.LeaseExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CheckpointJson, json)
                .SetProperty(x => x.AttemptCount, x => progressed ? 0 : x.AttemptCount), ct);
        if (saved != 1) throw new JobSourceSnapshotUnsafeException("CheckpointLeaseLost");
        State = state;
    }

    private JobSourceRunItem? Receipt(RawExternalJob raw)
    {
        if (!Enabled) return null;
        // A live offset listing can reorder between attempts. RippleHire replay
        // uses external identity, never a positional cursor.
        if (rippleHire) return raw.ExternalId is { } id ? rippleHireReceipts.GetValueOrDefault(id.Trim()) : null;
        if (raw.SourceOrdinal is not { } ordinal) return null;
        var receipt = committed.GetValueOrDefault(ordinal);
        return receipt?.ExternalId == raw.ExternalId?.Trim() ? receipt : null;
    }

    public JobIngestionResult? Replay(RawExternalJob raw) => Receipt(raw) is { } receipt ? new()
    {
        Outcome = (JobIngestionOutcome)receipt.IngestionOutcome, JobId = receipt.JobId,
        ExplicitReasonCode = (JobIngestionReasonCode)receipt.ReasonCode,
        Message = receipt.ValidationReason is null ? null : "Validation:" + receipt.ValidationReason
    } : null;

    public JobAutoPublishResult? ReplayedPublication(RawExternalJob raw) => Receipt(raw) is { PublicationOutcome: { } outcome } receipt
        ? new() { Outcome = (JobAutoPublishOutcome)outcome,
            Reasons = JsonSerializer.Deserialize<JobQualityReasonCode[]>(receipt.PublicationReasonsJson ?? "[]") ?? [] } : null;

    public void SetCurrentItem(RawExternalJob raw) => current = raw;

    public void StageIngestion(RawExternalJob raw, JobIngestionResult result)
    {
        if (!Enabled || raw.JobSourceId != run?.JobSourceId) return;
        if (Receipt(raw) is not null) return;
        if (rippleHire && string.IsNullOrWhiteSpace(raw.ExternalId)) return;
        if (!rippleHire && raw.SourceOrdinal is null) return;
        var ordinal = rippleHire ? committed.Keys.Concat(staged.Keys).DefaultIfEmpty(-1).Max() + 1 : raw.SourceOrdinal!.Value;
        if (rippleHire && ordinal > 9999) throw new JobSourceSnapshotUnsafeException("InvalidReceiptOrdinal");
        if (staged.ContainsKey(ordinal)) return;
        var row = new JobSourceRunItem
        {
            RunId = run!.Id, Ordinal = ordinal, ExternalId = raw.ExternalId?.Trim() ?? string.Empty,
            JobId = result.JobId, IngestionOutcome = (int)result.Outcome, ReasonCode = (int)result.ReasonCode,
            ValidationReason = result.Outcome == JobIngestionOutcome.Invalid ? JobIngestionReasonCodes.ValidationDetail(result.Message) : null,
            CommittedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        if (committed.TryGetValue(ordinal, out var previous))
        {
            // The live detail now represents another requisition at this position.
            // Archive its old counters and replace its receipt atomically with the
            // new ingestion; the old job itself remains untouched.
            var state = stagedState ?? State ?? throw new InvalidOperationException("Checkpoint is not prepared.");
            stagedState = state with { ArchivedCounters = WorkdayCheckpointRebasePolicy.AddCounters(
                ReceiptCounters(previous), state.ArchivedCounters) };
            StageState(stagedState);
            var tracked = db.Set<JobSourceRunItem>().Local.SingleOrDefault(x => x.RunId == row.RunId && x.Ordinal == ordinal);
            if (tracked is null) { tracked = Copy(previous); db.Attach(tracked); }
            db.Entry(tracked).CurrentValues.SetValues(row);
            row = tracked;
        }
        else db.Set<JobSourceRunItem>().Add(row);
        staged.Add(ordinal, row);
    }

    public void StagePublication(Guid jobId, JobAutoPublishResult result)
    {
        if (current is null || Receipt(current) is not { } receipt || receipt.JobId != jobId) return;
        var ordinal = receipt.Ordinal;
        var tracked = db.Set<JobSourceRunItem>().Local.SingleOrDefault(x => x.RunId == receipt.RunId && x.Ordinal == ordinal);
        if (tracked is null) { tracked = Copy(receipt); db.Attach(tracked); }
        tracked.PublicationOutcome = (int)result.Outcome;
        tracked.PublicationReasonsJson = JsonSerializer.Serialize(result.Reasons);
        tracked.PublicationAtUtc = clock.GetUtcNow().UtcDateTime;
        staged[ordinal] = tracked;
    }

    public void ConfirmSaved()
    {
        foreach (var (ordinal, row) in staged)
        {
            if (committed.TryGetValue(ordinal, out var previous)) Tally(previous, -1);
            committed[ordinal] = Copy(row);
            rippleHireReceipts[row.ExternalId] = committed[ordinal];
            Tally(row, 1);
        }
        staged.Clear();
        if (stagedState is not null) { State = stagedState; stagedState = null; }
    }

    public async Task CompleteItemAsync(RawExternalJob raw, JobIngestionResult result, JobAutoPublishResult? publication, CancellationToken ct)
    {
        if (!Enabled) return;
        StageIngestion(raw, result);
        // Normal ingestion already committed its receipt; invalid results need only a receipt.
        if (staged.Count > 0) { await db.SaveChangesAsync(ct); ConfirmSaved(); }
        SetCurrentItem(raw);
        if (publication is not null && ReplayedPublication(raw) is null && result.JobId.HasValue)
        {
            StagePublication(result.JobId.Value, publication);
            if (staged.Count > 0) { await db.SaveChangesAsync(ct); ConfirmSaved(); }
        }
    }

    // Joins source-success bookkeeping so recovery preserves non-destructive completion.
    public void StageCompletedResult(JobSourceRunResult result)
    {
        if (!Enabled || State is null || !result.Succeeded || result.ReconciliationSafe || result.Closed != 0 || State.Cursor != State.ListingTotal) return;
        var next = State with { CompletedResult = result };
        StageState(next);
        State = next;
    }

    private void StageState(WorkdayRunCheckpoint next)
    {
        var owner = run ?? throw new InvalidOperationException("Run is not bound.");
        var row = db.JobSourceRuns.Local.SingleOrDefault(x => x.Id == owner.Id);
        if (row is null) { row = new JobSourceRun { Id = owner.Id }; db.Attach(row); }
        var entry = db.Entry(row).Property(x => x.CheckpointJson);
        entry.CurrentValue = JsonSerializer.Serialize(next); entry.IsModified = true;
    }

    private static JobSourceRunResult ReceiptCounters(JobSourceRunItem row)
    {
        var outcome = (JobIngestionOutcome)row.IngestionOutcome;
        var publication = (JobAutoPublishOutcome?)row.PublicationOutcome;
        var reason = (JobIngestionReasonCode)row.ReasonCode;
        var reasonCounts = new Dictionary<JobIngestionReasonCode, int>();
        if (reason != JobIngestionReasonCode.None) reasonCounts[reason] = 1;
        if (publication == JobAutoPublishOutcome.JobNotFound)
            reasonCounts[JobIngestionReasonCode.AutoPublishFailed] = reasonCounts.GetValueOrDefault(JobIngestionReasonCode.AutoPublishFailed) + 1;
        return new()
        {
            TotalReceived = 1,
            Parsed = 1,
            EligibilityReasonCounts = new Dictionary<string, int> { ["IndiaLocationConfirmed"] = 1 },
            Created = outcome == JobIngestionOutcome.Created ? 1 : 0,
            Updated = outcome == JobIngestionOutcome.Updated ? 1 : 0,
            Unchanged = outcome == JobIngestionOutcome.Unchanged ? 1 : 0,
            Matched = outcome is JobIngestionOutcome.MatchedByUrl or JobIngestionOutcome.MatchedByFingerprint or JobIngestionOutcome.MatchedByFuzzy ? 1 : 0,
            Skipped = outcome is JobIngestionOutcome.Invalid or JobIngestionOutcome.CompanyNotFound ? 1 : 0,
            Failed = (outcome == JobIngestionOutcome.Failed ? 1 : 0) + (publication == JobAutoPublishOutcome.JobNotFound ? 1 : 0),
            Published = publication == JobAutoPublishOutcome.Published ? 1 : 0,
            NeedsReview = publication == JobAutoPublishOutcome.NeedsReview ? 1 : 0,
            QualityRejected = publication == JobAutoPublishOutcome.Rejected ? 1 : 0,
            AutoPublishDisabled = publication == JobAutoPublishOutcome.Disabled ? 1 : 0,
            PublishFailed = publication == JobAutoPublishOutcome.JobNotFound ? 1 : 0,
            ReasonCounts = reasonCounts,
            ValidationReasonCounts = row.ValidationReason is { } detail ? new Dictionary<string, int> { [detail] = 1 } : null,
            QualityReasonCounts = (JsonSerializer.Deserialize<JobQualityReasonCode[]>(row.PublicationReasonsJson ?? "[]") ?? [])
                .Distinct().ToDictionary(x => x, _ => 1)
        };
    }

    private JobSourceRunResult ArchiveCensus(JobSourceRunResult counters)
    {
        var state = State!;
        var decisions = state.ObservedJobs.Select(JobPortal.Application.Services.IndiaJobEligibilityPolicy.Evaluate).ToArray();
        var reasons = decisions.GroupBy(x => x.ReasonCode).ToDictionary(x => x.Key, x => x.Count());
        // A batch can commit receipts before its cursor commits. Include these
        // positively eligible items once, alongside the checkpointed full census.
        var pending = committed.Keys.Count(ordinal => ordinal >= state.Cursor);
        reasons["IndiaLocationConfirmed"] = reasons.GetValueOrDefault("IndiaLocationConfirmed") + pending;
        return (counters with
        {
            TotalReceived = state.Cursor + pending,
            Parsed = state.ObservedJobs.Count + pending,
            EligibilityReasonCounts = reasons,
            SelectionReasonCounts = decisions.Where(x => x.Decision != JobPortal.Application.Services.IndiaJobEligibilityDecision.Accepted)
                .GroupBy(x => x.ReasonCode).ToDictionary(x => x.Key, x => x.Count())
        }).WithArchivedCensus(state.ArchivedCounters);
    }

    public JobSourceRunResult CommittedCounters
    {
        get
        {
            int Ingestion(JobIngestionOutcome outcome) => ingestionCounts.GetValueOrDefault(outcome);
            int Publication(JobAutoPublishOutcome outcome) => publicationCounts.GetValueOrDefault(outcome);
            var counters = WorkdayCheckpointRebasePolicy.AddCounters(new()
            {
                TotalReceived = committed.Count,
                Created = Ingestion(JobIngestionOutcome.Created), Updated = Ingestion(JobIngestionOutcome.Updated),
                Unchanged = Ingestion(JobIngestionOutcome.Unchanged), Matched = Ingestion(JobIngestionOutcome.MatchedByUrl) +
                    Ingestion(JobIngestionOutcome.MatchedByFingerprint) + Ingestion(JobIngestionOutcome.MatchedByFuzzy),
                Skipped = Ingestion(JobIngestionOutcome.Invalid) + Ingestion(JobIngestionOutcome.CompanyNotFound),
                Failed = Ingestion(JobIngestionOutcome.Failed) + Publication(JobAutoPublishOutcome.JobNotFound),
                Published = Publication(JobAutoPublishOutcome.Published), NeedsReview = Publication(JobAutoPublishOutcome.NeedsReview),
                QualityRejected = Publication(JobAutoPublishOutcome.Rejected), AutoPublishDisabled = Publication(JobAutoPublishOutcome.Disabled),
                PublishFailed = Publication(JobAutoPublishOutcome.JobNotFound), ReasonCounts = new Dictionary<JobIngestionReasonCode, int>(reasons),
                ValidationReasonCounts = new Dictionary<string, int>(validation, StringComparer.Ordinal),
                QualityReasonCounts = new Dictionary<JobQualityReasonCode, int>(quality)
            }, State?.ArchivedCounters);
            return State is null ? counters : ArchiveCensus(counters);
        }
    }

    private void Tally(JobSourceRunItem item, int delta)
    {
        Add(ingestionCounts, (JobIngestionOutcome)item.IngestionOutcome, delta);
        if (item.ReasonCode != (int)JobIngestionReasonCode.None) Add(reasons, (JobIngestionReasonCode)item.ReasonCode, delta);
        if (item.ValidationReason is { } detail) Add(validation, detail, delta);
        if (item.PublicationOutcome is { } outcome)
        {
            Add(publicationCounts, (JobAutoPublishOutcome)outcome, delta);
            if (outcome == (int)JobAutoPublishOutcome.JobNotFound) Add(reasons, JobIngestionReasonCode.AutoPublishFailed, delta);
        }
        foreach (var reason in (JsonSerializer.Deserialize<JobQualityReasonCode[]>(item.PublicationReasonsJson ?? "[]") ?? []).Distinct())
            Add(quality, reason, delta);
    }

    private static void Add<TKey>(Dictionary<TKey, int> counters, TKey key, int delta) where TKey : notnull
    {
        var value = counters.GetValueOrDefault(key) + delta;
        if (value == 0) counters.Remove(key); else counters[key] = value;
    }

    private static JobSourceRunItem Copy(JobSourceRunItem r) => new()
    {
        RunId = r.RunId, Ordinal = r.Ordinal, ExternalId = r.ExternalId, JobId = r.JobId,
        IngestionOutcome = r.IngestionOutcome, ReasonCode = r.ReasonCode, ValidationReason = r.ValidationReason,
        PublicationOutcome = r.PublicationOutcome, PublicationReasonsJson = r.PublicationReasonsJson,
        CommittedAtUtc = r.CommittedAtUtc, PublicationAtUtc = r.PublicationAtUtc
    };
}
