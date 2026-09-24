using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/career-guidance/consultants")]
public sealed class CareerGuidanceDiscoveryController(ICareerGuidanceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<ConsultantPublicResponse>>>> Search([FromQuery] ConsultantSearchQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<ConsultantPublicResponse>>(await service.SearchAsync(query, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ConsultantPublicResponse>>> Get(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<ConsultantPublicResponse>(await service.GetAsync(id, ct)));
}

[ApiController]
[Authorize]
[Route("api/career-guidance/me")]
public sealed class CareerGuidanceOwnerController(ICareerGuidanceService service) : ControllerBase
{
    [HttpPost("application")]
    public async Task<ActionResult<ApiResponse<ConsultantPrivateResponse>>> Apply(ConsultantProfileRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new ApiResponse<ConsultantPrivateResponse>(await service.ApplyAsync(User.GetRequiredUserId(), request, ct)));
    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<ConsultantPrivateResponse>>> Get(CancellationToken ct) =>
        Ok(new ApiResponse<ConsultantPrivateResponse>(await service.MineAsync(User.GetRequiredUserId(), ct)));
    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<ConsultantPrivateResponse>>> Update(ConsultantProfileRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<ConsultantPrivateResponse>(await service.UpdateAsync(User.GetRequiredUserId(), request, ct)));
    [HttpPost("services")]
    public async Task<ActionResult<ApiResponse<ConsultantPrivateResponse>>> AddService(ConsultantServiceRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new ApiResponse<ConsultantPrivateResponse>(await service.SaveServiceAsync(User.GetRequiredUserId(), null, request, ct)));
    [HttpPut("services/{id:guid}")]
    public async Task<ActionResult<ApiResponse<ConsultantPrivateResponse>>> UpdateService(Guid id, ConsultantServiceRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<ConsultantPrivateResponse>(await service.SaveServiceAsync(User.GetRequiredUserId(), id, request, ct)));
    [HttpDelete("services/{id:guid}")]
    public async Task<ActionResult<ApiResponse<ConsultantPrivateResponse>>> DeleteService(Guid id, [FromQuery] Guid revision, CancellationToken ct) =>
        Ok(new ApiResponse<ConsultantPrivateResponse>(await service.DeleteServiceAsync(User.GetRequiredUserId(), id, revision, ct)));
    [HttpPost("onboarding")]
    public async Task<ActionResult<ApiResponse<OnboardingResponse>>> StartOnboarding(CancellationToken ct) =>
    StatusCode(StatusCodes.Status201Created,
        new ApiResponse<OnboardingResponse>(
            await service.StartOnboardingAsync(User.GetRequiredUserId(), ct)));

    [HttpGet("onboarding")]
    public async Task<ActionResult<ApiResponse<OnboardingResponse>>> GetOnboarding(CancellationToken ct) =>
        Ok(new ApiResponse<OnboardingResponse>(
            await service.OnboardingAsync(User.GetRequiredUserId(), ct)));

    [HttpPatch("onboarding/basic")]
    public async Task<ActionResult<ApiResponse<OnboardingResponse>>> SaveOnboardingBasic(
        OnboardingBasicRequest request,
        CancellationToken ct) =>
        Ok(new ApiResponse<OnboardingResponse>(
            await service.SaveBasicAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPatch("onboarding/professional")]
    public async Task<ActionResult<ApiResponse<OnboardingResponse>>> SaveOnboardingProfessional(
        OnboardingProfessionalRequest request,
        CancellationToken ct) =>
        Ok(new ApiResponse<OnboardingResponse>(
            await service.SaveProfessionalAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPatch("onboarding/expertise")]
    public async Task<ActionResult<ApiResponse<OnboardingResponse>>> SaveOnboardingExpertise(
        OnboardingExpertiseRequest request,
        CancellationToken ct) =>
        Ok(new ApiResponse<OnboardingResponse>(
            await service.SaveExpertiseAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPut("onboarding/education")]
    public async Task<ActionResult<ApiResponse<OnboardingResponse>>> SaveOnboardingEducation(
        OnboardingEducationRequest request,
        CancellationToken ct) =>
        Ok(new ApiResponse<OnboardingResponse>(
            await service.SaveEducationAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPut("onboarding/work-experience")]
    public async Task<ActionResult<ApiResponse<OnboardingResponse>>> SaveOnboardingWorkExperience(
        OnboardingExperienceRequest request,
        CancellationToken ct) =>
        Ok(new ApiResponse<OnboardingResponse>(
            await service.SaveExperienceAsync(User.GetRequiredUserId(), request, ct)));

    [HttpGet("onboarding/import-options")]
    public async Task<ActionResult<ApiResponse<OnboardingImportOptions>>> GetOnboardingImportOptions(
        CancellationToken ct) =>
        Ok(new ApiResponse<OnboardingImportOptions>(
            await service.ImportOptionsAsync(User.GetRequiredUserId(), ct)));

    [HttpPost("onboarding/import")]
    public async Task<ActionResult<ApiResponse<OnboardingResponse>>> ImportOnboarding(
        OnboardingImportRequest request,
        CancellationToken ct) =>
        Ok(new ApiResponse<OnboardingResponse>(
            await service.ImportAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPost("onboarding/submit")]
    public async Task<ActionResult<ApiResponse<OnboardingResponse>>> SubmitOnboarding(
        OnboardingSubmitRequest request,
        CancellationToken ct) =>
        Ok(new ApiResponse<OnboardingResponse>(
            await service.SubmitOnboardingAsync(User.GetRequiredUserId(), request, ct)));
}

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/career-guidance/consultants")]
public sealed class AdminCareerGuidanceController(ICareerGuidanceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<ConsultantPrivateResponse>>>> Search([FromQuery] ConsultantAdminQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<ConsultantPrivateResponse>>(await service.AdminSearchAsync(User.GetRequiredUserId(), query, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ConsultantPrivateResponse>>> Get(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<ConsultantPrivateResponse>(await service.AdminGetAsync(User.GetRequiredUserId(), id, ct)));
    [HttpPost("{id:guid}/verification")]
    public async Task<ActionResult<ApiResponse<ConsultantPrivateResponse>>> Review(Guid id, ConsultantReviewRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<ConsultantPrivateResponse>(await service.ReviewAsync(User.GetRequiredUserId(), id, request, ct)));
}
