using System.Text;
using System.Text.RegularExpressions;

namespace JobPortal.Application.Features.CareerAccelerator;

public static partial class MarketExtractionPolicy
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var words = Separator().Replace(value.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), " ");
        return string.Join(' ', words.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static MarketExtraction Extract(MarketJobSnapshot job, IReadOnlyList<MarketTerm> skills,
        IReadOnlyList<MarketTerm> roles)
    {
        var structured = job.StructuredSkills.Select(Normalize).Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var text = $" {Normalize(string.Join(' ', job.Title, job.Requirements,
            job.Responsibilities, job.Description))} ";
        var matched = new HashSet<Guid>();
        var structuredMatches = new HashSet<Guid>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in skills.Where(x => x.IsActive))
        {
            var terms = skill.Aliases.Append(skill.Name).Select(Normalize)
                .Where(x => x.Length > 0).Distinct(StringComparer.Ordinal);
            foreach (var term in terms)
            {
                known.Add(term);
                // Short acronyms require an explicit structured field. In prose they
                // are too ambiguous (C, Go, ML, AI).
                if (structured.Contains(term))
                { matched.Add(skill.Id); structuredMatches.Add(skill.Id); }
                else if (term.Length >= 3 && text.Contains($" {term} ", StringComparison.Ordinal))
                    matched.Add(skill.Id);
            }
        }
        var title = Normalize(job.Title);
        var role = roles.Where(x => x.IsActive)
            .Select(x => new { x.Id, Match = x.Aliases.Append(x.Name).Select(Normalize)
                .Any(alias => alias == title || StripSeniority(title) == alias) })
            .Where(x => x.Match).Take(2).ToArray();
        // Competing exact role aliases are deliberately left for review.
        var primaryRole = role.Length == 1 ? role[0].Id : (Guid?)null;
        var unknown = structured.Where(x => !known.Contains(x) && Plausible(x)).ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in SkillLines(job.Requirements))
        {
            var term = Normalize(candidate);
            if (!known.Contains(term) && Plausible(term)) unknown.Add(term);
        }
        foreach (Match mention in TechnologyMention().Matches(job.Requirements + "\n" + job.Description))
        {
            var raw = mention.Groups[1].Value;
            if (!IsDistinctiveTechnologyToken(raw)) continue;
            var term = Normalize(raw);
            if (!known.Contains(term) && !known.Any(x => x.StartsWith(term + " ", StringComparison.Ordinal)) &&
                Plausible(term)) unknown.Add(term);
        }
        return new(matched.Order().ToArray(), structuredMatches.Order().ToArray(), primaryRole,
            unknown.Order(StringComparer.Ordinal).ToArray(),
            primaryRole is null && PlausibleTitle(title) ? title : null);
    }

    private static string StripSeniority(string title) => Seniority().Replace(title, string.Empty).Trim();

    private static IEnumerable<string> SkillLines(string? requirements)
    {
        if (string.IsNullOrWhiteSpace(requirements)) yield break;
        foreach (var line in requirements.Split('\n').Take(80))
        {
            var match = SkillLine().Match(line.Trim());
            if (!match.Success) continue;
            foreach (var item in match.Groups[1].Value.Split([',', ';', '|'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(25))
                yield return item;
        }
    }

    private static bool Plausible(string term) => term.Length is >= 3 and <= 80 &&
        term.Split(' ').Length is >= 1 and <= 5 && term.Any(char.IsLetter) &&
        term.Split(' ').Any(word => !Noise().IsMatch(word));

    private static bool PlausibleTitle(string title) => title.Length is >= 5 and <= 120 &&
        title.Split(' ').Length is >= 2 and <= 8 &&
        RoleWord().IsMatch(title) && !Noise().IsMatch(title);

    private static bool IsDistinctiveTechnologyToken(string value) =>
        value.Skip(1).Any(char.IsUpper) || value.Any(x => x is '.' or '+' or '#');

    [GeneratedRegex("[^\\p{L}\\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Separator();
    [GeneratedRegex("^(?:SENIOR|JUNIOR|LEAD|PRINCIPAL|STAFF|ASSOCIATE)\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Seniority();
    [GeneratedRegex("^(?:[-*•]\\s*)?(?:REQUIRED\\s+)?(?:SKILLS?|TECHNOLOGIES|TOOLS)\\s*:\\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkillLine();
    [GeneratedRegex("^(?:THE|WE|YOU|OUR|A|AN|EXPERIENCE|REQUIRED|PREFERRED|GOOD|STRONG|KNOWLEDGE|TEAM|WORK)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Noise();
    [GeneratedRegex("\\b(?:ENGINEER|DEVELOPER|ANALYST|ARCHITECT|DESIGNER|SCIENTIST|MANAGER|SPECIALIST|CONSULTANT)\\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex RoleWord();
    [GeneratedRegex("\\b(?:experience|proficiency|knowledge|hands.on)\\s+(?:with|in|of)\\s+([A-Za-z][A-Za-z0-9.+#-]{2,39})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TechnologyMention();
}
