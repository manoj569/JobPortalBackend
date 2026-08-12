using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Portfolios;
using JobPortal.Application.Features.Portfolios;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Candidate")]
[Route("api/candidate/portfolio")]
[Produces("application/json")]
public sealed class CandidatePortfolioController(ICandidatePortfolioService portfolios) : ControllerBase
{
    private Guid UserId => User.GetRequiredUserId();

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PortfolioEditorResponse>>> Get(CancellationToken ct) =>
        Ok(new ApiResponse<PortfolioEditorResponse>(await portfolios.GetAsync(UserId, ct)));
    [HttpPost]
    public async Task<ActionResult<ApiResponse<PortfolioEditorResponse>>> Create(CreatePortfolioRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PortfolioEditorResponse>(await portfolios.CreateAsync(UserId, request, ct), "Portfolio draft ready."));
    [HttpPut("settings")]
    public async Task<ActionResult<ApiResponse<PortfolioEditorResponse>>> Settings(UpdatePortfolioSettingsRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PortfolioEditorResponse>(await portfolios.UpdateSettingsAsync(UserId, request, ct), "Portfolio settings updated."));
    [HttpGet("preview")]
    public async Task<ActionResult<ApiResponse<PublicPortfolioResponse>>> Preview(CancellationToken ct) =>
        Ok(new ApiResponse<PublicPortfolioResponse>(await portfolios.PreviewAsync(UserId, ct)));
    [HttpPost("publish")]
    public async Task<ActionResult<ApiResponse<PortfolioPublishResponse>>> Publish(CancellationToken ct) =>
        Ok(new ApiResponse<PortfolioPublishResponse>(await portfolios.PublishAsync(UserId, ct)));
    [HttpPost("unpublish")]
    public async Task<ActionResult<ApiResponse<PortfolioEditorResponse>>> Unpublish(CancellationToken ct) =>
        Ok(new ApiResponse<PortfolioEditorResponse>(await portfolios.UnpublishAsync(UserId, ct), "Portfolio unpublished."));

    [HttpGet("experiences")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<ExperienceResponse>>>> Experiences(CancellationToken ct) => Ok(new ApiResponse<IReadOnlyCollection<ExperienceResponse>>(await portfolios.GetExperiencesAsync(UserId, ct)));
    [HttpPost("experiences")]
    public async Task<ActionResult<ApiResponse<ExperienceResponse>>> AddExperience(ExperienceRequest request, CancellationToken ct) => CreatedResponse(await portfolios.AddExperienceAsync(UserId, request, ct));
    [HttpPut("experiences/{id:guid}")]
    public async Task<ActionResult<ApiResponse<ExperienceResponse>>> UpdateExperience(Guid id, ExperienceRequest request, CancellationToken ct) => Ok(new ApiResponse<ExperienceResponse>(await portfolios.UpdateExperienceAsync(UserId, id, request, ct)));
    [HttpDelete("experiences/{id:guid}")]
    public async Task<IActionResult> DeleteExperience(Guid id, CancellationToken ct) { await portfolios.DeleteExperienceAsync(UserId, id, ct); return NoContent(); }

    [HttpGet("education")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<EducationResponse>>>> Education(CancellationToken ct) => Ok(new ApiResponse<IReadOnlyCollection<EducationResponse>>(await portfolios.GetEducationAsync(UserId, ct)));
    [HttpPost("education")]
    public async Task<ActionResult<ApiResponse<EducationResponse>>> AddEducation(EducationRequest request, CancellationToken ct) => CreatedResponse(await portfolios.AddEducationAsync(UserId, request, ct));
    [HttpPut("education/{id:guid}")]
    public async Task<ActionResult<ApiResponse<EducationResponse>>> UpdateEducation(Guid id, EducationRequest request, CancellationToken ct) => Ok(new ApiResponse<EducationResponse>(await portfolios.UpdateEducationAsync(UserId, id, request, ct)));
    [HttpDelete("education/{id:guid}")]
    public async Task<IActionResult> DeleteEducation(Guid id, CancellationToken ct) { await portfolios.DeleteEducationAsync(UserId, id, ct); return NoContent(); }

    [HttpGet("projects")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<ProjectResponse>>>> Projects(CancellationToken ct) => Ok(new ApiResponse<IReadOnlyCollection<ProjectResponse>>(await portfolios.GetProjectsAsync(UserId, ct)));
    [HttpPost("projects")]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> AddProject(ProjectRequest request, CancellationToken ct) => CreatedResponse(await portfolios.AddProjectAsync(UserId, request, ct));
    [HttpPut("projects/{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> UpdateProject(Guid id, ProjectRequest request, CancellationToken ct) => Ok(new ApiResponse<ProjectResponse>(await portfolios.UpdateProjectAsync(UserId, id, request, ct)));
    [HttpDelete("projects/{id:guid}")]
    public async Task<IActionResult> DeleteProject(Guid id, CancellationToken ct) { await portfolios.DeleteProjectAsync(UserId, id, ct); return NoContent(); }

    [HttpGet("certifications")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<CertificationResponse>>>> Certifications(CancellationToken ct) => Ok(new ApiResponse<IReadOnlyCollection<CertificationResponse>>(await portfolios.GetCertificationsAsync(UserId, ct)));
    [HttpPost("certifications")]
    public async Task<ActionResult<ApiResponse<CertificationResponse>>> AddCertification(CertificationRequest request, CancellationToken ct) => CreatedResponse(await portfolios.AddCertificationAsync(UserId, request, ct));
    [HttpPut("certifications/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CertificationResponse>>> UpdateCertification(Guid id, CertificationRequest request, CancellationToken ct) => Ok(new ApiResponse<CertificationResponse>(await portfolios.UpdateCertificationAsync(UserId, id, request, ct)));
    [HttpDelete("certifications/{id:guid}")]
    public async Task<IActionResult> DeleteCertification(Guid id, CancellationToken ct) { await portfolios.DeleteCertificationAsync(UserId, id, ct); return NoContent(); }

    [HttpGet("links")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<ProfessionalLinkResponse>>>> Links(CancellationToken ct) => Ok(new ApiResponse<IReadOnlyCollection<ProfessionalLinkResponse>>(await portfolios.GetLinksAsync(UserId, ct)));
    [HttpPost("links")]
    public async Task<ActionResult<ApiResponse<ProfessionalLinkResponse>>> AddLink(ProfessionalLinkRequest request, CancellationToken ct) => CreatedResponse(await portfolios.AddLinkAsync(UserId, request, ct));
    [HttpPut("links/{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProfessionalLinkResponse>>> UpdateLink(Guid id, ProfessionalLinkRequest request, CancellationToken ct) => Ok(new ApiResponse<ProfessionalLinkResponse>(await portfolios.UpdateLinkAsync(UserId, id, request, ct)));
    [HttpDelete("links/{id:guid}")]
    public async Task<IActionResult> DeleteLink(Guid id, CancellationToken ct) { await portfolios.DeleteLinkAsync(UserId, id, ct); return NoContent(); }

    [HttpGet("custom-sections")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<CustomSectionResponse>>>> CustomSections(CancellationToken ct) => Ok(new ApiResponse<IReadOnlyCollection<CustomSectionResponse>>(await portfolios.GetCustomSectionsAsync(UserId, ct)));
    [HttpPost("custom-sections")]
    public async Task<ActionResult<ApiResponse<CustomSectionResponse>>> AddCustomSection(CustomSectionRequest request, CancellationToken ct) => CreatedResponse(await portfolios.AddCustomSectionAsync(UserId, request, ct));
    [HttpPut("custom-sections/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CustomSectionResponse>>> UpdateCustomSection(Guid id, CustomSectionRequest request, CancellationToken ct) => Ok(new ApiResponse<CustomSectionResponse>(await portfolios.UpdateCustomSectionAsync(UserId, id, request, ct)));
    [HttpDelete("custom-sections/{id:guid}")]
    public async Task<IActionResult> DeleteCustomSection(Guid id, CancellationToken ct) { await portfolios.DeleteCustomSectionAsync(UserId, id, ct); return NoContent(); }
    [HttpPost("custom-sections/{sectionId:guid}/items")]
    public async Task<ActionResult<ApiResponse<CustomItemResponse>>> AddCustomItem(Guid sectionId, CustomItemRequest request, CancellationToken ct) => CreatedResponse(await portfolios.AddCustomItemAsync(UserId, sectionId, request, ct));
    [HttpPut("custom-sections/{sectionId:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<ApiResponse<CustomItemResponse>>> UpdateCustomItem(Guid sectionId, Guid itemId, CustomItemRequest request, CancellationToken ct) => Ok(new ApiResponse<CustomItemResponse>(await portfolios.UpdateCustomItemAsync(UserId, sectionId, itemId, request, ct)));
    [HttpDelete("custom-sections/{sectionId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> DeleteCustomItem(Guid sectionId, Guid itemId, CancellationToken ct) { await portfolios.DeleteCustomItemAsync(UserId, sectionId, itemId, ct); return NoContent(); }

    private ActionResult<ApiResponse<T>> CreatedResponse<T>(T value) =>
        StatusCode(StatusCodes.Status201Created, new ApiResponse<T>(value));
}
