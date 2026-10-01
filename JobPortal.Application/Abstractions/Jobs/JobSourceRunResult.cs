namespace JobPortal.Application.Abstractions.Jobs;

public sealed record JobSourceRunResult
{
    public Guid JobSourceId { get; init; }

    public int TotalReceived { get; init; }

    public int Created { get; init; }

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

    /// <summary>
    /// Run-level counter grouping: rejected items are the ones skipped by
    /// validation/company resolution (Invalid + CompanyNotFound outcomes).
    /// </summary>
    public int Rejected => Skipped;

    /// <summary>
    /// Machine-readable reason-code tallies collected across all processed
    /// items in this run (for example DuplicateCanonicalUrl = 15).
    /// </summary>
    public IReadOnlyDictionary<JobIngestionReasonCode, int>? ReasonCounts { get; init; }

    public bool Succeeded { get; init; }

    public string? Error { get; init; }
}
