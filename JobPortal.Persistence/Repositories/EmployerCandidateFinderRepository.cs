using JobPortal.Application.Abstractions.EmployerCandidateFinder;
using JobPortal.Application.Features.EmployerCandidateFinder;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class EmployerCandidateFinderRepository(
    JobPortalDbContext context)
    : IEmployerCandidateFinderRepository
{
    public Task<Job?> GetOwnedJobAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return context.Jobs
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.JobSkills)
                .ThenInclude(x => x.Skill)
            .SingleOrDefaultAsync(
                x =>
                    x.Id == jobId &&
                    x.Company.OwnerUserId == employerUserId,
                cancellationToken);
    }

    public async Task<IReadOnlyCollection<EmployerCandidateFinderCandidate>>
        GetCandidatesAsync(
            EmployerCandidateFinderQuery query,
            CancellationToken cancellationToken = default)
    {
        var source = context.Users
            .AsNoTracking()
            .Where(x =>
                x.RoleId == SystemRoleIds.Candidate &&
                x.Status == UserStatus.Active &&
                x.IsRecruiterVisible &&
                x.OnboardingCompletedAtUtc.HasValue);

        if (query.MinimumExperienceYears.HasValue)
        {
            source = source.Where(x =>
                x.YearsOfExperience.HasValue &&
                x.YearsOfExperience.Value >=
                    query.MinimumExperienceYears.Value);
        }

        if (query.MaximumExperienceYears.HasValue)
        {
            source = source.Where(x =>
                x.YearsOfExperience.HasValue &&
                x.YearsOfExperience.Value <=
                    query.MaximumExperienceYears.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            var location = query.Location.Trim();

            source = source.Where(x =>
                (x.CurrentCity != null &&
                 x.CurrentCity.Contains(location)) ||

                (x.Location != null &&
                 x.Location.Contains(location)) ||

                x.PreferredCitiesJson.Contains(location));
        }

        if (!string.IsNullOrWhiteSpace(query.Skill))
        {
            var normalizedSkill = Normalize(query.Skill);

            source = source.Where(x =>
                x.CandidateSkills.Any(candidateSkill =>
                    candidateSkill.NormalizedName == normalizedSkill));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            source = source.Where(x =>
                x.FirstName.Contains(search) ||

                x.LastName.Contains(search) ||

                (x.Headline != null &&
                 x.Headline.Contains(search)) ||

                (x.CurrentCity != null &&
                 x.CurrentCity.Contains(search)) ||

                x.CandidateSkills.Any(candidateSkill =>
                    candidateSkill.Name.Contains(search)));
        }

        /*
         * We intentionally bound the candidate set before
         * in-memory deterministic scoring.
         *
         * This protects the endpoint from loading an unlimited
         * number of candidate profiles during MVP.
         */
        var candidates = await source
            .OrderByDescending(x => x.ResumeUploadedAtUtc)
            .ThenBy(x => x.Id)
            .Take(500)
            .Select(x => new
            {
                x.Id,
                x.FirstName,
                x.LastName,
                x.Headline,
                x.CurrentCity,
                x.Location,
                x.YearsOfExperience,
                x.PreferredJobRolesJson,
                x.PreferredCitiesJson,

                Skills = x.CandidateSkills
                    .Select(skill =>
                        new EmployerCandidateFinderSkill(
                            skill.Name,
                            skill.NormalizedName,
                            skill.YearsOfExperience))
                    .ToArray()
            })
            .ToArrayAsync(cancellationToken);

        return candidates
            .Select(x =>
                new EmployerCandidateFinderCandidate(
                    x.Id,
                    x.FirstName,
                    x.LastName,
                    x.Headline,
                    x.CurrentCity,
                    x.Location,
                    x.YearsOfExperience,
                    x.PreferredJobRolesJson,
                    x.PreferredCitiesJson,
                    x.Skills))
            .ToArray();
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
