using System.Collections.Frozen;

namespace JobPortal.Application.Features.CareerAccelerator;

// The market worker uses this only when an installation has no published Accelerator
// taxonomy at all.  It deliberately recognizes a small, reviewed set of role families;
// arbitrary job titles continue through the discovery/review workflow.
public static class MarketFoundationTaxonomyPolicy
{
    public const string Version = "market-foundation-v1";
    private static readonly string[] SeniorityPrefixes = ["SENIOR ", "JUNIOR ", "LEAD ",
        "PRINCIPAL ", "STAFF ", "ASSOCIATE "];

    private static readonly FrozenDictionary<string, FoundationRole> Roles =
        new Dictionary<string, FoundationRole>(StringComparer.Ordinal)
        {
            ["BACKEND DEVELOPER"] = new("backend-developer", "Backend Developer", "Engineering",
                ["BACKEND DEVELOPER", "BACKEND ENGINEER"]),
            ["FRONTEND DEVELOPER"] = new("frontend-developer", "Frontend Developer", "Engineering",
                ["FRONTEND DEVELOPER", "FRONTEND ENGINEER", "REACT DEVELOPER", "ANGULAR DEVELOPER"]),
            ["FULL STACK DEVELOPER"] = new("full-stack-developer", "Full Stack Developer", "Engineering",
                ["FULL STACK DEVELOPER", "FULLSTACK DEVELOPER", "FULL STACK ENGINEER"]),
            ["DATA ENGINEER"] = new("data-engineer", "Data Engineer", "Data & AI",
                ["DATA ENGINEER", "ETL DEVELOPER", "ETL ENGINEER"]),
            ["DATA ANALYST"] = new("data-analyst", "Data Analyst", "Data & AI",
                ["DATA ANALYST", "BI ANALYST", "BUSINESS INTELLIGENCE ANALYST"]),
            ["MACHINE LEARNING ENGINEER"] = new("machine-learning-engineer", "Machine Learning Engineer", "Data & AI",
                ["MACHINE LEARNING ENGINEER", "ML ENGINEER"]),
            ["DEVOPS ENGINEER"] = new("devops-engineer", "DevOps Engineer", "Infrastructure",
                ["DEVOPS ENGINEER", "SITE RELIABILITY ENGINEER", "SRE"]),
            ["QA ENGINEER"] = new("qa-engineer", "QA Engineer", "Quality Engineering",
                ["QA ENGINEER", "TEST ENGINEER", "AUTOMATION ENGINEER"])
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyCollection<FoundationRole> Definitions => Roles.Values;

    public static bool TryResolveRole(string title, out FoundationRole role)
    {
        var normalized = MarketExtractionPolicy.Normalize(title);
        foreach (var prefix in SeniorityPrefixes)
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        if (Roles.TryGetValue(normalized, out role!)) return true;
        foreach (var candidate in Roles.Values)
            if (candidate.Aliases.Contains(normalized, StringComparer.Ordinal))
            {
                role = candidate;
                return true;
            }
        role = default!;
        return false;
    }

    public static FoundationSkillAliasPlan PlanSkillAliases(
        IEnumerable<FoundationSkillAliasCandidate> candidates,
        IEnumerable<string> persistedNormalizedAliases)
    {
        var occupied = persistedNormalizedAliases.ToHashSet(StringComparer.Ordinal);
        var normalized = candidates.SelectMany(candidate => candidate.Aliases
            .Append(candidate.CanonicalName)
            .Select(text => new AliasCandidate(candidate.SkillId, text,
                MarketExtractionPolicy.Normalize(text),
                string.Equals(text, candidate.CanonicalName, StringComparison.Ordinal))))
            .Where(x => x.NormalizedText.Length > 0)
            .GroupBy(x => new { x.SkillId, x.NormalizedText })
            .Select(group => group.OrderByDescending(x => x.IsCanonical).ThenBy(x => x.Text,
                StringComparer.Ordinal).First())
            .ToArray();

        var selected = new List<FoundationSkillAliasSelection>();
        var duplicateSkipped = 0;
        var ambiguousSkipped = 0;
        foreach (var group in normalized.GroupBy(x => x.NormalizedText, StringComparer.Ordinal))
        {
            if (occupied.Contains(group.Key))
            {
                duplicateSkipped += group.Count();
                continue;
            }
            var canonical = group.Where(x => x.IsCanonical).ToArray();
            var owners = canonical.Length == 1 ? canonical : canonical.Length == 0 && group.Count() == 1
                ? group.ToArray() : [];
            if (owners.Length != 1)
            {
                ambiguousSkipped += group.Count();
                continue;
            }
            var owner = owners[0];
            selected.Add(new FoundationSkillAliasSelection(owner.SkillId, owner.Text, owner.NormalizedText));
        }
        return new(selected, duplicateSkipped, ambiguousSkipped);
    }

    private sealed record AliasCandidate(Guid SkillId, string Text, string NormalizedText, bool IsCanonical);
}

public sealed record FoundationRole(string Code, string Name, string Family,
    IReadOnlyList<string> Aliases);
public sealed record FoundationSkillAliasCandidate(Guid SkillId, string CanonicalName,
    IReadOnlyList<string> Aliases);
public sealed record FoundationSkillAliasSelection(Guid SkillId, string Text, string NormalizedText);
public sealed record FoundationSkillAliasPlan(IReadOnlyList<FoundationSkillAliasSelection> Selections,
    int DuplicateAliasesSkipped, int AmbiguousAliasesSkipped);
