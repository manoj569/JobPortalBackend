using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.Jobs;

public sealed record RawExternalJob
{
    public string Title { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public Guid? CompanyId { get; init; }
    public string? Location { get; init; }
    // Provider facts used for eligibility; not inferred from "remote" alone.
    public IReadOnlyCollection<string> AdditionalLocations { get; init; } = [];
    public IReadOnlyCollection<string> CountryCodes { get; init; } = [];
    public string? Description { get; init; }
    public string? Requirements { get; init; }
    public string? Responsibilities { get; init; }
    public string? Benefits { get; init; }
    public string? ApplicationUrl { get; init; }
    public string? ExternalId { get; init; }
    // Set by JobSourceRunner only for providers that return a complete snapshot.
    public Guid? JobSourceId { get; init; }
    public DateTime? SourcePostedAtUtc { get; init; }
    // Source-provided deadline only; providers must supply an explicit UTC instant.
    public DateTime? ExpiresAtUtc { get; init; }
    public EmploymentType? EmploymentType { get; init; }
    public WorkplaceType? WorkplaceType { get; init; }
    public string? EmploymentTypeText { get; init; }
    public string? WorkplaceTypeText { get; init; }

    public ExperienceLevel? ExperienceLevel { get; init; }
    public int? MinimumExperienceYears { get; init; }
    public int? MaximumExperienceYears { get; init; }
    public string? EducationRequirement { get; init; }
    public string? ExternalCategory { get; init; }
    public bool DescriptionIsHtml { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }

    public Guid? CategoryId { get; init; }
}
