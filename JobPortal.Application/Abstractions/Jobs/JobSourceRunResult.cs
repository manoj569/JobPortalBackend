namespace JobPortal.Application.Abstractions.Jobs;

public sealed record JobSourceRunResult
{
    public Guid JobSourceId { get; init; }

    public int TotalReceived { get; init; }

    public int Created { get; init; }

    public int Matched { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    public bool Succeeded { get; init; }

    public string? Error { get; init; }
}
