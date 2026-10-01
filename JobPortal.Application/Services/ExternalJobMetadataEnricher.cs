using System.Globalization;
using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Services;

// Deliberately a small grammar of explicit job assertions, not keyword scoring or NLP.
public sealed class ExternalJobMetadataEnricher : IExternalJobMetadataEnricher
{
    private const string Start = @"(?:^|[\r\n.!?;])\s*(?:[-*•]\s*)?";
    private const string Employment = @"full[- ]time|part[- ]time|contract|temporary|freelance";
    private static Regex Pattern(string expression) => new(expression,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
    private static readonly Regex EmploymentAssertion = Pattern(
        Start + @"(?:(?:this|the|our)\s+(?:is\s+)?(?:a\s+)?)?(?<value>" + Employment + @")\s+(?:position|role|job)\b|\bthis\s+(?<value>" + Employment + @")\s+(?:position|role|job)\b|" +
        Start + @"employment type\s*:\s*(?<value>" + Employment + @")\b");
    private static readonly Regex EmploymentAlternative = Pattern(@"\b(?:" + Employment + @")(?:\s+(?:position|role|job))?\s*(?:or|/)\s*(?:" + Employment + @")\b");
    private static readonly Regex InternshipTitle = Pattern(@"\b(?:summer\s+)?intern(?:ship)?\s*(?:$|[-,(])");
    private static readonly Regex InternshipAssertion = Pattern(Start + @"(?:(?:this|the)\s+is\s+(?:a|an)\s+)?internship\s*(?:position\b|role\b|[.!?;\r\n]|$)");
    private static readonly Regex ContractAssertion = Pattern(Start + @"(?:this is a\s+)?\d{1,2}[- ]months?\s+contract\b");
    private static readonly Regex WorkplaceAssertion = Pattern(Start +
        @"(?:(?:this|the)\s+(?:is\s+)?(?:a\s+)?)?(?<value>fully remote|remote|hybrid|on[- ]?site)\s+(?:position|role|job)\b");
    private static readonly Regex WorkplaceAlternative = Pattern(@"\b(?:remote|hybrid|on[- ]?site)(?:\s+(?:position|role|job))?\s*(?:or|/)\s*(?:remote|hybrid|on[- ]?site)\b");
    private static readonly Regex UncertainAssertion = Pattern(@"\b(?:not|never|no|optional|preferred|either|rather than|instead of|may be|might be|could be)\b");
    private static readonly Regex OfficeDays = Pattern(Start + @"(?:you (?:will|must) (?:work|be)\s+)?[1-4]\s+days\s+(?:per week\s+)?in (?:the )?office\b");
    private static readonly Regex RemoteRegion = Pattern(@"^remote\s*-\s*(?<location>[\p{L}][\p{L} ,.-]{1,80})$");
    private static readonly Regex RemoteCountry = Pattern(Start + @"(?:you (?:can|may|will)\s+)?work remotely from anywhere in (?<location>India|United States|United Kingdom|Canada|Australia)\s*(?:[.!?;\r\n]|$)");
    private static readonly Regex LocationAssertion = Pattern(Start +
        @"(?:(?:this role|this position|the role) is\s+)?(?:based in|based out of (?:our )?headquarters in|location\s*:)\s*(?<location>[\p{L}][\p{L} ,'-]{1,80})\s*(?:[.!?;\r\n]|$)");
    private static readonly Regex LocationUncertainty = Pattern(@"\b(?:and|or|with|where|but|team|teams|office|offices|headquarters|home|anywhere|various|multiple|locations|travel|preferred)\b");
    private static readonly Regex ExperienceAssertion = Pattern(Start +
        @"(?:(?:you (?:have|bring)|requires?|minimum|at least)\s+)?(?<min>\d{1,2})(?:(?<plus>\+)|\s*(?:-|–|to)\s*(?<max>\d{1,2}))?\s+years?\s+(?:of\s+)?(?:[\p{L}]+\s+){0,7}experience\b");
    private static readonly Regex BareRequiredYears = Pattern(@"^\s*(?<min>\d{1,2})\+\s+years?\s*[.!]?\s*$");
    private static readonly Regex EducationAssertion = Pattern(Start +
        @"(?:education\s*:\s*)?(?<value>bachelor['’]s degree|B\.E\.?|B\.Tech\.?|B\.Sc\.?|Diploma|ITI|HSC|SSC)\s+(?:is\s+)?required\b");

    public RawExternalJob Enrich(RawExternalJob normalizedJob)
    {
        ArgumentNullException.ThrowIfNull(normalizedJob);
        var text = string.Join("\n", normalizedJob.Description, normalizedJob.Requirements, normalizedJob.Responsibilities);
        // Fail closed on oversized/unexpected source text; never truncate into a misleading assertion.
        if (text.Length > 64_000 || normalizedJob.Title.Length > 250) return normalizedJob;
        try
        {
            var employment = normalizedJob.EmploymentType;
            if (!employment.HasValue)
            {
                var values = PositiveMatches(EmploymentAssertion, text).Select(m => EmploymentValue(m.Groups["value"].Value)).ToList();
                if (InternshipTitle.IsMatch(normalizedJob.Title) || PositiveMatches(InternshipAssertion, text).Any()) values.Add(EmploymentType.Internship);
                if (PositiveMatches(ContractAssertion, text).Any()) values.Add(EmploymentType.Contract);
                employment = EmploymentAlternative.IsMatch(text) ? null : Unique(values);
            }

            var workplace = normalizedJob.WorkplaceType;
            var location = normalizedJob.Location;
            var remoteRegion = RemoteRegion.Match(location ?? string.Empty);
            var remoteCountries = PositiveMatches(RemoteCountry, text).Select(m => m.Groups["location"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (!workplace.HasValue)
            {
                var values = PositiveMatches(WorkplaceAssertion, text).Select(m => WorkplaceValue(m.Groups["value"].Value)).ToList();
                if (PositiveMatches(OfficeDays, text).Any()) values.Add(WorkplaceType.Hybrid);
                if (remoteRegion.Success || remoteCountries.Length == 1) values.Add(WorkplaceType.Remote);
                workplace = WorkplaceAlternative.IsMatch(text) ? null : Unique(values);
            }
            if (remoteRegion.Success && workplace == WorkplaceType.Remote)
                location = remoteRegion.Groups["location"].Value.Trim();
            if (string.IsNullOrWhiteSpace(location))
            {
                var locations = PositiveMatches(LocationAssertion, text).Select(m => m.Groups["location"].Value.Trim())
                    .Where(v => !LocationUncertainty.IsMatch(v)).Concat(remoteCountries)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (locations.Length == 1) location = locations[0];
            }

            var min = normalizedJob.MinimumExperienceYears;
            var max = normalizedJob.MaximumExperienceYears;
            if (!min.HasValue && !max.HasValue)
            {
                var matches = PositiveMatches(ExperienceAssertion, text).ToList();
                var bare = BareRequiredYears.Match(normalizedJob.Requirements ?? string.Empty);
                if (bare.Success) matches.Add(bare);
                var ranges = matches.Select(m => (
                    Min: int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture),
                    Max: m.Groups["max"].Success ? (int?)int.Parse(m.Groups["max"].Value, CultureInfo.InvariantCulture) : null))
                    .Distinct().ToArray();
                if (ranges.Length == 1 && ranges[0].Min is >= 0 and <= 60 &&
                    (!ranges[0].Max.HasValue || ranges[0].Max is <= 60 && ranges[0].Max >= ranges[0].Min))
                    (min, max) = ranges[0];
            }

            var education = normalizedJob.EducationRequirement;
            if (string.IsNullOrWhiteSpace(education))
            {
                var values = PositiveMatches(EducationAssertion, text).Select(m => m.Groups["value"].Value)
                    .Select(v => v.StartsWith("bachelor", StringComparison.OrdinalIgnoreCase) ? "Graduate" : v)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (values.Length == 1) education = values[0];
            }

            // Reuse canonical aliases for extracted fields. No salary or expiry parsing, ever.
            return new ExternalJobNormalizer().Normalize(normalizedJob with
            {
                EmploymentType = employment, WorkplaceType = workplace, Location = location,
                MinimumExperienceYears = min, MaximumExperienceYears = max,
                ExperienceLevel = normalizedJob.ExperienceLevel ?? LevelFromMinimum(min, max),
                EducationRequirement = education
            });
        }
        catch (RegexMatchTimeoutException) { return normalizedJob; }
    }

    private static IEnumerable<Match> PositiveMatches(Regex pattern, string text)
    {
        foreach (Match match in pattern.Matches(text))
        {
            var start = match.Index;
            if (start < text.Length && Boundary(text[start])) start++;
            while (start > 0 && !Boundary(text[start - 1])) start--;
            var end = match.Index + match.Length;
            while (end < text.Length && !Boundary(text[end])) end++;
            if (!UncertainAssertion.IsMatch(text[start..end])) yield return match;
        }
    }

    private static bool Boundary(char value) => value is '.' or '!' or '?' or ';' or '\r' or '\n';

    // Numeric seniority only. Leadership/executive responsibilities cannot be inferred from years.
    internal static ExperienceLevel? LevelFromMinimum(int? min, int? max) =>
        min is null or < 0 or > 60 || max is < 0 or > 60 || max < min ? null : min switch
        {
            <= 1 => ExperienceLevel.Entry,
            2 => ExperienceLevel.Junior,
            <= 5 => ExperienceLevel.Mid,
            _ => ExperienceLevel.Senior
        };

    private static T? Unique<T>(IEnumerable<T> values) where T : struct =>
        values.Distinct().ToArray() is [var only] ? only : null;
    private static EmploymentType EmploymentValue(string value) => value.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() switch
    {
        "FULLTIME" => EmploymentType.FullTime, "PARTTIME" => EmploymentType.PartTime,
        "CONTRACT" => EmploymentType.Contract, "TEMPORARY" => EmploymentType.Temporary, _ => EmploymentType.Freelance
    };
    private static WorkplaceType WorkplaceValue(string value) => value.ToUpperInvariant() switch
    {
        "REMOTE" or "FULLY REMOTE" => WorkplaceType.Remote, "HYBRID" => WorkplaceType.Hybrid, _ => WorkplaceType.OnSite
    };
}
