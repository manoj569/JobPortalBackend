namespace JobPortal.Application.Abstractions.Jobs;

public sealed record JobSourceRunResult
{
    public Guid JobSourceId { get; init; }

    public int TotalReceived { get; init; }
    public int Fetched => TotalReceived;
    public int Parsed { get; init; }
    public bool ScanComplete { get; init; }
    public bool ReconciliationSafe { get; init; }
    public IReadOnlyDictionary<string, int>? EligibilityReasonCounts { get; init; }
    public int IndiaEligible => (EligibilityReasonCounts?.GetValueOrDefault("IndiaLocationConfirmed") ?? 0) +
        (EligibilityReasonCounts?.GetValueOrDefault("ExplicitIndiaRemote") ?? 0);
    public int MissingLocationAccepted => EligibilityReasonCounts?.GetValueOrDefault("MissingLocationAccepted") ?? 0;
    public int NonIndiaRejected => (EligibilityReasonCounts?.GetValueOrDefault("NonIndiaLocation") ?? 0) +
        (EligibilityReasonCounts?.GetValueOrDefault("IndiaRemoteExcluded") ?? 0);
    public int AmbiguousLocationHeld => (EligibilityReasonCounts?.GetValueOrDefault("AmbiguousLocation") ?? 0) +
        (EligibilityReasonCounts?.GetValueOrDefault("ContradictoryLocation") ?? 0) + (EligibilityReasonCounts?.GetValueOrDefault("UnknownCountry") ?? 0);
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
