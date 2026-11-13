using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Administrator")]
[Route("api/admin/accelerator/v1/taxonomies")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(131072), EnableRateLimiting("CareerAccelerator")]
public sealed class AdminAcceleratorController(AcceleratorService service, AcceleratorPhase2Service phase2) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<TaxonomyResponse?>>> Get([FromQuery] Guid? versionId, CancellationToken ct) =>
        Ok(new ApiResponse<TaxonomyResponse?>(await service.AdminTaxonomyAsync(User.GetRequiredUserId(), versionId, ct)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<TaxonomyResponse>>> Publish(PublishTaxonomyRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<TaxonomyResponse>(await service.PublishTaxonomyAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPost("canonical-skills")]
    public async Task<ActionResult<ApiResponse<CanonicalAcceleratorSkill>>> CreateCanonicalSkill(CreateCanonicalAcceleratorSkillRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CanonicalAcceleratorSkill>(await service.CreateCanonicalSkillAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPut("assessment-templates")]
    public async Task<ActionResult<ApiResponse<Guid>>> UpsertTemplate(UpsertAssessmentTemplateRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await phase2.UpsertTemplateAsync(User.GetRequiredUserId(), request, ct)));

    [HttpPut("questions")]
    public async Task<ActionResult<ApiResponse<Guid>>> UpsertQuestion(UpsertAssessmentQuestionRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<Guid>(await phase2.UpsertQuestionAsync(User.GetRequiredUserId(), request, ct)));
}
