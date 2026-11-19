namespace JobPortal.Application.Features.EmployerTalentPool;

public sealed class EmployerTalentPoolQuery
{
    public string? Search { get; set; }

    public string? Skill { get; set; }

    public string? Location { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed class AddEmployerTalentPoolCandidateRequest
{
    public Guid CandidateUserId { get; set; }

    public string? Notes { get; set; }
}

public sealed class UpdateEmployerTalentPoolNotesRequest
{
    public string? Notes { get; set; }
}

public sealed record EmployerTalentPoolItemResponse(
    Guid CandidateUserId,
    string FullName,
    string? Headline,
    string? Location,
    decimal? YearsOfExperience,
    string? ProfileImageUrl,
    string? Notes,
    DateTime AddedAtUtc,
    IReadOnlyCollection<string> Skills);

public sealed record EmployerTalentPoolResponse(
    IReadOnlyCollection<EmployerTalentPoolItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
