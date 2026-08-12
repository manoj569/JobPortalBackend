using JobPortal.Application.Features.Portfolios;

namespace JobPortal.Application.Abstractions.Portfolios;

public interface ICandidatePortfolioService
{
    Task<PortfolioEditorResponse> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PortfolioEditorResponse> CreateAsync(Guid userId, CreatePortfolioRequest request, CancellationToken cancellationToken = default);
    Task<PortfolioEditorResponse> UpdateSettingsAsync(Guid userId, UpdatePortfolioSettingsRequest request, CancellationToken cancellationToken = default);
    Task<PublicPortfolioResponse> PreviewAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PortfolioPublishResponse> PublishAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PortfolioEditorResponse> UnpublishAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PublicPortfolioResponse> GetPublicAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ExperienceResponse>> GetExperiencesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ExperienceResponse> AddExperienceAsync(Guid userId, ExperienceRequest request, CancellationToken cancellationToken = default);
    Task<ExperienceResponse> UpdateExperienceAsync(Guid userId, Guid id, ExperienceRequest request, CancellationToken cancellationToken = default);
    Task DeleteExperienceAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<EducationResponse>> GetEducationAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<EducationResponse> AddEducationAsync(Guid userId, EducationRequest request, CancellationToken cancellationToken = default);
    Task<EducationResponse> UpdateEducationAsync(Guid userId, Guid id, EducationRequest request, CancellationToken cancellationToken = default);
    Task DeleteEducationAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ProjectResponse>> GetProjectsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ProjectResponse> AddProjectAsync(Guid userId, ProjectRequest request, CancellationToken cancellationToken = default);
    Task<ProjectResponse> UpdateProjectAsync(Guid userId, Guid id, ProjectRequest request, CancellationToken cancellationToken = default);
    Task DeleteProjectAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CertificationResponse>> GetCertificationsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CertificationResponse> AddCertificationAsync(Guid userId, CertificationRequest request, CancellationToken cancellationToken = default);
    Task<CertificationResponse> UpdateCertificationAsync(Guid userId, Guid id, CertificationRequest request, CancellationToken cancellationToken = default);
    Task DeleteCertificationAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ProfessionalLinkResponse>> GetLinksAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ProfessionalLinkResponse> AddLinkAsync(Guid userId, ProfessionalLinkRequest request, CancellationToken cancellationToken = default);
    Task<ProfessionalLinkResponse> UpdateLinkAsync(Guid userId, Guid id, ProfessionalLinkRequest request, CancellationToken cancellationToken = default);
    Task DeleteLinkAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CustomSectionResponse>> GetCustomSectionsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CustomSectionResponse> AddCustomSectionAsync(Guid userId, CustomSectionRequest request, CancellationToken cancellationToken = default);
    Task<CustomSectionResponse> UpdateCustomSectionAsync(Guid userId, Guid id, CustomSectionRequest request, CancellationToken cancellationToken = default);
    Task DeleteCustomSectionAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<CustomItemResponse> AddCustomItemAsync(Guid userId, Guid sectionId, CustomItemRequest request, CancellationToken cancellationToken = default);
    Task<CustomItemResponse> UpdateCustomItemAsync(Guid userId, Guid sectionId, Guid itemId, CustomItemRequest request, CancellationToken cancellationToken = default);
    Task DeleteCustomItemAsync(Guid userId, Guid sectionId, Guid itemId, CancellationToken cancellationToken = default);
}
