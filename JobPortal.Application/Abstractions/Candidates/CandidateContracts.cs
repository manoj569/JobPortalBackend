using JobPortal.Application.Features.Candidates;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Candidates;

public interface ICandidateService
{
    Task<CandidateProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateProfileResponse> UpdateProfileAsync(Guid userId, UpdateCandidateProfileRequest request, CancellationToken cancellationToken = default);
    Task<CandidateOnboardingResponse> GetOnboardingAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateOnboardingResponse> UpdateOnboardingAsync(Guid userId, UpdateCandidateOnboardingRequest request, CancellationToken cancellationToken = default);
    Task<ResumeResponse> UploadResumeAsync(Guid userId, ResumeUpload upload, CancellationToken cancellationToken = default);
    Task<ResumeDownload> DownloadResumeAsync(Guid userId, CancellationToken cancellationToken = default);
    Task DeleteResumeAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ResumeStatusResponse> GetResumeStatusAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<RecommendedJobsResponse> GetRecommendedJobsAsync(Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default);
    Task<RecruiterContactResponse> GetRecruiterContactAsync(
    Guid userId,
    Guid jobId,
    CancellationToken cancellationToken = default);
    Task<PagedResponse<CandidateSavedJobResponse>> GetSavedJobsAsync(Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default);
    Task<ApplicationQuotaResponse> GetApplicationQuotaAsync(
    Guid userId,
    CancellationToken cancellationToken = default);
    Task SaveJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default);
    Task RemoveSavedJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default);
    Task<JobApplicationResponse> ApplyAsync(Guid userId, Guid jobId, CreateJobApplicationRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<JobApplicationResponse>> GetApplicationsAsync(Guid userId, JobApplicationQuery query, CancellationToken cancellationToken = default);
    Task<JobApplicationResponse> GetApplicationAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);
    Task<JobApplicationResponse> WithdrawAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);
}

public interface IResumeTextExtractor
{
    Task<string> ExtractAsync(Stream content, string extension, CancellationToken cancellationToken = default);
}

public interface IResumeStorage
{
    Task<string> StoreAsync(Stream content, string extension, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
