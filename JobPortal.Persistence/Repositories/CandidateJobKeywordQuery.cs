using System.Linq.Expressions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

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
            Expression<Func<Job, bool>> Matches(string term) => job =>
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
                job.JobSkills.Any(skill => skill.Skill.Name.ToLower().Contains(term)) ||
                (job.Location != null && job.Location.ToLower().Contains(term)) ||
                (term == "remote" && job.WorkplaceType == WorkplaceType.Remote);
            var predicate = Matches(term);
            var alias = term switch { "dotnet" => ".net", ".net" => "dotnet", "aspnet" => "asp.net", "asp.net" => "aspnet", _ => null };
            if (alias is not null)
            {
                var alternative = Matches(alias);
                var body = new ReplaceParameter(alternative.Parameters[0], predicate.Parameters[0]).Visit(alternative.Body)!;
                predicate = Expression.Lambda<Func<Job, bool>>(Expression.OrElse(predicate.Body, body), predicate.Parameters);
            }
            source = source.Where(predicate);
        }
        return source;
    }

    internal static IOrderedQueryable<Job> Rank(IQueryable<Job> source, string query)
    {
        var normalized = query.ToLowerInvariant();
        // Full-query tiers dominate; weighted token coverage breaks cross-field ties.
        Expression<Func<Job, int>> coverage = job => 0;
        foreach (var token in normalized.Split(' ').Distinct())
        {
            Expression<Func<Job, int>> match = job => job.Title.ToLower().Contains(token) ? 4 :
                job.JobSkills.Any(skill => skill.Skill.Name.ToLower().Contains(token)) ? 3 :
                job.Description.ToLower().Contains(token) ||
                (job.Requirements != null && job.Requirements.ToLower().Contains(token)) ||
                (job.Responsibilities != null && job.Responsibilities.ToLower().Contains(token)) ? 2 :
                (job.Location != null && job.Location.ToLower().Contains(token)) ? 1 : 0;
            var body = new ReplaceParameter(match.Parameters[0], coverage.Parameters[0]).Visit(match.Body)!;
            coverage = Expression.Lambda<Func<Job, int>>(Expression.Add(coverage.Body, body), coverage.Parameters);
        }
        return source.OrderByDescending(job =>
                job.Title.Trim().ToLower() == normalized ? 6 :
                job.Title.ToLower().StartsWith(normalized) ? 5 :
                job.Title.ToLower().Contains(normalized) ? 4 :
                job.Company.Name.ToLower().Contains(normalized) ? 3 :
                job.JobSkills.Any(skill => skill.Skill.Name.ToLower().Contains(normalized)) ? 2 : 0)
            .ThenByDescending(coverage)
            .ThenByDescending(job => job.IsFeatured)
            .ThenByDescending(job => job.PublishedAtUtc)
            .ThenBy(job => job.Id);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
#pragma warning restore CA1304, CA1311, CA1862
}
