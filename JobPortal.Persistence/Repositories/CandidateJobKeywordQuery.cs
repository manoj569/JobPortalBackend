using JobPortal.Domain.Entities;

namespace JobPortal.Persistence.Repositories;

/// <summary>Literal partial terms, AND across terms and OR across job content fields.</summary>
internal static class CandidateJobKeywordQuery
{
#pragma warning disable CA1304, CA1311, CA1862 // EF translates case folding and literal substring matching server-side.
    internal static IQueryable<Job> Apply(IQueryable<Job> source, string? keyword)
    {
        var terms = keyword?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.ToLowerInvariant()).Distinct().ToArray() ?? [];
        foreach (var term in terms)
        {
            // Contains uses literal substring semantics; %, _ and backslash are not wildcards.
            source = source.Where(job =>
                job.Title.ToLower().Contains(term) ||
                job.Description.ToLower().Contains(term) ||
                (job.Requirements != null && job.Requirements.ToLower().Contains(term)) ||
                (job.Responsibilities != null && job.Responsibilities.ToLower().Contains(term)) ||
                (job.Benefits != null && job.Benefits.ToLower().Contains(term)) ||
                (job.Department != null && job.Department.ToLower().Contains(term)) ||
                (job.RoleCategory != null && job.RoleCategory.ToLower().Contains(term)) ||
                (job.EducationRequirement != null && job.EducationRequirement.ToLower().Contains(term)) ||
                job.Company.Name.ToLower().Contains(term) ||
                (job.Category != null && job.Category.Name.ToLower().Contains(term)) ||
                job.JobSkills.Any(skill => skill.Skill.Name.ToLower().Contains(term)));
        }
        return source;
    }
#pragma warning restore CA1304, CA1311, CA1862
}
