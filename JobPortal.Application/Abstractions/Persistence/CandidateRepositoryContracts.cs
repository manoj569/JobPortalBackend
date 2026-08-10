using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.Persistence;

public interface ICandidateRepository
{
    Task<User?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateResumeProfile?> GetResumeProfileAsync(Guid userId, bool tracking, CancellationToken cancellationToken = default);
    Task AddResumeProfileAsync(CandidateResumeProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RecommendationJobCandidate>> GetRecommendationCandidatesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<RecommendationJobCandidate> Items, int TotalCount)> GetCandidateBrowseJobsAsync(Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default);
    Task<CandidateJob?> GetAvailableJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<CandidateRecruiterContact?> GetApprovedRecruiterContactForAvailableJobAsync(
    Guid jobId,
    CancellationToken cancellationToken = default);
    Task<bool> HasActiveMembershipAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> IsResumeReferencedAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<JobApplication?> GetApplicationAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);
    Task<bool> HasApplicationAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default);
    Task<JobApplication?> GetApplicationByJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default);
    Task<ApplicationQuotaUsage?> GetQuotaUsageAsync(
    Guid userId,
    ApplicationQuotaPeriod period,
    DateTime periodStartsAtUtc,
    CancellationToken cancellationToken = default);

    Task AddQuotaUsageAsync(
        ApplicationQuotaUsage quotaUsage,
        CancellationToken cancellationToken = default);
    Task AddApplicationAsync(JobApplication application, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<JobApplicationResponse> Items, int TotalCount)> GetApplicationsAsync(
        Guid userId, JobApplicationQuery query, CancellationToken cancellationToken = default);
}

public sealed record CandidateJob(Guid Id, string Title, string Slug, string CompanyName, string? ApplicationUrl = null);
public sealed record RecommendationJobCandidate(Guid Id, string ReferenceNumber, string Title, string Slug,
    string Description, string? Requirements, string? Responsibilities, Guid CompanyId,
    string CompanyName, string CompanySlug, string? CompanyLogoUrl, string? CompanyIndustry,
    Guid CategoryId, string CategoryName, string? Location, EmploymentType EmploymentType,
    WorkplaceType WorkplaceType, ExperienceLevel ExperienceLevel, bool IsFeatured,
    DateTime PublishedAtUtc, DateTime? ExpiresAtUtc);
public sealed record CandidateRecruiterContact(
    Guid JobId,
    string JobTitle,
    string JobSlug,
    string CompanyName,
    string ContactName,
    string ContactRole,
    string Email,
    string? PhoneNumber);
