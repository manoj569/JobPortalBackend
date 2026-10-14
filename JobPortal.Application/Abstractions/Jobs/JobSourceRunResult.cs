namespace JobPortal.Application.Abstractions.Jobs;

public sealed record JobSourceRunResult
{
    public Guid JobSourceId { get; init; }
    public Guid? RunId { get; init; }
    public bool ContinuationPending { get; init; }
    public string? StopReason { get; init; }
    public string? FailureReasonCode { get; init; }
    public string? FailureStage { get; init; }
    public IReadOnlyDictionary<string, int>? ProviderReasonCounts { get; init; }
    public WorkdayListingDiagnostics? ListingDiagnostics { get; init; }
    public int ListingTotal { get; init; }
    public int RebaseCount { get; init; }
    public int ListingPages { get; init; }
    public int ObservedIdentities { get; init; }
    public int DetailAttempted { get; init; }
    public int DetailSucceeded { get; init; }
    public int DetailFailed { get; init; }
    public int DetailInFlight => Math.Max(0, DetailAttempted - DetailSucceeded - DetailFailed);
    public JobSourceRunResult WithDetailProgress(WorkdayDetailProgress details) => details.ApplyTo(this);
    public int CheckpointCursor { get; init; }
    public int? RemainingWork { get; init; }
    public DateTime? LastCheckpointAtUtc { get; init; }
    public double DurationMilliseconds { get; init; }

    public int TotalReceived { get; init; }
    public int Fetched => TotalReceived;
    public int Parsed { get; init; }
    public bool ScanComplete { get; init; }
    public bool ReconciliationSafe { get; init; }
    public IReadOnlyList<string> ReconciliationBlockReasons { get; init; } = [];
    public IReadOnlyDictionary<string, int>? EligibilityReasonCounts { get; init; }
    public int IndiaEligible => (EligibilityReasonCounts?.GetValueOrDefault("IndiaLocationConfirmed") ?? 0) +
        (EligibilityReasonCounts?.GetValueOrDefault("ExplicitIndiaRemote") ?? 0);
    public int MissingLocationAccepted => EligibilityReasonCounts?.GetValueOrDefault("MissingLocationAccepted") ?? 0;
    public int NonIndiaRejected => (SelectionReasonCounts?.GetValueOrDefault("NonIndiaLocation") ?? 0) +
        (SelectionReasonCounts?.GetValueOrDefault("IndiaRemoteExcluded") ?? 0);
    public int AmbiguousLocationHeld => (SelectionReasonCounts?.GetValueOrDefault("AmbiguousLocation") ?? 0) +
        (SelectionReasonCounts?.GetValueOrDefault("ContradictoryLocation") ?? 0) + (SelectionReasonCounts?.GetValueOrDefault("UnknownCountry") ?? 0);
    public int DuplicatesPrevented => Existing;
    public int InvalidRequiredDataRejected => ValidationRejected;

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Unchanged { get; init; }

    public int Closed { get; init; }

    public int Matched { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    // Publication counters are a separate dimension: a persisted creation may also fail publication.
    public int Published { get; init; }
    public int NeedsReview { get; init; }
    public int QualityRejected { get; init; }
    public int PublishFailed { get; init; }
    public int AutoPublishDisabled { get; init; }
    public int NotPublished => NeedsReview + QualityRejected + AutoPublishDisabled + PublishFailed;
    public int SkippedInvalid => ProviderSkipped + ValidationRejected;
    public IReadOnlyDictionary<JobQualityReasonCode, int>? QualityReasonCounts { get; init; }

    /// <summary>
    /// Run-level counter grouping: every MatchedBy* outcome (URL/fingerprint/
    /// fuzzy) means the job already existed, so it counts as an existing
    /// duplicate here while the detailed Matched value is preserved for
    /// existing callers.
    /// </summary>
    public int ExistingDuplicate => Matched;
    public int Existing => Updated + Unchanged + Matched;
    public int ValidationRejected => ValidationReasonCounts?.Values.Sum() ?? 0;
    public int IngestionRejected => ValidationRejected +
        (ReasonCounts?.GetValueOrDefault(JobIngestionReasonCode.CompanyNotFound) ?? 0);
    public int ProviderSkipped => ReasonCounts?.GetValueOrDefault(JobIngestionReasonCode.ProviderError) ?? 0;
    public int SelectionSkipped => SelectionReasonCounts?.Values.Sum() ?? 0;

    /// <summary>
    /// Run-level counter grouping: rejected items are the ones skipped by
    /// validation/company resolution (Invalid + CompanyNotFound outcomes).
    /// </summary>
    public int Rejected => NonIndiaRejected + IngestionRejected;

    /// <summary>
    /// Machine-readable reason-code tallies collected across all processed
    /// items in this run (for example DuplicateCanonicalUrl = 15).
    /// </summary>
    public IReadOnlyDictionary<JobIngestionReasonCode, int>? ReasonCounts { get; init; }
    public IReadOnlyDictionary<string, int>? ValidationReasonCounts { get; init; }
    public IReadOnlyDictionary<string, int>? SelectionReasonCounts { get; init; }

    public bool Succeeded { get; init; }

    public string? Error { get; init; }
}
