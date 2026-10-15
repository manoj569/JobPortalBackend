using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Services;

public interface IJobClassificationService
{
    JobClassificationResult Analyze(JobClassificationInput input);
}

public sealed record JobClassificationInput(string Title, string? Description, string? Location = null,
    EmploymentType? EmploymentType = null, WorkplaceType? WorkplaceType = null,
    int? MinimumExperienceYears = null, int? MaximumExperienceYears = null,
    decimal? MinimumSalary = null, decimal? MaximumSalary = null, string? CurrencyCode = null,
    SalaryPeriod? SalaryPeriod = null, string? Provider = null);
public sealed record ClassificationEvidence(string Field, string Source, string Text, double Confidence);
public sealed record JobClassificationResult(EmploymentType EmploymentType, WorkplaceType WorkplaceType,
    int? MinimumExperienceYears, int? MaximumExperienceYears, decimal? MinimumSalary, decimal? MaximumSalary,
    string? CurrencyCode, SalaryPeriod? SalaryPeriod, string? DescriptionLocation,
    IReadOnlyList<ClassificationEvidence> Evidence, IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> ReviewFields, string Version = "jd-rules-v1", bool Historical = false, string? TitleLocation = null)
{
    public IReadOnlyDictionary<string, double> Confidence => JobClassificationPolicy.Fields.ToDictionary(field => field,
        field => ReviewFields.Contains(field, StringComparer.Ordinal) ? 0 : Evidence.Where(e => e.Field == field).Select(e => e.Confidence).DefaultIfEmpty(0).Min(), StringComparer.Ordinal);
    public string? ExperienceFilterKey => MinimumExperienceYears is { } minimum ?
        JobPortal.Application.Features.PublicJobs.ExperienceFilterPolicy.Ranges.FirstOrDefault(range => minimum >= range.MinimumInclusive &&
            (!range.MaximumInclusive.HasValue || minimum <= range.MaximumInclusive))?.Key : null;
}

/// <summary>Conservative deterministic suggestions. Never changes identities, geography eligibility or URLs.</summary>
public sealed class JobClassificationService : IJobClassificationService
{
    // Avoid per-pattern compiled-runner JIT inside the first match's timeout window.
    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(500));
    private static readonly Regex Negation = Rx(@"\b(?:not|no)(?: an?)?\s+$");
    private static readonly Regex PreferredHeading = Rx(@"^\s*(?:preferred|desired|nice to have)\s*(?:qualifications|requirements|skills)?\s*:?\s*$");
    private static readonly Regex RequiredHeading = Rx(@"^\s*(?:required|basic|minimum)\s*(?:qualifications|requirements|skills)\s*:?\s*$");
    private static readonly Regex RequiredSignal = Rx(@"\b(?:at least|minimum|more than)\b");
    private static readonly Regex SupplementalCompensation = Rx(@"\b(sign(?:ing|[- ]on)? bonus|bonus|benefits?|equity|reimbursement)\b");
    private static readonly Regex BaseSalary = Rx(@"\b(salary|base pay|compensation)\b");
    private static readonly Regex TitleCity = Rx(@"\b(Hyderabad|Bengaluru|Bangalore|Mumbai|Pune|Chennai|Delhi|Gurugram|Gurgaon|Noida|Kolkata)\b");
    private static readonly Regex Sentences = Rx(@"[\n!?;]+|\.(?=\s|$)");
    private static readonly Regex Years = Rx(@"(?<![\d.])(?:(?<minimum>minimum|at least|more than|over)\s+)?(?<min>\d{1,2})(?:\s*(?:[-–—]|to)\s*(?<max>\d{1,2}))?\s*(?<plus>\+)?\s*year(?:\(s\)|s)?\b");
    private static readonly Regex Freshers = Rx(@"\b(?:no (?:prior )?experience (?:is )?required|freshers (?:can apply|welcome)|fresh graduates (?:can apply|welcome))\b");
    private static readonly Regex Preferred = Rx(@"\b(preferred|preferably|desirable|nice to have|bonus|plus|advantage)\b");
    private static readonly Regex Experience = Rx(@"\b(experience|experienced)\b");
    private static readonly Regex Education = Rx(@"\b(education|degree|course|study|studies|established|founded|history)\b");
    private static readonly Regex Location = Rx(@"\b(?:based (?:at|in)|job location\s*[:\-]|location\s*:)\s*(?:our\s+)?(?<city>Hyderabad|Bengaluru|Bangalore|Mumbai|Pune|Chennai|Delhi|Gurugram|Gurgaon|Noida|Kolkata)\b");
    private static readonly Regex OfficeSignal = Rx(@"\bbased at our\s+\w+\s+office\b");
    private static readonly char[] CurrencySymbols = ['₹', '$', '£', '€'];
    private static readonly Regex Salary = new(@"(?<currency>₹|INR|USD|\$|GBP|£|EUR|€)\s*(?<min>\d[\d,]*(?:\.\d+)?)\s*(?:(?:[-–—]|to)\s*(?:₹|INR|USD|\$|GBP|£|EUR|€)?\s*(?<max>\d[\d,]*(?:\.\d+)?))?\s*(?<unit>LPA|lakhs?(?: per annum)?|k)?\s*(?<period>per month|monthly|per year|yearly|annually|annual|per annum|per hour|hourly)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    private static readonly (EmploymentType Value, Regex Pattern)[] EmploymentRules =
    [
        (EmploymentType.FullTime, Rx(@"\bfull[- ]time\s+(?:opportunity|position|role|employment|job)|\b(?:employment|job) type\s*:\s*full[- ]time\b")),
        (EmploymentType.PartTime, Rx(@"\bpart[- ]time\s+(?:opportunity|position|role|employment|job)\b")),
        (EmploymentType.Contract, Rx(@"\b\d+[- ]months?\s+contract\b|\bcontract(?:or)?\s+(?:position|role|employment|job)\b|\bemployment type\s*:\s*contract\b")),
        (EmploymentType.Internship, Rx(@"\b(?:summer )?internship\b")),
        (EmploymentType.Freelance, Rx(@"\bfreelance\s+(?:developer|designer|position|role|job|engineer)\b")),
        (EmploymentType.Temporary, Rx(@"\btemporary\s+(?:position|role|employment|job)\b"))
    ];
    private static readonly (WorkplaceType Value, Regex Pattern)[] WorkRules =
    [
        (WorkplaceType.OnSite, Rx(@"\b(?:work(?:ing)? from (?:the )?office|WFO|on[- ]?site|office[- ]based|mandatory office attendance)\b")),
        (WorkplaceType.Remote, Rx(@"\b(?:work from home|WFH|fully remote|100% remote|remote within\s+\w+)\b")),
        (WorkplaceType.Hybrid, Rx(@"\bhybrid\s+(?:work model|schedule|work|role|position)\b|\b\w+ days (?:in (?:the )?)?office and \w+ days remote\b"))
    ];

    public JobClassificationResult Analyze(JobClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        // Reuse the established HTML parser, including script/style suppression.
        var text = new ExternalJobNormalizer().Normalize(new RawExternalJob
        { Title = input.Title, Description = input.Description is { Length: > 100_000 } ? input.Description[..100_000] : input.Description, DescriptionIsHtml = true }).Description ?? "";
        if (text.Length > 100_000) text = text[..100_000];
        var evidence = new List<ClassificationEvidence>();
        var conflicts = new List<string>();
        var review = new HashSet<string>(StringComparer.Ordinal);
        void Add(string field, string source, string value, double confidence = .95) => evidence.Add(new(field, source, value, confidence));
        T Select<T>(string field, T? structured, (T Value, Regex Pattern)[] rules) where T : struct, Enum
        {
            var matches = rules.Select(r => (r.Value, Match: r.Pattern.Match(text)))
                .Where(r => r.Match.Success && !Negation.IsMatch(text[..r.Match.Index])).ToArray();
            foreach (var m in matches) Add(field, "Description", m.Match.Value);
            var distinct = matches.Select(m => m.Value).Distinct().ToArray();
            if (distinct.Length > 1 || (structured.HasValue && Convert.ToInt32(structured.Value, CultureInfo.InvariantCulture) != 0 && distinct.Any(v => !v.Equals(structured.Value))))
            { conflicts.Add(field + ": conflicting evidence"); review.Add(field); }
            if (structured.HasValue && Enum.IsDefined(structured.Value) && Convert.ToInt32(structured.Value, CultureInfo.InvariantCulture) != 0)
            { Add(field, "Provider", structured.Value.ToString(), 1); return structured.Value; }
            return distinct.Length == 1 ? distinct[0] : default;
        }
        var employment = Select("EmploymentType", input.EmploymentType, EmploymentRules);
        var workplace = Select("WorkplaceType", input.WorkplaceType, WorkRules);
        var experienceRanges = new List<(int Min, int? Max)>();
        var preferredSection = false;
        foreach (var sentence in Sentences.Split(text))
        {
            if (PreferredHeading.IsMatch(sentence)) { preferredSection = true; continue; }
            if (RequiredHeading.IsMatch(sentence)) { preferredSection = false; continue; }
            if (preferredSection || Preferred.IsMatch(sentence)) { if (Years.IsMatch(sentence)) Add("PreferredExperience", "Description", sentence, .7); continue; }
            if (Education.IsMatch(sentence) || !Experience.IsMatch(sentence) && !RequiredSignal.IsMatch(sentence)) continue;
            foreach (Match match in Years.Matches(sentence))
            {
                var min = int.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture);
                int? max = match.Groups["max"].Success ? int.Parse(match.Groups["max"].Value, CultureInfo.InvariantCulture) : null;
                // More than X is a strict bound; whole-year contract represents it as X+1.
                if (match.Groups["minimum"].Value.Equals("more than", StringComparison.OrdinalIgnoreCase) || match.Groups["minimum"].Value.Equals("over", StringComparison.OrdinalIgnoreCase)) min++;
                if (min > 60 || max > 60 || max < min) { review.Add("Experience"); continue; }
                experienceRanges.Add((min, max)); Add("Experience", "Description", match.Value);
            }
        }
        if (Freshers.IsMatch(text)) { if (!experienceRanges.Any(r => r.Min == 0)) experienceRanges.Add((0, null)); Add("Experience", "Description", Freshers.Match(text).Value); }
        var ranges = experienceRanges.Distinct().ToArray();
        if (ranges.Length > 1) { conflicts.Add("Experience: multiple required ranges; review total versus skill experience"); review.Add("Experience"); }
        int? minimumExperience = ranges.Length == 1 ? ranges[0].Min : null;
        int? maximumExperience = ranges.Length == 1 ? ranges[0].Max : null;
        if (input.MinimumExperienceYears < 0 || input.MaximumExperienceYears < 0 || input.MinimumExperienceYears > 60 || input.MaximumExperienceYears > 60 || input.MinimumExperienceYears > input.MaximumExperienceYears)
        { review.Add("Experience"); conflicts.Add("Experience: invalid structured range"); }
        else if (input.MinimumExperienceYears.HasValue || input.MaximumExperienceYears.HasValue)
        {
            if (ranges.Length > 0 && (minimumExperience != input.MinimumExperienceYears || maximumExperience != input.MaximumExperienceYears))
            { conflicts.Add("Experience: provider and description differ"); review.Add("Experience"); }
            minimumExperience = input.MinimumExperienceYears; maximumExperience = input.MaximumExperienceYears;
            Add("Experience", "Provider", $"{minimumExperience}..{maximumExperience}", 1);
        }
        var salaryRanges = new List<(decimal Min, decimal? Max, string Currency, SalaryPeriod? Period)>();
        foreach (var sentence in Sentences.Split(text))
        {
            // Every supported salary disclosure begins with one of these currency tokens.
            if (sentence.IndexOfAny(CurrencySymbols) < 0 &&
                !sentence.Contains("INR", StringComparison.OrdinalIgnoreCase) &&
                !sentence.Contains("USD", StringComparison.OrdinalIgnoreCase) &&
                !sentence.Contains("GBP", StringComparison.OrdinalIgnoreCase) &&
                !sentence.Contains("EUR", StringComparison.OrdinalIgnoreCase)) continue;
            if (SupplementalCompensation.IsMatch(sentence)) continue;
            foreach (Match m in Salary.Matches(sentence))
            {
                var unit = m.Groups["unit"].Value.ToUpperInvariant();
                var multiplier = unit == "LPA" || unit.StartsWith("LAKH", StringComparison.Ordinal) ? 100000m : unit == "K" ? 1000m : 1m;
                if (!decimal.TryParse(m.Groups["min"].Value.Replace(",", "", StringComparison.Ordinal), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedMinimum) || parsedMinimum > 1_000_000_000) { review.Add("Salary"); continue; }
                decimal? max = null;
                if (m.Groups["max"].Success)
                {
                    if (!decimal.TryParse(m.Groups["max"].Value.Replace(",", "", StringComparison.Ordinal), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedMaximum) || parsedMaximum > 1_000_000_000) { review.Add("Salary"); continue; }
                    max = parsedMaximum * multiplier;
                }
                var min = parsedMinimum * multiplier;
                var currency = m.Groups["currency"].Value.ToUpperInvariant() switch { "₹" or "INR" => "INR", "$" or "USD" => "USD", "£" or "GBP" => "GBP", _ => "EUR" };
                SalaryPeriod? period = unit == "LPA" || unit.StartsWith("LAKH", StringComparison.Ordinal) ? JobPortal.Domain.Enums.SalaryPeriod.Yearly : m.Groups["period"].Value.ToLowerInvariant() switch
                { "per month" or "monthly" => JobPortal.Domain.Enums.SalaryPeriod.Monthly, "per hour" or "hourly" => JobPortal.Domain.Enums.SalaryPeriod.Hourly, "" => null, _ => JobPortal.Domain.Enums.SalaryPeriod.Yearly };
                if (min > 1_000_000_000 || max > 1_000_000_000 || max < min || !period.HasValue && !BaseSalary.IsMatch(sentence)) continue;
                salaryRanges.Add((min, max, currency, period)); Add("Salary", "Description", m.Value);
            }
        }
        var salaries = salaryRanges.Distinct().ToArray();
        if (salaries.Length > 1) { conflicts.Add("Salary: multiple disclosures"); review.Add("Salary"); }
        decimal? salaryMin = salaries.Length == 1 ? salaries[0].Min : null;
        decimal? salaryMax = salaries.Length == 1 ? salaries[0].Max : null;
        string? salaryCurrency = salaries.Length == 1 ? salaries[0].Currency : null;
        SalaryPeriod? salaryPeriod = salaries.Length == 1 ? salaries[0].Period : null;
        if (input.MinimumSalary < 0 || input.MaximumSalary < 0 || input.MinimumSalary > input.MaximumSalary)
        { review.Add("Salary"); conflicts.Add("Salary: invalid structured range"); }
        else if (input.MinimumSalary.HasValue || input.MaximumSalary.HasValue)
        {
            if (salaries.Length > 0 && (salaryMin != input.MinimumSalary || salaryMax != input.MaximumSalary || salaryCurrency != input.CurrencyCode || salaryPeriod != input.SalaryPeriod))
            { review.Add("Salary"); conflicts.Add("Salary: provider and description differ"); }
            salaryMin = input.MinimumSalary; salaryMax = input.MaximumSalary; salaryCurrency = input.CurrencyCode; salaryPeriod = input.SalaryPeriod; Add("Salary", "Provider", $"{salaryMin}..{salaryMax} {salaryCurrency} {salaryPeriod}", 1); }
        if ((salaryMin.HasValue || salaryMax.HasValue) && (salaryCurrency is null || salaryPeriod is null)) review.Add("Salary");
        var descriptionCities = Location.Matches(text).Select(m => m.Groups["city"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var titleCities = TitleCity.Matches(input.Title).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var cities = descriptionCities.Concat(titleCities).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var city in descriptionCities) Add("Location", "Description", city);
        foreach (var city in titleCities) Add("Location", "Title", city);
        if (OfficeSignal.IsMatch(text)) { Add("WorkplaceSignal", "Description", OfficeSignal.Match(text).Value, .6); review.Add("WorkplaceType"); }
        if (input.Location is not null) Add("Location", "Provider", input.Location, 1);
        if (cities.Length > 1 || cities.Any(city => input.Location is not null && !input.Location.Contains(city, StringComparison.OrdinalIgnoreCase)))
        { conflicts.Add("Location: provider/title/description locations require review"); review.Add("Location"); }
        return new(employment, workplace, minimumExperience, maximumExperience, salaryMin, salaryMax, salaryCurrency, salaryPeriod,
            descriptionCities.Length == 1 ? descriptionCities[0] : null, evidence, conflicts, review.Order(StringComparer.Ordinal).ToArray(), TitleLocation: titleCities.Length == 1 ? titleCities[0] : null);
    }
}

/// <summary>Accepted values and suggestions are separate. Overrides are field groups, including explicit nulls.</summary>
public static class JobClassificationPolicy
{
    public static readonly string[] Fields = ["EmploymentType", "WorkplaceType", "Experience", "Salary", "Location"];
    public static string[] Overrides(Job job) => job.ManualClassificationOverrides?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
    public static JobClassificationResult Analyze(Job job) => new JobClassificationService().Analyze(new(job.Title,
        job.Description + "\n" + job.Requirements + "\n" + job.Responsibilities, job.Location));
    public static void Apply(Job job, JobClassificationResult result, bool historical = false)
    {
        if (job.ClassificationJson is not null)
        {
            try { var prior = JsonSerializer.Deserialize<JobClassificationResult>(job.ClassificationJson); historical = prior is null || prior.Historical || prior.Version is null || !prior.Version.StartsWith("jd-rules-", StringComparison.Ordinal); }
            catch (JsonException) { historical = true; }
        }
        var locked = Overrides(job);
        bool Allowed(string field, bool empty) => !locked.Contains(field, StringComparer.Ordinal) &&
            !result.ReviewFields.Contains(field, StringComparer.Ordinal) && (!historical || empty);
        if (Allowed("EmploymentType", job.EmploymentType == EmploymentType.Unknown)) job.EmploymentType = result.EmploymentType;
        if (Allowed("WorkplaceType", job.WorkplaceType == WorkplaceType.Unknown)) job.WorkplaceType = result.WorkplaceType;
        if (Allowed("Experience", job.MinimumExperienceYears is null && job.MaximumExperienceYears is null))
        { job.MinimumExperienceYears = result.MinimumExperienceYears; job.MaximumExperienceYears = result.MaximumExperienceYears; }
        if (Allowed("Salary", job.MinimumSalary is null && job.MaximumSalary is null && job.CurrencyCode is null))
        { job.MinimumSalary = result.MinimumSalary; job.MaximumSalary = result.MaximumSalary; job.CurrencyCode = result.CurrencyCode; job.SalaryPeriod = result.SalaryPeriod; }
        // Location contributes to fingerprint and India eligibility. Suggestions only.
        var review = result.ReviewFields.ToHashSet(StringComparer.Ordinal);
        if ((result.DescriptionLocation is not null || result.TitleLocation is not null) && job.Location is null) review.Add("Location");
        if (historical)
        {
            if (job.EmploymentType != result.EmploymentType && job.EmploymentType != EmploymentType.Unknown) review.Add("EmploymentType");
            if (job.WorkplaceType != result.WorkplaceType && job.WorkplaceType != WorkplaceType.Unknown) review.Add("WorkplaceType");
            if (job.MinimumExperienceYears != result.MinimumExperienceYears || job.MaximumExperienceYears != result.MaximumExperienceYears) review.Add("Experience");
            if (job.CurrencyCode != result.CurrencyCode || job.SalaryPeriod != result.SalaryPeriod ||
                job.MinimumSalary != result.MinimumSalary || job.MaximumSalary != result.MaximumSalary) review.Add("Salary");
        }
        // Historical provenance must stay uncertain on later passes; recording suggestions is not ownership.
        job.ClassificationJson = JsonSerializer.Serialize(result with { ReviewFields = review.Order(StringComparer.Ordinal).ToArray(), Historical = historical });
    }
}
