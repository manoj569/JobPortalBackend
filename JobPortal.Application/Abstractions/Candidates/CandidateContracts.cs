using JobPortal.Application.Features.Candidates;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Candidates;

public interface ICandidateService
{
    Task<CandidateProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateProfileResponse> UpdateProfileAsync(Guid userId, UpdateCandidateProfileRequest request, CancellationToken cancellationToken = default);
    Task<CandidateAboutResponse> UpdateAboutAsync(Guid userId, UpdateCandidateAboutRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CandidateSkillResponse>> GetSkillsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateSkillResponse> AddSkillAsync(Guid userId, UpsertCandidateSkillRequest request, CancellationToken cancellationToken = default);
    Task<CandidateSkillResponse> UpdateSkillAsync(Guid userId, Guid skillId, UpsertCandidateSkillRequest request, CancellationToken cancellationToken = default);
    Task DeleteSkillAsync(Guid userId, Guid skillId, CancellationToken cancellationToken = default);
    Task<CandidateProfileCompletionResponse> GetProfileCompletionAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateBasicDetailsResponse> GetBasicDetailsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateBasicDetailsResponse> UpdateBasicDetailsAsync(Guid userId, UpdateCandidateBasicDetailsRequest request, CancellationToken cancellationToken = default);
    Task<CandidateCareerPreferencesResponse> GetCareerPreferencesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateCareerPreferencesResponse> UpdateCareerPreferencesAsync(Guid userId, UpdateCandidateCareerPreferencesRequest request, CancellationToken cancellationToken = default);
    Task<ProfilePhotoMetadata> UploadProfilePhotoAsync(Guid userId, ProfilePhotoUpload upload, CancellationToken cancellationToken = default);
    Task<ProfilePhotoDownload> DownloadProfilePhotoAsync(Guid userId, CancellationToken cancellationToken = default);
    Task DeleteProfilePhotoAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateOnboardingResponse> GetOnboardingAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CandidateOnboardingResponse> UpdateOnboardingAsync(Guid userId, UpdateCandidateOnboardingRequest request, CancellationToken cancellationToken = default);
    Task<ResumeResponse> UploadResumeAsync(Guid userId, ResumeUpload upload, CancellationToken cancellationToken = default);
    Task<ResumeDownload> DownloadResumeAsync(Guid userId, CancellationToken cancellationToken = default);
    Task DeleteResumeAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ResumeStatusResponse> GetResumeStatusAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<RecommendedJobsResponse> GetRecommendedJobsAsync(Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default);
    Task<CandidateBrowseJobsResponse> GetBrowseJobsAsync(Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default);
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
    Task<ApplyJobResponse> ApplyJobAsync(Guid userId, Guid jobId, CreateJobApplicationRequest request, CancellationToken cancellationToken = default);
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

public sealed record StoredProfilePhoto(byte[] Content, string ContentType, int SizeBytes, Guid Version);

public interface IProfilePhotoStorage
{
    Task<StoredProfilePhoto?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Guid> StoreAsync(Guid userId, byte[] content, string contentType, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
