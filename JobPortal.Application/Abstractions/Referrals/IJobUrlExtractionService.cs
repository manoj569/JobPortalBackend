namespace JobPortal.Application.Abstractions.Referrals;

public interface IJobUrlExtractionService
{
    /// <summary>Fetches the page at <paramref name="url"/> and asks an LLM to pull out
    /// structured job fields. Returns null values for anything it couldn't confidently find —
    /// the caller (referrer) reviews/edits before final submission, this is never auto-published.</summary>
    Task<ExtractedJobDetails> ExtractAsync(string url, CancellationToken cancellationToken = default);
}

public sealed record ExtractedJobDetails(
    string? Title,
    string? CompanyName,
    string? Location,
    string? Description,
    decimal? MinimumSalary,
    decimal? MaximumSalary,
    int? MinimumExperienceYears,
    int? MaximumExperienceYears,
    bool Succeeded,
    string? FailureReason = null);
