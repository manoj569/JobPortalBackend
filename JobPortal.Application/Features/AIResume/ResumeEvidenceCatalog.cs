using System.Text.RegularExpressions;

namespace JobPortal.Application.Features.AIResume;

public static class ResumeEvidenceCatalog
{
    public static ResumeSourceEvidence[] Create(TailoredResumeContent source)
    {
        var items = new List<ResumeSourceEvidence> { new("SRC-SUMMARY-001", "summary", source.ProfessionalSummary) };
        items.Add(new("SRC-CONTACT-001", "contact", System.Text.Json.JsonSerializer.Serialize(source.Contact)));
        for (var i = 0; i < source.Skills.Length; i++) items.Add(new($"SRC-SKILL-{i + 1:D3}", "skills", source.Skills[i]));
        for (var i = 0; i < source.Experience.Length; i++)
        {
            items.Add(new($"SRC-EXP-{i + 1:D3}-IDENTITY-001", $"experienceIdentity/{i}", System.Text.Json.JsonSerializer.Serialize(new
            { source.Experience[i].Employer, source.Experience[i].Role, source.Experience[i].StartDate, source.Experience[i].EndDate })));
            for (var j = 0; j < source.Experience[i].Bullets.Length; j++)
                items.Add(new($"SRC-EXP-{i + 1:D3}-BULLET-{j + 1:D3}", $"experience/{i}", source.Experience[i].Bullets[j]));
        }
        for (var i = 0; i < source.Projects.Length; i++)
        {
            items.Add(new($"SRC-PROJECT-{i + 1:D3}-IDENTITY-001", $"projectIdentity/{i}", source.Projects[i].Name));
            for (var j = 0; j < source.Projects[i].Technologies.Length; j++)
                items.Add(new($"SRC-PROJECT-{i + 1:D3}-TECH-{j + 1:D3}", $"projectTechnologies/{i}", source.Projects[i].Technologies[j]));
            for (var j = 0; j < source.Projects[i].Bullets.Length; j++)
                items.Add(new($"SRC-PROJECT-{i + 1:D3}-BULLET-{j + 1:D3}", $"projects/{i}", source.Projects[i].Bullets[j]));
        }
        for (var i = 0; i < source.Education.Length; i++) items.Add(new($"SRC-EDU-{i + 1:D3}", "education", source.Education[i].ToString()));
        for (var i = 0; i < source.Certifications.Length; i++) items.Add(new($"SRC-CERT-{i + 1:D3}", "certifications", source.Certifications[i].ToString()));
        for (var i = 0; i < source.AdditionalInfo.Length; i++) items.Add(new($"SRC-ADDITIONAL-{i + 1:D3}", "additional", source.AdditionalInfo[i]));
        return items.ToArray();
    }
}

public static partial class ResumeClaimGuard
{
    // A deterministic guard, not a claim that semantic factual verification is mathematically complete.
    public static bool IsSupported(string text, string evidence) => IsSupported(text, [evidence]);

    public static bool IsSupported(string text, IReadOnlyCollection<string> evidenceItems)
        => RejectionCategory(text, evidenceItems) is null;

    public static string? RejectionCategory(string text, IReadOnlyCollection<string> evidenceItems)
        => RejectionCategory(text, evidenceItems, out _);

    public static string? RejectionCategory(string text, IReadOnlyCollection<string> evidenceItems, out int? claimOffset)
    {
        claimOffset = null;
        var evidence = string.Join('\n', evidenceItems);
        foreach (Match claim in Numbers().Matches(text))
            if (!evidenceItems.Any(item => Numbers().Matches(item).Any(source => NormalizeMetric(source.Value) == NormalizeMetric(claim.Value)) && SharesClaimContext(text, item)))
            { claimOffset = claim.Index; return "metric"; }
        foreach (Match claim in SensitiveClaims().Matches(text))
        {
            var word = claim.Value.ToLowerInvariant();
            var leadership = word is "led" or "leadership" or "managed" or "supervised" or "team" or "owned" or "spearheaded" or "mentored" or "architected" or "directed" or "drove";
            if (!evidenceItems.Any(item => leadership
                ? SensitiveClaims().Matches(item).Any(source => string.Equals(source.Value, claim.Value, StringComparison.OrdinalIgnoreCase))
                : item.Contains(claim.Value, StringComparison.OrdinalIgnoreCase)))
            {
                claimOffset = claim.Index;
                return leadership ? "leadership" :
                    word is "certified" or "certification" ? "certification" :
                    word is "year" or "years" ? "experience_duration" : "achievement_expertise";
            }
        }
        foreach (Match claim in TechnologyClaims().Matches(text))
            if (!evidenceItems.Any(item => TechnologyClaims().Matches(item).Any(source => string.Equals(source.Value, claim.Value, StringComparison.OrdinalIgnoreCase))))
            { claimOffset = claim.Index; return "technology"; }
        if (evidenceItems.Any(item => Negation().IsMatch(item)) && !Negation().IsMatch(text)) return "negation";
        // Preserve named technologies/entities; allow normal sentence-opening professional verbs.
        var permitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Developed", "Maintained", "Built", "Implemented", "Improved", "Optimized", "Reduced", "Delivered", "Worked", "Created", "Supported", "Collaborated", "Professional", "Experienced", "Backend", "Frontend", "REST", "APIs", "API" };
        foreach (Match token in NamedClaims().Matches(text))
            if (!permitted.Contains(token.Value) && !evidence.Contains(token.Value, StringComparison.OrdinalIgnoreCase))
            { claimOffset = token.Index; return "named_entity_or_wording"; }
        return null;
    }

    private static string NormalizeMetric(string value) => string.Concat(value.Where(x => !char.IsWhiteSpace(x)));

    private static bool SharesClaimContext(string claim, string evidence)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "improved", "improve", "increased", "increase", "reduced", "reduce", "decreased", "decrease",
            "built", "developed", "delivered", "created", "achieved", "optimized", "optimised", "by", "to",
            "from", "with", "using", "and", "the", "for", "into", "result", "results", "work", "worked"
        };
        var context = ContextWords().Matches(claim).Select(x => x.Value).Where(x => !stopWords.Contains(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ContextWords().Matches(evidence).Any(x => !stopWords.Contains(x.Value) && context.Contains(x.Value));
    }
    [GeneratedRegex(@"\d+(?:[.,]\d+)*(?:\s*%)?", RegexOptions.CultureInvariant)] private static partial Regex Numbers();
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+#.-]{3,}", RegexOptions.CultureInvariant)] private static partial Regex ContextWords();
    [GeneratedRegex(@"\b(?:led|leadership|managed|supervised|owned|spearheaded|mentored|architected|directed|drove|team|award\w*|rank\w*|certified|certification|expert|years?|million|billion)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex SensitiveClaims();
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:kubernetes|k8s|docker|aws|azure|gcp|react|angular|vue|node(?:\.js)?|python|java|c\+\+|c#|typescript|javascript|sql(?: server)?|mongodb|postgresql|mysql|redis|kafka|terraform|linux|\.net|asp\.net|tensorflow|pytorch|databricks|snowflake|oracle|elasticsearch|jenkins|github actions|gitlab)(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex TechnologyClaims();
    [GeneratedRegex(@"\b(?:no|not|never|without)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex Negation();
    [GeneratedRegex(@"(?:\.[A-Z][A-Za-z0-9]*|\b[A-Z][A-Za-z0-9+#.]*\b)", RegexOptions.CultureInvariant)] private static partial Regex NamedClaims();
}
