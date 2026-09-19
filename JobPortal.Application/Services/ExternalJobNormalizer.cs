using System.Net;
using System.Text;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Services;

public sealed class ExternalJobNormalizer : IExternalJobNormalizer
{
    public RawExternalJob Normalize(RawExternalJob rawJob)
    {
        ArgumentNullException.ThrowIfNull(rawJob);
        return rawJob with
        {
            Title = NormalizeText(rawJob.Title) ?? string.Empty,
            CompanyName = NormalizeText(rawJob.CompanyName) ?? string.Empty,
            Location = NormalizeLocation(rawJob.Location),
            Description = NormalizeParagraphs(rawJob.DescriptionIsHtml ? HtmlToText(rawJob.Description) : rawJob.Description),
            DescriptionIsHtml = false,
            Requirements = NormalizeParagraphs(rawJob.Requirements),
            Responsibilities = NormalizeParagraphs(rawJob.Responsibilities),
            Benefits = NormalizeParagraphs(rawJob.Benefits),
            ApplicationUrl = Trim(rawJob.ApplicationUrl),
            ExternalId = Trim(rawJob.ExternalId),
            ExternalCategory = NormalizeText(rawJob.ExternalCategory),
            EmploymentTypeText = NormalizeText(rawJob.EmploymentTypeText),
            WorkplaceTypeText = NormalizeText(rawJob.WorkplaceTypeText),
            EmploymentType = rawJob.EmploymentType is { } employment && Enum.IsDefined(employment)
                ? employment : MapEmployment(rawJob.EmploymentTypeText),
            WorkplaceType = rawJob.WorkplaceType is { } workplace && Enum.IsDefined(workplace)
                ? workplace : MapWorkplace(rawJob.WorkplaceTypeText)
        };
    }

    public static string? NormalizeText(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeLocation(string? value)
    {
        var text = NormalizeText(value);
        if (text is null) return null;
        return Trim(string.Join(", ", text.Split(',').Select(Trim).Where(x => x is not null)));
    }

    private static string TypeKey(string? value) => (NormalizeText(value) ?? string.Empty)
        .Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static EmploymentType? MapEmployment(string? value) => TypeKey(value) switch
    {
        "FULLTIME" => EmploymentType.FullTime,
        "PARTTIME" => EmploymentType.PartTime,
        "CONTRACT" or "CONTRACTOR" => EmploymentType.Contract,
        "INTERN" or "INTERNSHIP" => EmploymentType.Internship,
        "FREELANCE" => EmploymentType.Freelance,
        "TEMPORARY" => EmploymentType.Temporary,
        _ => null
    };

    private static WorkplaceType? MapWorkplace(string? value) => TypeKey(value) switch
    {
        "REMOTE" => WorkplaceType.Remote,
        "HYBRID" => WorkplaceType.Hybrid,
        "ONSITE" or "OFFICE" => WorkplaceType.OnSite,
        _ => null
    };

    private static string? NormalizeParagraphs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var lines = value.ReplaceLineEndings("\n").Split('\n').Select(NormalizeText);
        var result = new StringBuilder();
        var blank = false;
        foreach (var line in lines)
        {
            if (line is null) { blank = result.Length > 0; continue; }
            if (result.Length > 0) result.Append(blank ? "\n\n" : "\n");
            result.Append(line);
            blank = false;
        }
        return Trim(result.ToString());
    }

    // Text extraction only, never HTML sanitization or rendering. Quoted attributes
    // cannot end a tag; unclosed script/style elements suppress their remaining body.
    private static string? HtmlToText(string? value)
    {
        if (value is null) return null;
        // Greenhouse may encode the entire HTML fragment. Decode that envelope
        // only when there are no literal tags; entities inside real HTML remain
        // text, so a literal List&lt;T&gt; is not mistaken for a tag.
        var html = value;
        if (!value.Contains('<') && EncodedHtmlTags.Any(tag =>
            value.Contains($"&lt;{tag}&gt;", StringComparison.OrdinalIgnoreCase) ||
            value.Contains($"&lt;{tag} ", StringComparison.OrdinalIgnoreCase)))
            html = WebUtility.HtmlDecode(value);
        var result = new StringBuilder(html.Length);
        string? suppressed = null;
        for (var i = 0; i < html.Length;)
        {
            if (html.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal))
            {
                var endComment = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = endComment < 0 ? html.Length : endComment + 3;
                continue;
            }
            if (html[i] != '<' || i + 1 == html.Length ||
                !(char.IsLetter(html[i + 1]) || html[i + 1] is '/' or '!' or '?'))
            {
                if (suppressed is null) result.Append(html[i]);
                i++;
                continue;
            }
            var start = i + 1;
            var closing = html[start] == '/';
            if (closing) start++;
            var nameEnd = start;
            while (nameEnd < html.Length && char.IsLetterOrDigit(html[nameEnd])) nameEnd++;
            var name = html[start..nameEnd].ToUpperInvariant();
            var end = nameEnd;
            var quote = '\0';
            for (; end < html.Length; end++)
            {
                var c = html[end];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c is '\'' or '"') quote = c;
                else if (c == '>') break;
            }
            if (end == html.Length) break;
            i = end + 1;
            if (suppressed is not null)
            {
                if (closing && name == suppressed) { suppressed = null; result.Append('\n'); }
                continue;
            }
            if (!closing && name is "SCRIPT" or "STYLE") { suppressed = name; continue; }
            if (name is "P" or "DIV" or "BR" or "LI" or "UL" or "OL" or "H1" or "H2" or "H3" or "TR" or "SECTION")
                result.Append('\n');
        }
        return WebUtility.HtmlDecode(result.ToString());
    }

    private static readonly string[] EncodedHtmlTags = ["p", "div", "br", "ul", "ol", "li", "h1", "h2", "h3", "script", "style"];
}
