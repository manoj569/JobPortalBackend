using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class CandidatePortfolioRepository(JobPortalDbContext context) : ICandidatePortfolioRepository
{
    public async Task<CandidatePortfolioData?> GetCandidateDataAsync(
        Guid userId, bool tracking, CancellationToken cancellationToken = default)
    {
        var users = context.Users.Where(x => x.Id == userId &&
            x.RoleId == SystemRoleIds.Candidate && x.Status == UserStatus.Active);
        var user = await (tracking ? users : users.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
        return user is null ? null : await LoadAsync(user, tracking, cancellationToken);
    }

    public async Task<CandidatePortfolioData?> GetPublishedDataAsync(
        string normalizedSlug, CancellationToken cancellationToken = default)
    {
        var portfolio = await context.CandidatePortfolios.AsNoTracking()
            .Include(x => x.SectionSettings)
            .Where(x => x.NormalizedSlug == normalizedSlug &&
                x.Status == CandidatePortfolioStatus.Published)
            .SingleOrDefaultAsync(cancellationToken);
        if (portfolio is null) return null;
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == portfolio.UserId && x.Status == UserStatus.Active, cancellationToken);
        return user is null ? null : await LoadAsync(user, false, cancellationToken, portfolio);
    }

    public Task<bool> SlugExistsAsync(
        string normalizedSlug, Guid? excludingPortfolioId,
        CancellationToken cancellationToken = default) =>
        context.CandidatePortfolios.AnyAsync(x => x.NormalizedSlug == normalizedSlug &&
            (!excludingPortfolioId.HasValue || x.Id != excludingPortfolioId.Value), cancellationToken);

    public Task AddPortfolioAsync(
        CandidatePortfolio portfolio, CancellationToken cancellationToken = default) =>
        context.CandidatePortfolios.AddAsync(portfolio, cancellationToken).AsTask();

    public Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : BaseEntity => context.Set<TEntity>().AddAsync(entity, cancellationToken).AsTask();

    public void Remove<TEntity>(TEntity entity) where TEntity : BaseEntity => context.Set<TEntity>().Remove(entity);

    private async Task<CandidatePortfolioData> LoadAsync(
        User user, bool tracking, CancellationToken cancellationToken,
        CandidatePortfolio? knownPortfolio = null)
    {
        IQueryable<CandidatePortfolio> portfolios = context.CandidatePortfolios.Include(x => x.SectionSettings);
        IQueryable<CandidateSkill> skills = context.CandidateSkills;
        IQueryable<CandidateExperience> experiences = context.CandidateExperiences;
        IQueryable<CandidateEducation> education = context.CandidateEducation;
        IQueryable<CandidateProject> projects = context.CandidateProjects;
        IQueryable<CandidateCertification> certifications = context.CandidateCertifications;
        IQueryable<CandidateProfessionalLink> links = context.CandidateProfessionalLinks;
        IQueryable<PortfolioCustomSection> customSections = context.PortfolioCustomSections.Include(x => x.Items);
        if (!tracking)
        {
            portfolios = portfolios.AsNoTracking(); skills = skills.AsNoTracking();
            experiences = experiences.AsNoTracking(); education = education.AsNoTracking();
            projects = projects.AsNoTracking(); certifications = certifications.AsNoTracking();
            links = links.AsNoTracking(); customSections = customSections.AsNoTracking();
        }
        var portfolio = knownPortfolio ?? await portfolios.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        return new(user, portfolio,
            await skills.Where(x => x.UserId == user.Id).OrderBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync(cancellationToken),
            await experiences.Where(x => x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArrayAsync(cancellationToken),
            await education.Where(x => x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArrayAsync(cancellationToken),
            await projects.Where(x => x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArrayAsync(cancellationToken),
            await certifications.Where(x => x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArrayAsync(cancellationToken),
            await links.Where(x => x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArrayAsync(cancellationToken),
            await customSections.Where(x => x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArrayAsync(cancellationToken));
    }
}
