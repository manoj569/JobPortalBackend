namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record TalentMatchResult(int MarketMatchScore, IReadOnlyList<string> Reasons,
    IReadOnlyList<string> SkillMatches, IReadOnlyList<string> SkillGaps);

// The market score describes overlap with observed requirements; it is never a
// Career Readiness or assessment score.
public static class TalentMatchPolicy
{
    public static TalentMatchResult Evaluate(bool roleMatches, IReadOnlyCollection<string> requiredSkills,
        IReadOnlyCollection<string> verifiedSkills)
    {
        var known = new HashSet<string>(verifiedSkills, StringComparer.OrdinalIgnoreCase);
        var required = requiredSkills.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var matched = required.Where(known.Contains).ToArray();
        var gaps = required.Where(x => !known.Contains(x)).ToArray();
        var score = required.Length == 0 ? 0 :
            (int)Math.Round(100m * matched.Length / required.Length, MidpointRounding.AwayFromZero);
        var reasons = matched.Select(x => $"Verified skill evidence: {x}")
            .Concat(gaps.Select(x => $"No verified evidence for: {x}"))
            .ToList();
        if (roleMatches) reasons.Insert(0, "Matches active career goal role");
        return new(score, reasons, matched, gaps);
    }
}
