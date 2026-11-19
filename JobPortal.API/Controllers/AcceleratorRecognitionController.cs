using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles="Candidate"), Route("api/accelerator/v1/journeys/{contextId:guid}")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None), EnableRateLimiting("CareerAccelerator")]
public sealed class AcceleratorRecognitionController(AcceleratorRecognitionService service) : ControllerBase
{
    [HttpGet("~/api/accelerator/v1/enrollments/{enrollmentId:guid}/recognition")]
    public async Task<ActionResult<ApiResponse<EnrollmentRecognitionResponse>>> Enrollment(Guid enrollmentId,CancellationToken ct) => Ok(new ApiResponse<EnrollmentRecognitionResponse>(await service.EnrollmentAsync(User.GetRequiredUserId(),enrollmentId,ct)));
    [HttpGet("proficiency")]
    public async Task<ActionResult<ApiResponse<JourneyProficiencyResponse>>> Proficiency(Guid contextId,CancellationToken ct) => Ok(new ApiResponse<JourneyProficiencyResponse>(await service.ProficiencyAsync(User.GetRequiredUserId(),contextId,ct)));
    [HttpGet("achievements")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<JourneyAchievementResponse>>>> Achievements(Guid contextId,CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<JourneyAchievementResponse>>(await service.AchievementsAsync(User.GetRequiredUserId(),contextId,ct)));
    [HttpPost("achievements/evaluate")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<JourneyAchievementResponse>>>> EvaluateAchievements(Guid contextId,CancellationToken ct) => Ok(new ApiResponse<IReadOnlyList<JourneyAchievementResponse>>(await service.EvaluateAchievementsAsync(User.GetRequiredUserId(),contextId,ct)));
    [HttpGet("certificate")]
    public async Task<ActionResult<ApiResponse<JourneyCertificateResponse>>> Certificate(Guid contextId,CancellationToken ct) => Ok(new ApiResponse<JourneyCertificateResponse>(await service.CertificateAsync(User.GetRequiredUserId(),contextId,ct)));
    [HttpPost("certificate/evaluate")]
    public async Task<ActionResult<ApiResponse<JourneyCertificateResponse>>> EvaluateCertificate(Guid contextId,CancellationToken ct) => Ok(new ApiResponse<JourneyCertificateResponse>(await service.EvaluateCertificateAsync(User.GetRequiredUserId(),contextId,ct)));
}

[ApiController, Authorize(Roles="Administrator"), Route("api/admin/accelerator/v1/recognition")]
[RequestSizeLimit(131072)]
public sealed class AdminAcceleratorRecognitionController(AcceleratorRecognitionService service) : ControllerBase
{
    [HttpPost("policies")]
    public async Task<ActionResult<ApiResponse<Guid>>> Policy(CreateProficiencyPolicyRequest request,CancellationToken ct) => Ok(new ApiResponse<Guid>(await service.CreatePolicyAsync(User.GetRequiredUserId(),request,ct)));
    [HttpPost("achievement-definitions")]
    public async Task<ActionResult<ApiResponse<Guid>>> Achievement(CreateAchievementDefinitionRequest request,CancellationToken ct) => Ok(new ApiResponse<Guid>(await service.CreateAchievementAsync(User.GetRequiredUserId(),request,ct)));
}
