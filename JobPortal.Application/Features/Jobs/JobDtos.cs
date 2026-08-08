using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Jobs;

public sealed record CreateJobRequest(string Title, string Description, Guid CompanyId, Guid CategoryId, string ApplicationUrl, string? Responsibilities, string? Requirements, string? Benefits, string? Location, decimal? MinimumSalary, decimal? MaximumSalary, string CurrencyCode, EmploymentType EmploymentType, WorkplaceType WorkplaceType, ExperienceLevel ExperienceLevel, DateTime? ExpiresAtUtc, int? MinimumExperienceYears = null, int? MaximumExperienceYears = null, int? InternshipDurationMonths = null, bool IsFlexibleDuration = false, string? Department = null, string? RoleCategory = null, string? EducationRequirement = null, PostedByType? PostedByType = null);
public sealed record UpdateJobRequest(string Title, string Description, Guid CompanyId, Guid CategoryId, string ApplicationUrl, string? Responsibilities, string? Requirements, string? Benefits, string? Location, decimal? MinimumSalary, decimal? MaximumSalary, string CurrencyCode, EmploymentType EmploymentType, WorkplaceType WorkplaceType, ExperienceLevel ExperienceLevel, DateTime? ExpiresAtUtc, int? MinimumExperienceYears = null, int? MaximumExperienceYears = null, int? InternshipDurationMonths = null, bool IsFlexibleDuration = false, string? Department = null, string? RoleCategory = null, string? EducationRequirement = null, PostedByType? PostedByType = null);
public sealed record ComposeJobDraftRequest(string Title, string? Description = null, string? ApplicationUrl = null,
    EmploymentType? EmploymentType = null, WorkplaceType? WorkplaceType = null,
    ExperienceLevel? ExperienceLevel = null, string? Location = null, decimal? MinimumSalary = null,
    decimal? MaximumSalary = null, string? CurrencyCode = null, DateTime? ExpiresAtUtc = null,
    string? Responsibilities = null, string? Requirements = null, string? Benefits = null);
public sealed record ComposeRelationRequest<T>(Guid? ExistingId = null, T? New = default);
public sealed record ComposeJobRequest(ComposeJobDraftRequest Job,
    ComposeRelationRequest<CreateInlineCompanyRequest>? Company = null,
    ComposeRelationRequest<CreateInlineCategoryRequest>? Category = null,
    ComposeRecruiterContactRequest? RecruiterContact = null);
public sealed record ComposeRecruiterContactRequest(string? Name = null, string? Role = null,
    string? Email = null, string? PhoneNumber = null, bool SharingApproved = false);
public sealed record CreateInlineCompanyRequest(string Name, string? Slug = null, string? Description = null,
    string? WebsiteUrl = null, string? LogoUrl = null, string? Industry = null, string? Location = null,
    int? EmployeeCount = null, bool IsVerified = false);
public sealed record CreateInlineCategoryRequest(string Name, string? Slug = null, string? Description = null,
    int DisplayOrder = 0, Guid? ParentCategoryId = null);
public sealed record ComposedRelationResponse(Guid Id, string Name, bool Created);
public sealed record ComposeJobResponse(Guid Id, string Slug, JobStatus Status,
    ComposedRelationResponse? Company, ComposedRelationResponse? Category,
    bool RecruiterContactCreated = false);
public sealed record JobSearchQuery(int PageNumber = 1, int PageSize = 20, string? Search = null, Guid? CompanyId = null, Guid? CategoryId = null, JobStatus? Status = null, EmploymentType? EmploymentType = null, WorkplaceType? WorkplaceType = null, ExperienceLevel? ExperienceLevel = null, bool? IsFeatured = null, bool? IsHidden = null, bool? IsDeleted = false, DateTime? PublishedFromUtc = null, DateTime? PublishedToUtc = null, string SortBy = "createdAt", string SortDirection = "desc", DateTime? ExpiresFromUtc = null, DateTime? ExpiresToUtc = null);
public sealed record JobResponse(Guid Id, string ReferenceNumber, string Title, string Slug, string Description, string? Responsibilities, string? Requirements, string? Benefits, string ApplicationUrl, string CompanyName, Guid CompanyId, string CategoryName, Guid CategoryId, string? Location, decimal? MinimumSalary, decimal? MaximumSalary, string CurrencyCode, EmploymentType EmploymentType, WorkplaceType WorkplaceType, ExperienceLevel ExperienceLevel, JobStatus Status, bool IsFeatured, bool IsHidden, DateTime? PublishedAtUtc, DateTime? ExpiresAtUtc, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, bool IsDeleted, DateTime? DeletedAtUtc, int? MinimumExperienceYears, int? MaximumExperienceYears, int? InternshipDurationMonths, bool IsFlexibleDuration, string? Department, string? RoleCategory, string? EducationRequirement, PostedByType? PostedByType);
public sealed record UpdateRecruiterContactRequest(
    string ContactName,
    string ContactRole,
    string Email,
    string? PhoneNumber,
    bool IsSharingApproved);

public sealed record AdminRecruiterContactResponse(
    Guid JobId,
    string ContactName,
    string ContactRole,
    string Email,
    string? PhoneNumber,
    bool IsSharingApproved);
