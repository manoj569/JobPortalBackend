using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.JobPromotions;

public sealed record SaveJobPromotionRequest(
    bool IsEnabled,
    DateTimeOffset? StartsAtUtc = null,
    DateTimeOffset? EndsAtUtc = null,
    int DisplayPriority = 0);

public sealed record JobPromotionQuery(int PageNumber = 1, int PageSize = 20, bool? IsEnabled = null);
public sealed record JobPromotionBannerUpload(Stream Content, long Length, string FileName, string ContentType);
public sealed record JobPromotionBanner(byte[] Content, string ContentType);
public sealed record ActivePromotedJob(
    Guid JobId, string JobSlug, string Title, string CompanyName, string? CompanyLogoUrl,
    string? BannerImageUrl, string JobCategory, string? Location, WorkplaceType WorkplaceType,
    EmploymentType EmploymentType, int? MinimumExperienceYears, int? MaximumExperienceYears,
    string? EducationRequirement, decimal? MinimumSalary, decimal? MaximumSalary,
    string? CurrencyCode, IReadOnlyCollection<string> Skills, DateTime PostedAtUtc, int DisplayPriority);
public sealed record JobPromotionAdminItem(
    Guid JobId, string JobSlug, string JobTitle, bool IsEnabled, bool IsEligible,
    string? BannerImageUrl, DateTime? StartsAtUtc, DateTime? EndsAtUtc, int DisplayPriority,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, Guid? CreatedByUserId, Guid? UpdatedByUserId);
public sealed record JobPromotionConfigurationResponse(
    Guid JobId, string JobSlug, string JobTitle, bool IsEnabled, bool IsEligible,
    string? BannerImageUrl, DateTime? StartsAtUtc, DateTime? EndsAtUtc, int DisplayPriority,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, Guid? CreatedByUserId, Guid? UpdatedByUserId);
public sealed record PromotedJobResponse(
    Guid JobId, string JobSlug, string Title, string CompanyName, string? CompanyLogoUrl,
    string? BannerImageUrl, string JobCategory, string? Location, WorkplaceType WorkplaceType,
    EmploymentType EmploymentType, int? MinimumExperienceYears, int? MaximumExperienceYears,
    string? EducationRequirement, decimal? MinimumSalary, decimal? MaximumSalary,
    string? CurrencyCode, IReadOnlyCollection<string> Skills, DateTime PostedAtUtc, string DetailsUrl);

public interface IJobPromotionRepository
{
    Task<JobPromotion?> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<JobPromotionAdminItem> Items, int Total)> SearchAsync(JobPromotionQuery query, DateTime nowUtc, CancellationToken cancellationToken);
    Task<bool> IsJobEligibleAsync(Guid jobId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ActivePromotedJob>> ActiveAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken);
    Task<JobPromotion?> GetActiveBannerAsync(Guid jobId, DateTime nowUtc, CancellationToken cancellationToken);
    Task UpsertAsync(JobPromotion promotion, Guid actorId, CancellationToken cancellationToken);
    Task<bool> JobExistsAsync(Guid jobId, CancellationToken cancellationToken);
}

public interface IJobPromotionService
{
    Task<JobPromotionConfigurationResponse> SaveAsync(Guid actorId, Guid jobId, SaveJobPromotionRequest request, CancellationToken cancellationToken);
    Task<JobPromotionConfigurationResponse> GetAsync(Guid jobId, CancellationToken cancellationToken);
    Task<PagedResponse<JobPromotionConfigurationResponse>> SearchAsync(JobPromotionQuery query, CancellationToken cancellationToken);
    Task<JobPromotionConfigurationResponse> DisableAsync(Guid actorId, Guid jobId, CancellationToken cancellationToken);
    Task<JobPromotionConfigurationResponse> UploadBannerAsync(Guid actorId, Guid jobId, JobPromotionBannerUpload upload, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PromotedJobResponse>> ActiveAsync(CancellationToken cancellationToken);
    Task<JobPromotionBanner> BannerAsync(Guid jobId, CancellationToken cancellationToken);
}
