using System.Text.Json.Serialization;
using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Portfolios;

public sealed record CreatePortfolioRequest(string? RequestedSlug, CandidatePortfolioTemplate Template);
public sealed record PortfolioSectionSettingRequest(PortfolioSectionType SectionType, bool IsVisible, int DisplayOrder);
public sealed record UpdatePortfolioSettingsRequest(
    string Slug, CandidatePortfolioTemplate Template,
    IReadOnlyCollection<PortfolioSectionSettingRequest> Sections);

public sealed record ExperienceRequest(string JobTitle, string CompanyName, string? Location,
    EmploymentType? EmploymentType, DateOnly StartDate, DateOnly? EndDate, bool IsCurrent,
    string? Description, int DisplayOrder, decimal? AnnualSalary = null,
    IReadOnlyCollection<string>? SkillsUsed = null, CandidateAvailability? NoticePeriod = null);
public sealed record EducationRequest(string Qualification, string Institution, string? FieldOfStudy,
    int? StartYear, int? EndYear, string? Grade, string? Description, int DisplayOrder,
    EducationCourseType? CourseType = null, bool IsCurrentlyStudying = false,
    string? GradingSystem = null);
public sealed record ProjectRequest(string Name, string? Role, string Description,
    IReadOnlyCollection<string> Technologies, string? SourceUrl, string? LiveUrl,
    DateOnly? StartDate, DateOnly? EndDate, int DisplayOrder);
public sealed record CertificationRequest(string Name, string? Issuer, DateOnly? IssuedDate,
    DateOnly? ExpiryDate, bool DoesNotExpire, string? CredentialId, string? CredentialUrl, int DisplayOrder);
public sealed record ProfessionalLinkRequest(ProfessionalLinkType Type, string? Label, string Url, int DisplayOrder);
public sealed record CustomSectionRequest(string Title, int DisplayOrder);
public sealed record CustomItemRequest(string Title, string? Description, DateOnly? Date, string? Url, int DisplayOrder);

public sealed record PortfolioSectionSettingResponse(Guid Id, PortfolioSectionType SectionType,
    bool IsVisible, int DisplayOrder);
public sealed record ExperienceResponse(Guid Id, string JobTitle, string CompanyName, string? Location,
    EmploymentType? EmploymentType, DateOnly StartDate, DateOnly? EndDate, bool IsCurrent,
    string? Description, int DisplayOrder, decimal? AnnualSalary = null,
    IReadOnlyCollection<string>? SkillsUsed = null, CandidateAvailability? NoticePeriod = null);
public sealed record EducationResponse(Guid Id, string Qualification, string Institution,
    string? FieldOfStudy, int? StartYear, int? EndYear, string? Grade, string? Description, int DisplayOrder,
    EducationCourseType? CourseType = null, bool IsCurrentlyStudying = false,
    string? GradingSystem = null);
public sealed record PublicExperienceResponse(Guid Id, string JobTitle, string CompanyName, string? Location,
    EmploymentType? EmploymentType, DateOnly StartDate, DateOnly? EndDate, bool IsCurrent,
    string? Description, int DisplayOrder, IReadOnlyCollection<string> SkillsUsed);
public sealed record PublicEducationResponse(Guid Id, string Qualification, string Institution,
    string? FieldOfStudy, int? StartYear, int? EndYear, string? Grade, string? Description,
    int DisplayOrder, EducationCourseType? CourseType, bool IsCurrentlyStudying, string? GradingSystem);
public sealed record ProjectResponse(Guid Id, string Name, string? Role, string Description,
    IReadOnlyCollection<string> Technologies, string? SourceUrl, string? LiveUrl,
    DateOnly? StartDate, DateOnly? EndDate, int DisplayOrder);
public sealed record CertificationResponse(Guid Id, string Name, string? Issuer, DateOnly? IssuedDate,
    DateOnly? ExpiryDate, bool DoesNotExpire, string? CredentialId, string? CredentialUrl, int DisplayOrder);
public sealed record ProfessionalLinkResponse(Guid Id, ProfessionalLinkType Type,
    string? Label, string Url, int DisplayOrder);
public sealed record CustomItemResponse(Guid Id, string Title, string? Description,
    DateOnly? Date, string? Url, int DisplayOrder);
public sealed record CustomSectionResponse(Guid Id, string Title, int DisplayOrder,
    IReadOnlyCollection<CustomItemResponse> Items);

public sealed record PortfolioEditorResponse(
    bool IsCreated, Guid? Id, string? Slug, CandidatePortfolioStatus? Status,
    CandidatePortfolioTemplate? Template, DateTime? PublishedAtUtc,
    string DisplayName, string? ResumeHeadline, string? ProfileSummary,
    IReadOnlyCollection<string> Skills, bool ResumeAvailable,
    IReadOnlyCollection<PortfolioSectionSettingResponse> SectionSettings,
    IReadOnlyCollection<ExperienceResponse> Experiences,
    IReadOnlyCollection<EducationResponse> Education,
    IReadOnlyCollection<ProjectResponse> Projects,
    IReadOnlyCollection<CertificationResponse> Certifications,
    IReadOnlyCollection<ProfessionalLinkResponse> ProfessionalLinks,
    IReadOnlyCollection<CustomSectionResponse> CustomSections,
    CandidateAvailability? NoticePeriod = null,
    CandidateProfileCompletionResponse? ProfileCompletion = null);

public sealed record PortfolioPublishResponse(
    bool Published, IReadOnlyCollection<string> MissingRequirements,
    PortfolioEditorResponse Portfolio);

public sealed record PublicPortfolioCandidateResponse(
    string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResumeHeadline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProfileSummary);
public sealed record PublicPortfolioSectionsResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<string>? Skills,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<PublicExperienceResponse>? Experience,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<PublicEducationResponse>? Education,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<ProjectResponse>? Projects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<CertificationResponse>? Certifications,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<ProfessionalLinkResponse>? ProfessionalLinks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<CustomSectionResponse>? CustomSections,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ResumeAvailable);
public sealed record PublicPortfolioResponse(
    string Slug, CandidatePortfolioTemplate Template, DateTime? PublishedAtUtc,
    PublicPortfolioCandidateResponse Candidate,
    IReadOnlyCollection<PortfolioSectionType> SectionOrder,
    PublicPortfolioSectionsResponse Sections);
