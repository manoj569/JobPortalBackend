namespace JobPortal.Application.Abstractions.Jobs;

public sealed record JobIngestionResult
{
    public JobIngestionOutcome Outcome { get; init; }

    public Guid? JobId { get; init; }

    public string? Message { get; init; }

    /// <summary>
    /// Machine-readable reason for the outcome. When callers do not set it,
    /// it defaults to a deterministic mapping derived from <see cref="Outcome"/>
    /// and the sanitized <see cref="Message"/>, so existing callers that never
    /// set it keep working unchanged.
    /// </summary>
    public JobIngestionReasonCode ReasonCode =>
        _reasonCode ?? JobIngestionReasonCodes.FromOutcome(Outcome, Message);

    private readonly JobIngestionReasonCode? _reasonCode;

    /// <summary>
    /// Optional explicit machine-readable reason. When not set, <see cref="ReasonCode"/>
    /// derives the value from <see cref="Outcome"/> and the sanitized <see cref="Message"/>.
    /// </summary>
    public JobIngestionReasonCode? ExplicitReasonCode
    {
        get => _reasonCode;
        init => _reasonCode = value;
    }

    public bool Created =>
        Outcome == JobIngestionOutcome.Created;

    public bool MatchedExisting =>
        Outcome is JobIngestionOutcome.MatchedByUrl
            or JobIngestionOutcome.MatchedByFingerprint
            or JobIngestionOutcome.MatchedByFuzzy;
}
