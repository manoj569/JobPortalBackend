namespace JobPortal.Application.Abstractions.Jobs;

public sealed record JobIngestionResult
{
    public JobIngestionOutcome Outcome { get; init; }

    public Guid? JobId { get; init; }

    public string? Message { get; init; }

    public bool Created =>
        Outcome == JobIngestionOutcome.Created;

    public bool MatchedExisting =>
        Outcome is JobIngestionOutcome.MatchedByUrl
            or JobIngestionOutcome.MatchedByFingerprint
            or JobIngestionOutcome.MatchedByFuzzy;
}
