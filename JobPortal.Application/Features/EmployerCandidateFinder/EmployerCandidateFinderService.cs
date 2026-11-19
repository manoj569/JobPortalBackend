using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.EmployerCandidateFinder;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.EmployerCandidateFinder;

public sealed class EmployerCandidateFinderService(
    IEmployerCandidateFinderRepository repository,
    IValidator<EmployerCandidateFinderQuery> validator)
    : IEmployerCandidateFinderService
{
    public async Task<EmployerCandidateFinderResponse> SearchAsync(
        Guid employerUserId,
        Guid jobId,
        EmployerCandidateFinderQuery query,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var job = await repository.GetOwnedJobAsync(
            employerUserId,
            jobId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Job was not found.");

        var candidates = await repository.GetCandidatesAsync(
            query,
            cancellationToken);

        var matches = candidates
            .Select(candidate => Score(job, candidate))
            .Where(x => x.MatchScore >= query.MinimumMatchScore)
            .OrderByDescending(x => x.MatchScore)
            .ThenByDescending(x => x.SkillScore)
            .ThenBy(x => x.FullName)
            .ToArray();

        var totalCount = matches.Length;

        var items = matches
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();

        return new EmployerCandidateFinderResponse(
            job.Id,
            job.Title,
            items,
            totalCount,
            query.Page,
            query.PageSize);
    }

    private static EmployerCandidateMatchResponse Score(
        Job job,
        EmployerCandidateFinderCandidate candidate)
    {
        var jobSkills = job.JobSkills
            .Where(x => !x.IsDeleted)
            .Select(x => new
            {
                Name = x.Skill.Name,

                NormalizedName = Normalize(
                    string.IsNullOrWhiteSpace(x.Skill.NormalizedName)
                        ? x.Skill.Name
                        : x.Skill.NormalizedName),

                x.IsRequired
            })
            .Where(x => x.NormalizedName.Length > 0)
            .DistinctBy(x => x.NormalizedName)
            .ToArray();

        var candidateSkills = candidate.Skills
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => Normalize(
                string.IsNullOrWhiteSpace(x.NormalizedName)
                    ? x.Name
                    : x.NormalizedName))
            .ToDictionary(
                x => x.Key,
                x => x.First());

        var matchedSkills = jobSkills
            .Where(x => candidateSkills.ContainsKey(x.NormalizedName))
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();

        var requiredSkills = jobSkills
            .Where(x => x.IsRequired)
            .ToArray();

        var missingRequiredSkills = requiredSkills
            .Where(x => !candidateSkills.ContainsKey(x.NormalizedName))
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();

        // Maximum 50 points.
        var skillScore = jobSkills.Length == 0
            ? 50
            : (int)Math.Round(
                50m * matchedSkills.Length / jobSkills.Length,
                MidpointRounding.AwayFromZero);

        // Maximum 25 points.
        var experienceScore = CalculateExperienceScore(
            candidate.YearsOfExperience,
            job.MinimumExperienceYears,
            job.MaximumExperienceYears);

        // Maximum 15 points.
        var locationScore = CalculateLocationScore(
            candidate.CurrentCity ?? candidate.Location,
            ParseArray(candidate.PreferredCitiesJson),
            job.Location,
            job.WorkplaceType.ToString());

        // Maximum 10 points.
        var roleScore = CalculateRoleScore(
            ParseArray(candidate.PreferredJobRolesJson),
            job.Title,
            job.RoleCategory);

        var totalScore = Math.Clamp(
            skillScore +
            experienceScore +
            locationScore +
            roleScore,
            0,
            100);

        var reasons = new List<string>();

        if (matchedSkills.Length > 0)
        {
            reasons.Add(
                $"Matches {matchedSkills.Length} job skill(s): " +
                $"{string.Join(", ", matchedSkills.Take(5))}.");
        }

        if (experienceScore >= 20)
        {
            reasons.Add(
                "Experience is within or close to the job requirement.");
        }

        if (locationScore >= 10)
        {
            reasons.Add(
                "Location/workplace preference aligns with the job.");
        }

        if (roleScore >= 8)
        {
            reasons.Add(
                "Preferred role aligns with the job title/category.");
        }

        if (missingRequiredSkills.Length > 0)
        {
            reasons.Add(
                $"Missing required skill(s): " +
                $"{string.Join(", ", missingRequiredSkills.Take(5))}.");
        }

        return new EmployerCandidateMatchResponse(
            candidate.UserId,
            $"{candidate.FirstName} {candidate.LastName}".Trim(),
            candidate.Headline,
            candidate.CurrentCity ?? candidate.Location,
            candidate.YearsOfExperience,
            totalScore,
            skillScore,
            experienceScore,
            locationScore,
            roleScore,
            matchedSkills,
            missingRequiredSkills,
            reasons);
    }

    private static int CalculateExperienceScore(
        decimal? candidateYears,
        int? minimumYears,
        int? maximumYears)
    {
        if (!minimumYears.HasValue &&
            !maximumYears.HasValue)
        {
            return 25;
        }

        if (!candidateYears.HasValue)
        {
            return 0;
        }

        var years = candidateYears.Value;

        if ((!minimumYears.HasValue ||
             years >= minimumYears.Value) &&
            (!maximumYears.HasValue ||
             years <= maximumYears.Value))
        {
            return 25;
        }

        if (minimumYears.HasValue &&
            years < minimumYears.Value)
        {
            var difference = minimumYears.Value - years;

            if (difference <= 1)
            {
                return 18;
            }

            if (difference <= 2)
            {
                return 10;
            }

            return 0;
        }

        // Candidate has more experience than requested.
        return 20;
    }

    private static int CalculateLocationScore(
        string? currentLocation,
        IReadOnlyCollection<string> preferredCities,
        string? jobLocation,
        string workplaceType)
    {
        if (workplaceType.Contains(
                "Remote",
                StringComparison.OrdinalIgnoreCase))
        {
            return 15;
        }

        if (string.IsNullOrWhiteSpace(jobLocation))
        {
            return 10;
        }

        var normalizedJobLocation = Normalize(jobLocation);

        if (!string.IsNullOrWhiteSpace(currentLocation))
        {
            var normalizedCurrentLocation =
                Normalize(currentLocation);

            if (normalizedCurrentLocation.Contains(
                    normalizedJobLocation) ||
                normalizedJobLocation.Contains(
                    normalizedCurrentLocation))
            {
                return 15;
            }
        }

        if (preferredCities.Any(city =>
        {
            var normalizedCity = Normalize(city);

            return normalizedCity.Length > 0 &&
                   (normalizedJobLocation.Contains(normalizedCity) ||
                    normalizedCity.Contains(normalizedJobLocation));
        }))
        {
            return 15;
        }

        return 0;
    }

    private static int CalculateRoleScore(
    string[] preferredRoles,
    string jobTitle,
    string? roleCategory)
    {
        if (preferredRoles.Length == 0)
        {
            return 0;
        }

        var normalizedJobTitle = Normalize(jobTitle);
        var normalizedRoleCategory = Normalize(roleCategory);

        foreach (var preferredRole in preferredRoles)
        {
            var normalizedPreferredRole =
                Normalize(preferredRole);

            if (normalizedPreferredRole.Length == 0)
            {
                continue;
            }

            if (normalizedJobTitle.Contains(
                    normalizedPreferredRole) ||
                normalizedPreferredRole.Contains(
                    normalizedJobTitle))
            {
                return 10;
            }

            if (normalizedRoleCategory.Length > 0 &&
                (normalizedRoleCategory.Contains(
                     normalizedPreferredRole) ||
                 normalizedPreferredRole.Contains(
                     normalizedRoleCategory)))
            {
                return 10;
            }
        }

        return 0;
    }

    private static string[] ParseArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json)
                   ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(
            value
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit));
    }
}
