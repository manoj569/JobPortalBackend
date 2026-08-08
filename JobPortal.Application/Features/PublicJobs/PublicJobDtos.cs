using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.PublicJobs;

public sealed record PublicJobQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? CompanyId = null,
    Guid? CategoryId = null,
    EmploymentType? EmploymentType = null,
    WorkplaceType? WorkplaceType = null,
    ExperienceLevel? ExperienceLevel = null,
    string? Location = null,
    decimal? MinimumSalary = null,
    decimal? MaximumSalary = null,
    bool? IsFeatured = null,
    PublicJobSort SortBy = PublicJobSort.Recommended,
    string SortDirection = "desc",
    string? Keyword = null,
    string[]? Locations = null,
    WorkplaceType[]? WorkModes = null,
    int? MinExperienceYears = null,
    int? MaxExperienceYears = null,
    string[]? Departments = null,
    string[]? RoleCategories = null,
    EmploymentType[]? EmploymentTypes = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    int[]? InternshipDurationMonths = null,
    bool? FlexibleDuration = null,
    string[]? EducationRequirements = null,
    CompanyType[]? CompanyTypes = null,
    Guid[]? CompanyIds = null,
    string[]? Industries = null,
    PostedByType[]? PostedByTypes = null,
    int? FreshnessDays = null,
    bool? FeaturedOnly = null,
    int? Page = null,
    string? CompanyName = null,
    string? CategoryName = null)
{
    public int EffectivePageNumber => Page ?? PageNumber;
}

public sealed record PublicJobSummary(
    Guid Id, string ReferenceNumber, string Title, string Slug,
    Guid CompanyId, string CompanyName, string CompanySlug, string? CompanyLogoUrl,
    Guid CategoryId, string CategoryName, string? Location,
    decimal? MinimumSalary, decimal? MaximumSalary, string CurrencyCode,
    EmploymentType EmploymentType, WorkplaceType WorkplaceType,
    ExperienceLevel ExperienceLevel, bool IsFeatured,
    DateTime PublishedAtUtc, DateTime? ExpiresAtUtc,
    int? MinimumExperienceYears, int? MaximumExperienceYears,
    int? InternshipDurationMonths, bool IsFlexibleDuration,
    string? Department, string? RoleCategory, string? EducationRequirement,
    PostedByType? PostedByType, CompanyType? CompanyType, string? Industry);

public sealed record PublicJobDetails(
    Guid Id, string ReferenceNumber, string Title, string Slug, string Description,
    string? Responsibilities, string? Requirements, string? Benefits,
    Guid CompanyId, string CompanyName, string CompanySlug, string? CompanyLogoUrl,
    string? CompanyDescription, string? CompanyWebsiteUrl,
    Guid CategoryId, string CategoryName, string? Location,
    decimal? MinimumSalary, decimal? MaximumSalary, string CurrencyCode,
    EmploymentType EmploymentType, WorkplaceType WorkplaceType,
    ExperienceLevel ExperienceLevel, bool IsFeatured,
    DateTime PublishedAtUtc, DateTime? ExpiresAtUtc,
    int? MinimumExperienceYears, int? MaximumExperienceYears,
    int? InternshipDurationMonths, bool IsFlexibleDuration,
    string? Department, string? RoleCategory, string? EducationRequirement,
    PostedByType? PostedByType, CompanyType? CompanyType, string? Industry,
    string? ApplicationUrl = null);

public sealed record PopularCompanyResponse(
    Guid Id, string Name, string Slug, string? LogoUrl, string? Industry,
    string? Location, bool IsVerified, int ActiveJobCount);

public sealed record PublicJobPage(PagedResponse<PublicJobSummary> Page);

public sealed record PublicJobSearchResponse(
    IReadOnlyCollection<PublicJobSummary> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    PublicJobSort AppliedSort)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record StringFacetOption(string Value, int Count);
public sealed record EnumFacetOption<TEnum>(TEnum Value, int Count) where TEnum : struct, Enum;
public sealed record CompanyFacetOption(Guid Id, string Name, int Count);
public sealed record InternshipDurationFacetOption(
    int? Months, bool IsFlexible, string Label, int Count);
public sealed record DecimalRangeFacet(decimal? Minimum, decimal? Maximum, int Count);
public sealed record IntegerRangeFacet(int? Minimum, int? Maximum, int Count);

public sealed record PublicJobFilterOptionsResponse(
    IReadOnlyCollection<StringFacetOption> Locations,
    IReadOnlyCollection<EnumFacetOption<WorkplaceType>> WorkModes,
    IReadOnlyCollection<StringFacetOption> Departments,
    IReadOnlyCollection<StringFacetOption> RoleCategories,
    IReadOnlyCollection<EnumFacetOption<EmploymentType>> EmploymentTypes,
    IReadOnlyCollection<EnumFacetOption<CompanyType>> CompanyTypes,
    IReadOnlyCollection<CompanyFacetOption> Companies,
    IReadOnlyCollection<StringFacetOption> Industries,
    IReadOnlyCollection<StringFacetOption> EducationRequirements,
    IReadOnlyCollection<EnumFacetOption<PostedByType>> PostedByTypes,
    IReadOnlyCollection<InternshipDurationFacetOption> InternshipDurations,
    DecimalRangeFacet SalaryRange,
    IntegerRangeFacet ExperienceRange);
