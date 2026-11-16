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
}

public sealed record FoundationRole(string Code, string Name, string Family,
    IReadOnlyList<string> Aliases);
