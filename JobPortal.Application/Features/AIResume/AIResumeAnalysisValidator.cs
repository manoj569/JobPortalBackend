namespace JobPortal.Application.Features.AIResume;

// Paths/reasons are backend constants. Never include provider-supplied strings in diagnostics.
public sealed record ResumeAnalysisDiagnostic(string Category, string Reason, string Path, int? Count, bool HasValue);

public static class AIResumeAnalysisValidator
{
    public static ResumeAnalysisDiagnostic[] Rejections(ResumeAnalysis? analysis, string[]? sourceSkills = null)
    {
        var errors = new List<ResumeAnalysisDiagnostic>();
        if (analysis is null) return [new("structure", "missing_analysis", "analysis", null, false)];
        if (analysis.OverallMatchScore is < 0 or > 100)
            errors.Add(new("score", "outside_0_to_100", "overallMatchScore", null, true));
        void Check(string path, string[]? items)
        {
            if (items is null) { errors.Add(new("collection", "missing_array", path, null, false)); return; }
            if (items.Length > 20) errors.Add(new("collection", "too_many_items", path, items.Length, true));
            for (var i = 0; i < items.Length; i++)
                if (items[i] is null || items[i].Length > 240 || items[i].Contains('\0'))
                    errors.Add(new("item", items[i] is null ? "null_item" : items[i].Contains('\0') ? "invalid_character" : "item_too_long",
                        $"{path}/{i}", items[i]?.Length, items[i] is not null));
        }
        Check("matchedSkills", analysis.MatchedSkills); Check("partialMatches", analysis.PartialMatches);
        Check("missingSkills", analysis.MissingSkills); Check("jobRequirements", analysis.JobRequirements);
        Check("strengths", analysis.Strengths); Check("areasToImprove", analysis.AreasToImprove); Check("suggestions", analysis.Suggestions);
        if (sourceSkills is not null && analysis.MatchedSkills is not null)
            for (var i = 0; i < analysis.MatchedSkills.Length; i++)
                if (analysis.MatchedSkills[i] is { } skill && !sourceSkills.Contains(skill, StringComparer.OrdinalIgnoreCase))
                    errors.Add(new("skill", "not_an_exact_source_skill", $"matchedSkills/{i}", null, true));
        return errors.ToArray();
    }

    public static ResumeAnalysis Normalize(ResumeAnalysis analysis, string[] skills)
    {
        // Trim formatting and restore source capitalization only. Never map aliases or invent matches.
        string[] Clean(string[] values) => values?.Select(x => x?.Trim()!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()!;
        var matched = Clean(analysis.MatchedSkills);
        if (matched is not null)
            matched = matched.Select(x => skills.FirstOrDefault(s => string.Equals(s.Trim(), x, StringComparison.OrdinalIgnoreCase)) ?? x).ToArray();
        return analysis with { MatchedSkills = matched!, PartialMatches = Clean(analysis.PartialMatches), MissingSkills = Clean(analysis.MissingSkills),
            JobRequirements = Clean(analysis.JobRequirements), Strengths = Clean(analysis.Strengths),
            AreasToImprove = Clean(analysis.AreasToImprove), Suggestions = Clean(analysis.Suggestions) };
    }
}
