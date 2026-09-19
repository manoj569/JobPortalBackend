using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.Jobs;

public sealed record RawExternalJob
{
    public string Title { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string? Location { get; init; }
    public string? Description { get; init; }
    public string? Requirements { get; init; }
    public string? Responsibilities { get; init; }
    public string? Benefits { get; init; }
    public string? ApplicationUrl { get; init; }
    public string? ExternalId { get; init; }
    public EmploymentType? EmploymentType { get; init; }
    public WorkplaceType? WorkplaceType { get; init; }
    public string? EmploymentTypeText { get; init; }
    public string? WorkplaceTypeText { get; init; }
    public string? ExternalCategory { get; init; }
    public bool DescriptionIsHtml { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }

    public Guid? CategoryId { get; init; }
}
