using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public sealed record CandidatePortfolioData(
    User User,
    CandidatePortfolio? Portfolio,
    IReadOnlyCollection<CandidateSkill> StructuredSkills,
    IReadOnlyCollection<CandidateExperience> Experiences,
    IReadOnlyCollection<CandidateEducation> Education,
    IReadOnlyCollection<CandidateProject> Projects,
    IReadOnlyCollection<CandidateCertification> Certifications,
    IReadOnlyCollection<CandidateProfessionalLink> ProfessionalLinks,
    IReadOnlyCollection<PortfolioCustomSection> CustomSections);

public interface ICandidatePortfolioRepository
{
    Task<CandidatePortfolioData?> GetCandidateDataAsync(Guid userId, bool tracking,
        CancellationToken cancellationToken = default);
    Task<CandidatePortfolioData?> GetPublishedDataAsync(string normalizedSlug,
        CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(string normalizedSlug, Guid? excludingPortfolioId,
        CancellationToken cancellationToken = default);
    Task AddPortfolioAsync(CandidatePortfolio portfolio, CancellationToken cancellationToken = default);
    Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : BaseEntity;
    void Remove<TEntity>(TEntity entity) where TEntity : BaseEntity;
}
