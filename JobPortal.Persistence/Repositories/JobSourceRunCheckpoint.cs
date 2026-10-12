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
        State = run.CheckpointJson is null ? null : JsonSerializer.Deserialize<WorkdayRunCheckpoint>(run.CheckpointJson);
        if (State is { Version: not 1 } || State is { Cursor: < 0 } || State is { ListingTotal: > 10000 } ||
            State is not null && (State.Cursor > State.ListingTotal || State.ObservedJobs.Count + State.ProviderSkipped != State.Cursor))
            throw new JobSourceSnapshotUnsafeException("InvalidCheckpoint");
        await RefreshAsync(ct);
    }

    public void Enable(JobSource source) => Enabled = run is not null && run.JobSourceId == source.Id && source.AtsType == AtsType.Workday;

    public async Task RefreshAsync(CancellationToken ct)
    {
        staged.Clear(); committed.Clear();
        ingestionCounts.Clear(); publicationCounts.Clear(); reasons.Clear(); validation.Clear(); quality.Clear();
        if (run is null) return;
        var rows = await db.Set<JobSourceRunItem>().AsNoTracking().Where(x => x.RunId == run.Id).ToArrayAsync(ct);
        foreach (var row in rows) { committed.Add(row.Ordinal, row); Tally(row, 1); }
    }

    public async Task PrepareAsync(string sourceIdentity, string listingIdentity, int total, int pages, CancellationToken ct)
    {
        if (!Enabled) return;
        if (State is not null)
        {
            if (State.SourceIdentity != sourceIdentity || State.ListingIdentity != listingIdentity || State.ListingTotal != total)
                throw new JobSourceSnapshotUnsafeException("ListingSnapshotIncompatible");
            return;
        }
        var next = new WorkdayRunCheckpoint
        {
            SourceIdentity = sourceIdentity, ListingIdentity = listingIdentity, ListingTotal = total,
            ListingPages = pages, CapturedAtUtc = clock.GetUtcNow().UtcDateTime, CommittedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        await SaveAsync(next, ct);
    }

    public async Task CommitAsync(int cursor, int providerSkipped, IReadOnlyCollection<RawExternalJob> observed, CancellationToken ct)
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
        if (!Enabled || raw.SourceOrdinal is not { } ordinal) return null;
        var receipt = committed.GetValueOrDefault(ordinal);
        if (receipt is not null && receipt.ExternalId != raw.ExternalId?.Trim())
            throw new JobSourceSnapshotUnsafeException("DetailIdentityChanged");
        return receipt;
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
        if (!Enabled || raw.SourceOrdinal is not { } ordinal || raw.JobSourceId != run?.JobSourceId) return;
        if (Receipt(raw) is not null || staged.ContainsKey(ordinal)) return;
        var row = new JobSourceRunItem
        {
            RunId = run!.Id, Ordinal = ordinal, ExternalId = raw.ExternalId?.Trim() ?? string.Empty,
            JobId = result.JobId, IngestionOutcome = (int)result.Outcome, ReasonCode = (int)result.ReasonCode,
            ValidationReason = result.Outcome == JobIngestionOutcome.Invalid ? JobIngestionReasonCodes.ValidationDetail(result.Message) : null,
            CommittedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        db.Set<JobSourceRunItem>().Add(row); staged.Add(ordinal, row);
    }

    public void StagePublication(Guid jobId, JobAutoPublishResult result)
    {
        if (!Enabled || current?.SourceOrdinal is not { } ordinal || !committed.TryGetValue(ordinal, out var receipt) || receipt.JobId != jobId) return;
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
            Tally(row, 1);
        }
        staged.Clear();
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

    // Joins the existing closure/source-success transaction. Recovery preserves its exact Closed counter.
    public void StageCompletedResult(JobSourceRunResult result)
    {
        if (!Enabled || State is null || !result.Succeeded || !result.ReconciliationSafe || State.Cursor != State.ListingTotal) return;
        var owner = run ?? throw new InvalidOperationException("Run is not bound.");
        var row = db.JobSourceRuns.Local.SingleOrDefault(x => x.Id == owner.Id);
        if (row is null) { row = new JobSourceRun { Id = owner.Id }; db.Attach(row); }
        var next = State with { CompletedResult = result };
        var entry = db.Entry(row).Property(x => x.CheckpointJson);
        entry.CurrentValue = JsonSerializer.Serialize(next); entry.IsModified = true;
        State = next;
    }

    public JobSourceRunResult CommittedCounters
    {
        get
        {
            int Ingestion(JobIngestionOutcome outcome) => ingestionCounts.GetValueOrDefault(outcome);
            int Publication(JobAutoPublishOutcome outcome) => publicationCounts.GetValueOrDefault(outcome);
            return new()
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
            };
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
