namespace JobPortal.Application.Features.EmployerCandidateFinder;

public sealed class EmployerCandidateFinderQuery
{
    public string? Search { get; set; }

    public string? Location { get; set; }

    public string? Skill { get; set; }

    public decimal? MinimumExperienceYears { get; set; }

    public decimal? MaximumExperienceYears { get; set; }

    public int MinimumMatchScore { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed record EmployerCandidateFinderResponse(
    Guid JobId,
    string JobTitle,
    IReadOnlyCollection<EmployerCandidateMatchResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record EmployerCandidateMatchResponse(
    Guid CandidateUserId,
    string FullName,
    string? Headline,
    string? Location,
    decimal? YearsOfExperience,
    int MatchScore,
    int SkillScore,
    int ExperienceScore,
    int LocationScore,
    int RoleScore,
    IReadOnlyCollection<string> MatchedSkills,
    IReadOnlyCollection<string> MissingRequiredSkills,
    IReadOnlyCollection<string> Reasons);
