using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Features.Candidates;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Candidate")]
[Route("api/candidate")]
[Produces("application/json")]
public sealed class CandidateController(ICandidateService candidates) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<CandidateProfileResponse>>> Profile(CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateProfileResponse>(
            await candidates.GetProfileAsync(User.GetRequiredUserId(), cancellationToken)));

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<CandidateProfileResponse>>> UpdateProfile(
        UpdateCandidateProfileRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateProfileResponse>(
            await candidates.UpdateProfileAsync(User.GetRequiredUserId(), request, cancellationToken)));

    [HttpPut("profile/about")]
    public async Task<ActionResult<ApiResponse<CandidateAboutResponse>>> UpdateAbout(
        UpdateCandidateAboutRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateAboutResponse>(await candidates.UpdateAboutAsync(
            User.GetRequiredUserId(), request, cancellationToken),
            "Candidate profile updated successfully."));

    [HttpGet("profile/basic-details")]
    public async Task<ActionResult<ApiResponse<CandidateBasicDetailsResponse>>> BasicDetails(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateBasicDetailsResponse>(await candidates.GetBasicDetailsAsync(
            User.GetRequiredUserId(), cancellationToken)));

    [HttpPut("profile/basic-details")]
    public async Task<ActionResult<ApiResponse<CandidateBasicDetailsResponse>>> UpdateBasicDetails(
        UpdateCandidateBasicDetailsRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateBasicDetailsResponse>(await candidates.UpdateBasicDetailsAsync(
            User.GetRequiredUserId(), request, cancellationToken), "Basic details updated successfully."));

    [HttpGet("profile/career-preferences")]
    public async Task<ActionResult<ApiResponse<CandidateCareerPreferencesResponse>>> CareerPreferences(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateCareerPreferencesResponse>(await candidates.GetCareerPreferencesAsync(
            User.GetRequiredUserId(), cancellationToken)));

    [HttpPut("profile/career-preferences")]
    public async Task<ActionResult<ApiResponse<CandidateCareerPreferencesResponse>>> UpdateCareerPreferences(
        UpdateCandidateCareerPreferencesRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateCareerPreferencesResponse>(await candidates.UpdateCareerPreferencesAsync(
            User.GetRequiredUserId(), request, cancellationToken), "Career preferences updated successfully."));

    [HttpPut("profile/photo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(1100000)]
    public async Task<ActionResult<ApiResponse<ProfilePhotoMetadata>>> UploadProfilePhoto(
        IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return Ok(new ApiResponse<ProfilePhotoMetadata>(await candidates.UploadProfilePhotoAsync(
            User.GetRequiredUserId(), new ProfilePhotoUpload(stream, file.Length, file.ContentType),
            cancellationToken), "Profile photo updated successfully."));
    }

    [HttpGet("profile/photo")]
    public async Task<IActionResult> ProfilePhoto(CancellationToken cancellationToken)
    {
        var photo = await candidates.DownloadProfilePhotoAsync(
            User.GetRequiredUserId(), cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Vary = "Authorization";
        Response.Headers.ETag = $"\"private-photo-{photo.Version}\"";
        return File(photo.Content, photo.ContentType);
    }

    [HttpDelete("profile/photo")]
    public async Task<IActionResult> DeleteProfilePhoto(CancellationToken cancellationToken)
    {
        await candidates.DeleteProfilePhotoAsync(User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }

    [HttpGet("profile/skills")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<CandidateSkillResponse>>>> Skills(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<IReadOnlyCollection<CandidateSkillResponse>>(
            await candidates.GetSkillsAsync(User.GetRequiredUserId(), cancellationToken)));

    [HttpPost("profile/skills")]
    public async Task<ActionResult<ApiResponse<CandidateSkillResponse>>> AddSkill(
        UpsertCandidateSkillRequest request, CancellationToken cancellationToken)
    {
        var result = await candidates.AddSkillAsync(
            User.GetRequiredUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<CandidateSkillResponse>(result, "Skill added successfully."));
    }

    [HttpPut("profile/skills/{skillId:guid}")]
    public async Task<ActionResult<ApiResponse<CandidateSkillResponse>>> UpdateSkill(
        Guid skillId, UpsertCandidateSkillRequest request,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateSkillResponse>(await candidates.UpdateSkillAsync(
            User.GetRequiredUserId(), skillId, request, cancellationToken),
            "Skill updated successfully."));

    [HttpDelete("profile/skills/{skillId:guid}")]
    public async Task<IActionResult> DeleteSkill(
        Guid skillId, CancellationToken cancellationToken)
    {
        await candidates.DeleteSkillAsync(
            User.GetRequiredUserId(), skillId, cancellationToken);
        return NoContent();
    }

    [HttpGet("profile/completion")]
    public async Task<ActionResult<ApiResponse<CandidateProfileCompletionResponse>>> ProfileCompletion(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateProfileCompletionResponse>(
            await candidates.GetProfileCompletionAsync(
                User.GetRequiredUserId(), cancellationToken)));

    [HttpGet("onboarding")]
    [ProducesResponseType(
        typeof(ApiResponse<CandidateOnboardingResponse>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CandidateOnboardingResponse>>> Onboarding(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateOnboardingResponse>(
            await candidates.GetOnboardingAsync(
                User.GetRequiredUserId(), cancellationToken)));

    [HttpPut("onboarding")]
    [ProducesResponseType(
        typeof(ApiResponse<CandidateOnboardingResponse>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CandidateOnboardingResponse>>> UpdateOnboarding(
        UpdateCandidateOnboardingRequest request,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateOnboardingResponse>(
            await candidates.UpdateOnboardingAsync(
                User.GetRequiredUserId(), request, cancellationToken),
            "Candidate onboarding saved successfully."));

    [HttpPut("resume")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<ResumeResponse>>> UploadResume(
        IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var result = await candidates.UploadResumeAsync(User.GetRequiredUserId(),
            new ResumeUpload(stream, file.Length, file.FileName, file.ContentType), cancellationToken);
        var message = result.ExtractionStatus == JobPortal.Domain.Enums.ResumeExtractionStatus.Failed
            ? "Your resume was saved, but recommendations are not available yet."
            : "Resume uploaded successfully.";
        return Ok(new ApiResponse<ResumeResponse>(result, message));
    }

    [HttpGet("resume/status")]
    public async Task<ActionResult<ApiResponse<ResumeStatusResponse>>> ResumeStatus(CancellationToken cancellationToken) =>
        Ok(new ApiResponse<ResumeStatusResponse>(await candidates.GetResumeStatusAsync(
            User.GetRequiredUserId(), cancellationToken)));

    [HttpGet("jobs/recommended")]
    public async Task<ActionResult<ApiResponse<RecommendedJobsResponse>>> RecommendedJobs(
        [FromQuery] CandidatePageQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<RecommendedJobsResponse>(await candidates.GetRecommendedJobsAsync(
            User.GetRequiredUserId(), query, cancellationToken)));

    [HttpGet("jobs")]
    public async Task<ActionResult<ApiResponse<CandidateBrowseJobsResponse>>> BrowseJobs(
        [FromQuery] CandidatePageQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CandidateBrowseJobsResponse>(await candidates.GetBrowseJobsAsync(
            User.GetRequiredUserId(), query, cancellationToken)));

    [HttpGet("resume")]
    public async Task<IActionResult> DownloadResume(CancellationToken cancellationToken)
    {
        var result = await candidates.DownloadResumeAsync(User.GetRequiredUserId(), cancellationToken);
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpDelete("resume")]
    public async Task<IActionResult> DeleteResume(CancellationToken cancellationToken)
    {
        await candidates.DeleteResumeAsync(User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }

    [HttpGet("saved-jobs")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CandidateSavedJobResponse>>>> SavedJobs(
        [FromQuery] CandidatePageQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<CandidateSavedJobResponse>>(
            await candidates.GetSavedJobsAsync(User.GetRequiredUserId(), query, cancellationToken)));

    [HttpGet("application-quota")]
    [ProducesResponseType(
    typeof(ApiResponse<ApplicationQuotaResponse>),
    StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ApplicationQuotaResponse>>> ApplicationQuota(
    CancellationToken cancellationToken) =>
    Ok(new ApiResponse<ApplicationQuotaResponse>(
        await candidates.GetApplicationQuotaAsync(
            User.GetRequiredUserId(),
            cancellationToken)));

    [HttpPut("saved-jobs/{jobId:guid}")]
    public async Task<IActionResult> SaveJob(Guid jobId, CancellationToken cancellationToken)
    {
        await candidates.SaveJobAsync(User.GetRequiredUserId(), jobId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("saved-jobs/{jobId:guid}")]
    public async Task<IActionResult> RemoveSavedJob(Guid jobId, CancellationToken cancellationToken)
    {
        await candidates.RemoveSavedJobAsync(User.GetRequiredUserId(), jobId, cancellationToken);
        return NoContent();
    }

    [HttpPost("jobs/{jobId:guid}/applications")]
    [ProducesResponseType(typeof(ApiResponse<JobApplicationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApplicationQuotaLimitErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<JobApplicationResponse>>> Apply(
        Guid jobId, CreateJobApplicationRequest request, CancellationToken cancellationToken)
    {
        var result = await candidates.ApplyAsync(
            User.GetRequiredUserId(), jobId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<JobApplicationResponse>(result, "Application submitted successfully."));
    }

    [HttpPost("jobs/{jobId:guid}/apply")]
    [ProducesResponseType(typeof(ApiResponse<ApplyJobResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ApplyJobResponse>>> ApplyJob(
        Guid jobId, CreateJobApplicationRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<ApplyJobResponse>(await candidates.ApplyJobAsync(
            User.GetRequiredUserId(), jobId, request, cancellationToken)));

    [HttpGet("applications")]
    public async Task<ActionResult<ApiResponse<PagedResponse<JobApplicationResponse>>>> Applications(
        [FromQuery] JobApplicationQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<JobApplicationResponse>>(
            await candidates.GetApplicationsAsync(User.GetRequiredUserId(), query, cancellationToken)));

    [HttpGet("applications/{applicationId:guid}")]
    public async Task<ActionResult<ApiResponse<JobApplicationResponse>>> Application(
        Guid applicationId, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobApplicationResponse>(
            await candidates.GetApplicationAsync(User.GetRequiredUserId(), applicationId, cancellationToken)));

    [HttpPost("applications/{applicationId:guid}/withdraw")]
    public async Task<ActionResult<ApiResponse<JobApplicationResponse>>> Withdraw(
        Guid applicationId, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobApplicationResponse>(
            await candidates.WithdrawAsync(User.GetRequiredUserId(), applicationId, cancellationToken),
            "Application withdrawn successfully."));

    [HttpGet("jobs/{jobId:guid}/recruiter-contact")]
    [ProducesResponseType(
    typeof(ApiResponse<RecruiterContactResponse>),
    StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RecruiterContactResponse>>> GetRecruiterContact(
    Guid jobId,
    CancellationToken cancellationToken) =>
    Ok(new ApiResponse<RecruiterContactResponse>(
        await candidates.GetRecruiterContactAsync(
            User.GetRequiredUserId(),
            jobId,
            cancellationToken)));
}
