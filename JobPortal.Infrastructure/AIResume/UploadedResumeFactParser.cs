using System.Text.RegularExpressions;
using JobPortal.Application.Features.AIResume;

namespace JobPortal.Infrastructure.AIResume;

// Conservative MVP: extract literal fields from sectioned text, never infer missing facts.
// Ambiguous identity layouts fail before analysis/payment instead of becoming model-generated facts.
public static partial class UploadedResumeFactParser
{
    public static TailoredResumeContent Parse(string text)
    {
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n')
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var current = "contact";
        foreach (var line in lines)
        {
            var section = Section(line);
            if (section is not null)
            {
                if (section is "experience" or "projects" or "education" or "certifications" &&
                    sections.TryGetValue(section, out var existing) && existing.Count > 0) existing.Add("");
                current = section;
                continue;
            }
            if (!sections.TryGetValue(current, out var values)) sections[current] = values = [];
            values.Add(line);
        }
        string[] Part(string section) => sections.TryGetValue(section, out var values) ? values.ToArray() : [];
        var header = Part("contact");
        var headerText = string.Join('\n', header);
        var name = Value(header, "Name") ?? header.FirstOrDefault(x => x.Length <= 100 &&
            !x.Contains('@') && !x.Contains(':') && !x.Contains('|') && !x.Any(char.IsDigit)) ?? "";
        var contact = new ResumeContact(name, Email().Match(headerText).Value,
            Value(header, "Phone") ?? Phone().Match(headerText).Value,
            Value(header, "Location") ?? Value(header, "Address") ?? header.Select(x => x.Split('|', StringSplitOptions.TrimEntries))
                .Where(x => x.Length == 2 && !x.Any(part => part.Contains('@') || Url().IsMatch(part)) &&
                    Phone().IsMatch(x[0]) != Phone().IsMatch(x[1]))
                .Select(x => Phone().IsMatch(x[0]) ? x[1] : x[0]).FirstOrDefault() ?? "",
            Url().Matches(headerText).Select(x => x.Value).Distinct(StringComparer.Ordinal).ToArray());
        var skills = Part("skills").SelectMany(line =>
        {
            var colon = line.IndexOf(':');
            var list = colon >= 0 ? line[(colon + 1)..] : line;
            return list.Split([',', ';', '|', '•'], StringSplitOptions.RemoveEmptyEntries).Select(CleanBullet);
        }).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var experienceEntries = Entries(Part("experience"), "experience").ToArray();
        var experience = experienceEntries.Where(fields => !fields.ContainsKey("unclassified")).Select(fields =>
            new ResumeExperience(Required(fields, "employer"), Required(fields, "role"),
                Get(fields, "startDate"), Get(fields, "endDate"), Bullets(fields))).ToArray();
        var projects = Entries(Part("projects"), "projects", skills).Select(fields =>
            new ResumeProject(Required(fields, "name"), Get(fields, "technologies").Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).ToArray(), Bullets(fields))).ToArray();
        var education = Entries(Part("education"), "education").Select(fields =>
            new ResumeEducation(Required(fields, "institution"), Required(fields, "qualification"),
                Get(fields, "startDate"), Get(fields, "endDate"))).ToArray();
        var certifications = Entries(Part("certifications"), "certifications").Select(fields =>
            new ResumeCertification(Required(fields, "name"), Get(fields, "issuer"), Get(fields, "date"))).ToArray();
        var result = new TailoredResumeContent(contact, string.Join(' ', Part("summary")), skills,
            experience, projects, education, certifications, Part("additional").Concat(experienceEntries
                .Where(fields => fields.ContainsKey("unclassified")).SelectMany(fields => Get(fields, "bullets")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries))).ToArray());
        if (!AIResumeContentGuard.WellFormed(result) ||
            skills.Length + experience.Length + projects.Length + education.Length + certifications.Length == 0)
            throw InvalidLayout();
        return result;
    }

    private static IEnumerable<Dictionary<string, string>> Entries(string[] lines, string section, IReadOnlyCollection<string>? sourceSkills = null)
    {
        Dictionary<string, string>? fields = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                if (fields is not null) yield return fields;
                fields = null;
                continue;
            }
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            var technologyTokens = line.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (section == "projects" && fields is not null && !IsBullet(line) && technologyTokens.Length > 1 &&
                sourceSkills is not null && technologyTokens.All(token => sourceSkills.Contains(token, StringComparer.Ordinal)))
            {
                fields["technologies"] = string.Join(", ", technologyTokens);
                continue;
            }
            var identityWithDates = IdentityWithDates().Match(line);
            if (section == "experience" && parts.Length <= 2 && identityWithDates.Success && index + 1 < lines.Length &&
                RoleWords().IsMatch(lines[index + 1].Split(',')[0]) && lines[index + 1].Length <= 250)
            {
                if (fields is not null) yield return fields;
                fields = new(StringComparer.Ordinal)
                {
                    ["employer"] = identityWithDates.Groups["identity"].Value.Trim().TrimEnd('|', '–', '—', '-').Trim(),
                    ["role"] = lines[++index].Split(',')[0].Trim()
                };
                Dates(fields, identityWithDates.Groups["dates"].Value);
                continue;
            }
            var degreeWithYear = DegreeWithYear().Match(line);
            if (section == "education" && parts.Length == 1 && degreeWithYear.Success && InstitutionWords().IsMatch(degreeWithYear.Groups["degree"].Value) &&
                index + 1 < lines.Length && DegreeWords().IsMatch(lines[index + 1]))
            {
                if (fields is not null) yield return fields;
                fields = new(StringComparer.Ordinal)
                {
                    ["institution"] = degreeWithYear.Groups["degree"].Value.Trim().TrimEnd('|', '–', '—', '-').Trim(),
                    ["endDate"] = degreeWithYear.Groups["year"].Value,
                    ["qualification"] = lines[++index]
                };
                continue;
            }
            if (section == "education" && parts.Length == 1 && degreeWithYear.Success && DegreeWords().IsMatch(degreeWithYear.Groups["degree"].Value) &&
                index + 1 < lines.Length && InstitutionWords().IsMatch(lines[index + 1]))
            {
                if (fields is not null) yield return fields;
                fields = new(StringComparer.Ordinal)
                {
                    ["qualification"] = degreeWithYear.Groups["degree"].Value.Trim().TrimEnd('|', '–', '—', '-').Trim(),
                    ["endDate"] = degreeWithYear.Groups["year"].Value,
                    ["institution"] = lines[++index]
                };
                continue;
            }
            if (section == "experience" && parts.Length <= 2 && identityWithDates.Success)
            {
                // Retain ambiguous/training timeline entries as literal additional evidence.
                // They cannot authorize an employer/role/date tuple or a scoped experience bullet.
                if (fields is not null) yield return fields;
                fields = new(StringComparer.Ordinal) { ["unclassified"] = "true", ["bullets"] = line + "\n" };
                continue;
            }
            if (section == "projects" && parts.Length == 2 && !IsBullet(line))
            {
                if (fields is not null) yield return fields;
                fields = new(StringComparer.Ordinal) { ["name"] = parts[0], ["technologies"] = parts[1] };
                continue;
            }
            if (parts.Length == 3 && section is "experience" or "education" or "certifications" && !line.Contains(':'))
            {
                if (fields is not null) yield return fields;
                fields = new(StringComparer.Ordinal);
                if (section == "certifications")
                { fields["name"] = parts[0]; fields["issuer"] = parts[1]; fields["date"] = parts[2]; }
                else
                {
                    fields[section == "experience" ? "role" : "qualification"] = parts[0];
                    fields[section == "experience" ? "employer" : "institution"] = parts[1];
                    Dates(fields, parts[2]);
                }
                continue;
            }
            var colon = line.IndexOf(':');
            var label = colon > 0 ? line[..colon].Trim().ToLowerInvariant() : "";
            var key = Field(label, section);
            if (key is not null)
            {
                var value = line[(colon + 1)..].Trim();
                if (fields is not null && fields.ContainsKey(key)) { yield return fields; fields = null; }
                fields ??= new(StringComparer.Ordinal);
                if (key == "dates") Dates(fields, value); else fields[key] = value;
                continue;
            }
            var at = section == "experience" ? RoleAtEmployer().Match(line) : Match.Empty;
            if (at.Success)
            {
                if (fields is not null) yield return fields;
                fields = new(StringComparer.Ordinal) { ["role"] = at.Groups["role"].Value.Trim(), ["employer"] = at.Groups["employer"].Value.Trim() };
                continue;
            }
            if (fields is not null && DateRange().IsMatch(line)) { Dates(fields, line); continue; }
            if (section == "projects" && fields is null && !IsBullet(line))
            {
                fields = new(StringComparer.Ordinal) { ["name"] = line };
                continue;
            }
            if (fields is null || section is "education" or "certifications") throw InvalidLayout($"unrecognized_{section}_entry_at_line_{index}");
            fields["bullets"] = Get(fields, "bullets") + line + "\n";
        }
        if (fields is not null) yield return fields;
    }

    private static string? Field(string label, string section) => label switch
    {
        "employer" or "company" when section == "experience" => "employer",
        "role" or "title" or "job title" when section == "experience" => "role",
        "project" or "name" when section == "projects" => "name",
        "technologies" or "tech stack" when section == "projects" => "technologies",
        "institution" or "university" or "college" when section == "education" => "institution",
        "degree" or "qualification" when section == "education" => "qualification",
        "certification" or "certificate" or "name" when section == "certifications" => "name",
        "issuer" when section == "certifications" => "issuer",
        "date" when section == "certifications" => "date",
        "dates" or "duration" or "period" => "dates",
        "start date" => "startDate", "end date" => "endDate",
        _ => null
    };

    private static void Dates(Dictionary<string, string> fields, string value)
    {
        var match = DateRange().Match(value);
        if (!match.Success) throw InvalidLayout("unrecognized_date_range");
        fields["startDate"] = match.Groups["start"].Value.Trim();
        fields["endDate"] = match.Groups["end"].Value.Trim();
    }
    private static string Get(Dictionary<string, string> fields, string key) => fields.GetValueOrDefault(key, "");
    private static string Required(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw InvalidLayout("missing_" + key);
    private static string[] Bullets(Dictionary<string, string> fields) => Get(fields, "bullets").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(CleanBullet).ToArray();
    private static bool IsBullet(string value) => value.StartsWith('-') || value.StartsWith('•') || value.StartsWith('*');
    private static string CleanBullet(string value) => value.Trim().TrimStart('-', '•', '*').Trim();
    private static string? Value(string[] lines, string key) => lines.FirstOrDefault(x => x.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..].Trim();
    private static InvalidDataException InvalidLayout(string reason = "ambiguous_layout") => new("The uploaded resume layout could not be safely parsed into factual source entries: " + reason);
    private static string? Section(string line) => line.Trim().TrimEnd(':').ToLowerInvariant() switch
    {
        "summary" or "professional summary" or "profile summary" or "professional profile" or "profile" or "objective" or "career objective" or "career overview" or "career summary" => "summary",
        "skills" or "technical skills" or "core skills" or "skills & technologies" or "technical expertise" or "technical proficiency" => "skills",
        "experience" or "work experience" or "professional experience" or "employment history" => "experience",
        "projects" or "personal projects" or "selected projects" or "project experience" => "projects",
        "education" or "academic qualifications" or "educational qualifications" or "academic background" or "educational background" => "education",
        "certifications" or "certificates" => "certifications",
        "additional information" or "additional info" => "additional",
        "contact" or "contact information" => "contact",
        _ => null
    };
    [GeneratedRegex(@"^(?<start>(?:(?:(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+)?(?:19|20)\d{2}(?:[-/]\d{2})?))\s*(?:\s[-–—]\s|\s+to\s+|(?<=\d)[–—-](?=\d))\s*(?<end>(?:(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+)?(?:19|20)\d{2}(?:[-/]\d{2})?|Present|Current)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateRange();
    [GeneratedRegex(@"^(?<identity>.+?)\s+(?<dates>(?:(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+)?(?:19|20)\d{2}(?:[-/]\d{2})?\s*(?:[-–—]|to)\s*(?:(?:(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+)?(?:19|20)\d{2}(?:[-/]\d{2})?|Present|Current))\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IdentityWithDates();
    [GeneratedRegex(@"^(?<degree>.+?)\s+(?<year>(?:19|20)\d{2})\s*$", RegexOptions.CultureInvariant)] private static partial Regex DegreeWithYear();
    [GeneratedRegex(@"\b(?:engineer|developer|architect|consultant|manager|analyst|lead|specialist|intern|designer|officer|accountant|programmer|trainer|administrator|freelancer)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex RoleWords();
    [GeneratedRegex(@"\b(?:bachelor|master|b\.?tech|m\.?tech|b\.?e\.?|m\.?e\.?|b\.?sc|m\.?sc|bca|mca|mba|ph\.?d|degree|diploma)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DegreeWords();
    [GeneratedRegex(@"\b(?:university|college|institute|school|academy)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex InstitutionWords();
    [GeneratedRegex(@"^(?<role>[^|]+)\s+at\s+(?<employer>[^|]+)$", RegexOptions.CultureInvariant)] private static partial Regex RoleAtEmployer();
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)] private static partial Regex Email();
    [GeneratedRegex(@"\+?\d[\d ()-]{7,}\d", RegexOptions.CultureInvariant)] private static partial Regex Phone();
    [GeneratedRegex(@"https?://[^\s|]+", RegexOptions.CultureInvariant)] private static partial Regex Url();
}
