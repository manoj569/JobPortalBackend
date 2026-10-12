namespace JobPortal.Application.Abstractions.Jobs;

/// <summary>A fresh authoritative census may replace traversal, never source identity.</summary>
public static class WorkdayCheckpointRebasePolicy
{
    public static bool RequiresRebase(WorkdayRunCheckpoint state, string sourceIdentity,
        string listingIdentity, int total, WorkdayListingDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SourceIdentity != sourceIdentity)
            throw new JobSourceSnapshotUnsafeException("ListingSnapshotIncompatible", diagnostics);
        var changed = state.ListingIdentity != listingIdentity || state.ListingTotal != total;
        var replacesRestricted = state.ListingDiagnostics is { ReconciliationSafe: false } &&
            diagnostics is { ReconciliationSafe: true };
        if (!changed && !replacesRestricted) return false;
        if (diagnostics is not { IngestionSafe: true, SnapshotStable: true, ReconciliationSafe: true } ||
            diagnostics.UniquePaths != total || diagnostics.RawSlots != total ||
            diagnostics.DuplicatePaths != 0 || diagnostics.PlaceholderCount != 0)
            throw new JobSourceSnapshotUnsafeException("ListingSnapshotIncompatible", diagnostics);
        return true;
    }

    public static WorkdayRunCheckpoint Restart(WorkdayRunCheckpoint state, string listingIdentity,
        int total, int pages, WorkdayListingDiagnostics diagnostics, JobSourceRunResult committed, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with { Version = 3, RebaseCount = checked(state.RebaseCount + 1),
            ListingIdentity = listingIdentity, ListingTotal = total, ListingPages = pages,
            Cursor = 0, ProviderSkipped = 0, ObservedJobs = [], CompletedResult = null,
            ListingDiagnostics = diagnostics, CapturedAtUtc = now, CommittedAtUtc = now,
            ArchivedCounters = committed };
    }

    // Only receipt-backed counters are cumulative. Census/authority belong to the current traversal.
    public static JobSourceRunResult AddCounters(JobSourceRunResult current, JobSourceRunResult? archived)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (archived is null) return current;
        return current with {
            TotalReceived = current.TotalReceived + archived.TotalReceived,
            Created = current.Created + archived.Created, Updated = current.Updated + archived.Updated,
            Unchanged = current.Unchanged + archived.Unchanged, Matched = current.Matched + archived.Matched,
            Skipped = current.Skipped + archived.Skipped, Failed = current.Failed + archived.Failed,
            Published = current.Published + archived.Published, NeedsReview = current.NeedsReview + archived.NeedsReview,
            QualityRejected = current.QualityRejected + archived.QualityRejected,
            PublishFailed = current.PublishFailed + archived.PublishFailed,
            AutoPublishDisabled = current.AutoPublishDisabled + archived.AutoPublishDisabled,
            ReasonCounts = Add(current.ReasonCounts, archived.ReasonCounts),
            ValidationReasonCounts = Add(current.ValidationReasonCounts, archived.ValidationReasonCounts),
            QualityReasonCounts = Add(current.QualityReasonCounts, archived.QualityReasonCounts) };
    }

    private static Dictionary<T, int> Add<T>(IReadOnlyDictionary<T, int>? current,
        IReadOnlyDictionary<T, int>? archived) where T : notnull
    {
        var result = current?.ToDictionary(x => x.Key, x => x.Value) ?? [];
        if (archived is not null)
            foreach (var (key, value) in archived) result[key] = result.GetValueOrDefault(key) + value;
        return result;
    }
}
