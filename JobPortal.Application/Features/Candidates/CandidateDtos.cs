using System.Text.Json.Serialization;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Candidates;

public sealed record CandidateProfileResponse(
    Guid Id, string Email, string FirstName, string LastName, string? Headline, string? Bio,
    string? Location, IReadOnlyCollection<string> Skills, IReadOnlyCollection<string> Education,
    IReadOnlyCollection<string> Experience, string? LinkedInUrl, string? PortfolioUrl,
    IReadOnlyCollection<EmploymentType> PreferredJobTypes, ResumeResponse? Resume);
public sealed record UpdateCandidateProfileRequest(
    string? Headline, string? Bio, string? Location, IReadOnlyCollection<string> Skills,
    IReadOnlyCollection<string> Education, IReadOnlyCollection<string> Experience,
    string? LinkedInUrl, string? PortfolioUrl, IReadOnlyCollection<EmploymentType> PreferredJobTypes);

public sealed record CandidateAboutResponse(string? ResumeHeadline, string? ProfileSummary);
public sealed record UpdateCandidateAboutRequest(string? ResumeHeadline, string? ProfileSummary);
public sealed record CandidateSkillResponse(
    Guid Id, string Name, CandidateSkillProficiency? Proficiency,
    decimal? YearsOfExperience, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);
public sealed record UpsertCandidateSkillRequest(
    string Name, CandidateSkillProficiency? Proficiency = null,
    decimal? YearsOfExperience = null);
public sealed record ProfileSectionCompletionResponse(
    string Section, int Weight, bool IsCompleted);
public sealed record CandidateProfileCompletionResponse(
    int CompletionPercentage,
    IReadOnlyCollection<string> CompletedSections,
    IReadOnlyCollection<string> MissingSections,
    IReadOnlyCollection<ProfileSectionCompletionResponse> Sections);

public sealed record CandidateOnboardingResponse(
    CareerStage? CareerStage,
    IReadOnlyCollection<DesiredOpportunity> DesiredOpportunities,
    string? City,
    IReadOnlyCollection<string> Skills,
    IReadOnlyCollection<WorkPreference> WorkPreferences,
    string? College,
    string? Degree,
    int? GraduationYear,
    decimal? YearsOfExperience,
    DateTime? CompletedAtUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCandidateOnboardingRequest(
    CareerStage CareerStage,
    IReadOnlyCollection<DesiredOpportunity> DesiredOpportunities,
    string City,
    IReadOnlyCollection<string> Skills,
    IReadOnlyCollection<WorkPreference> WorkPreferences,
    string? College,
    string? Degree,
    int? GraduationYear,
    decimal? YearsOfExperience);
public sealed record ResumeUpload(Stream Content, long Length, string FileName, string ContentType);
public sealed record ResumeResponse(string FileName, string ContentType, long SizeBytes, DateTime UploadedAtUtc,
    ResumeExtractionStatus ExtractionStatus = ResumeExtractionStatus.NotStarted);
public sealed record ResumeDownload(Stream Content, string FileName, string ContentType);
public sealed record ResumeStatusResponse(bool HasResume, ResumeExtractionStatus ExtractionStatus,
    DateTime? ExtractedAtUtc, string Message);
public sealed record RecommendedJobResponse(Guid Id, string ReferenceNumber, string Title, string Slug,
    Guid CompanyId, string CompanyName, string CompanySlug, string? CompanyLogoUrl,
    Guid CategoryId, string CategoryName, string? Location, EmploymentType EmploymentType,
    WorkplaceType WorkplaceType, ExperienceLevel ExperienceLevel, bool IsFeatured,
    DateTime PublishedAtUtc, DateTime? ExpiresAtUtc, int MatchScore,
    IReadOnlyCollection<string> MatchReasons);
public sealed record RecommendedJobsResponse(ResumeExtractionStatus ExtractionStatus, string Message,
    IReadOnlyCollection<RecommendedJobResponse> Items, int PageNumber, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
public sealed record CandidateBrowseJobResponse(Guid Id, string ReferenceNumber, string Title, string Slug,
    Guid CompanyId, string CompanyName, string CompanySlug, string? CompanyLogoUrl,
    Guid CategoryId, string CategoryName, string? Location, EmploymentType EmploymentType,
    WorkplaceType WorkMode, ExperienceLevel ExperienceLevel, bool IsFeatured,
    DateTime PublishedAtUtc, DateTime? ClosingDate);
public sealed record CandidateBrowseJobsResponse(IReadOnlyCollection<CandidateBrowseJobResponse> Items,
    int PageNumber, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
public sealed record CandidatePageQuery(int PageNumber = 1, int PageSize = 20);
public sealed record CandidateSavedJobResponse(Guid SavedJobId, DateTime SavedAtUtc, Guid JobId, string Title, string Slug, string CompanyName);
public sealed record CreateJobApplicationRequest(string? CoverLetter = null,
    ApplicationMethod ApplicationMethod = ApplicationMethod.Portal);
public sealed record ApplyJobResponse(Guid ApplicationId, Guid JobId,
    JobApplicationStatus ApplicationStatus, ApplicationMethod ApplicationMethod, DateTime AppliedAtUtc);
public sealed record JobApplicationQuery(
    int PageNumber = 1, int PageSize = 20, JobApplicationStatus? Status = null);
public sealed record JobApplicationResponse(
    Guid Id, Guid JobId, string JobTitle, string JobSlug, string CompanyName,
    JobApplicationStatus Status, string? CoverLetter, string? ResumeFileName,
    DateTime SubmittedAtUtc, DateTime? WithdrawnAtUtc,
    ApplicationMethod ApplicationMethod = ApplicationMethod.Portal,
    string? CategoryName = null, string? Location = null,
    EmploymentType? EmploymentType = null, WorkplaceType? WorkMode = null,
    ExperienceLevel? ExperienceLevel = null, string? ApplicationUrl = null,
    DateTime? ClosingDate = null);
public sealed record RecruiterContactResponse(
    Guid JobId,
    string JobTitle,
    string JobSlug,
    string CompanyName,
    string ContactName,
    string ContactRole,
    string Email,
    string? PhoneNumber);
public sealed record ApplicationQuotaResponse(
    string Plan,
    bool IsPremium,
    int Limit,
    int UsedApplications,
    int RemainingApplications,
    DateTime ResetsAtUtc);

public sealed record ApplicationQuotaLimitErrorResponse(
    bool Success,
    string Code,
    string Message,
    bool RedirectToMembership);
