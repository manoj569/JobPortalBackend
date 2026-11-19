using JobPortal.Application.Abstractions.EmployerTalentPool;
using JobPortal.Application.Features.EmployerTalentPool;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class EmployerTalentPoolRepository(
    JobPortalDbContext context)
    : IEmployerTalentPoolRepository
{
    public async Task<(
        IReadOnlyCollection<EmployerTalentPoolCandidate> Items,
        int TotalCount)> SearchAsync(
        Guid employerUserId,
        EmployerTalentPoolQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = context.Set<EmployerTalentPool>()
            .AsNoTracking()
            .Where(x =>
                x.EmployerUserId == employerUserId &&
                !x.IsDeleted &&
                x.CandidateUser.RoleId == SystemRoleIds.Candidate &&
                x.CandidateUser.Status == UserStatus.Active &&
                x.CandidateUser.IsRecruiterVisible &&
                x.CandidateUser.OnboardingCompletedAtUtc.HasValue);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            source = source.Where(x =>
                x.CandidateUser.FirstName.Contains(search) ||
                x.CandidateUser.LastName.Contains(search) ||
                (x.CandidateUser.Headline != null &&
                 x.CandidateUser.Headline.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            var location = query.Location.Trim();

            source = source.Where(x =>
                (x.CandidateUser.CurrentCity != null &&
                 x.CandidateUser.CurrentCity.Contains(location)) ||
                (x.CandidateUser.Location != null &&
                 x.CandidateUser.Location.Contains(location)) ||
                x.CandidateUser.PreferredCitiesJson.Contains(location));
        }

        if (!string.IsNullOrWhiteSpace(query.Skill))
        {
            var normalizedSkill = Normalize(query.Skill);

            source = source.Where(x =>
                x.CandidateUser.CandidateSkills.Any(skill =>
                    skill.NormalizedName == normalizedSkill));
        }

        var totalCount = await source.CountAsync(
            cancellationToken);

        var items = await source
            .OrderByDescending(x => x.AddedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new EmployerTalentPoolCandidate(
                x.CandidateUserId,
                x.CandidateUser.FirstName,
                x.CandidateUser.LastName,
                x.CandidateUser.Headline,
                x.CandidateUser.CurrentCity,
                x.CandidateUser.Location,
                x.CandidateUser.YearsOfExperience,
                x.CandidateUser.ProfileImageUrl,
                x.Notes,
                x.AddedAtUtc,
                x.CandidateUser.CandidateSkills
                    .OrderBy(skill => skill.Name)
                    .Select(skill => skill.Name)
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<User?> GetVisibleCandidateAsync(
    Guid candidateUserId,
    CancellationToken cancellationToken = default)
    {
        return context.Users
            .AsNoTracking()
            .Include(x => x.CandidateSkills)
            .SingleOrDefaultAsync(
                x =>
                    x.Id == candidateUserId &&
                    x.RoleId == SystemRoleIds.Candidate &&
                    x.Status == UserStatus.Active &&
                    x.IsRecruiterVisible &&
                    x.OnboardingCompletedAtUtc.HasValue,
                cancellationToken);
    }

    public Task<EmployerTalentPool?> GetAsync(
        Guid employerUserId,
        Guid candidateUserId,
        CancellationToken cancellationToken = default)
    {
        return context.Set<EmployerTalentPool>()
            .SingleOrDefaultAsync(
                x =>
                    x.EmployerUserId == employerUserId &&
                    x.CandidateUserId == candidateUserId &&
                    !x.IsDeleted,
                cancellationToken);
    }

    public async Task AddAsync(
        EmployerTalentPool entry,
        CancellationToken cancellationToken = default)
    {
        await context.Set<EmployerTalentPool>()
            .AddAsync(entry, cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return context.SaveChangesAsync(cancellationToken);
    }

    private static string Normalize(string value)
    {
        return string.Concat(
            value
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit));
    }
}
