using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using JobPortal.Application.Abstractions.Candidates;
using UglyToad.PdfPig;

namespace JobPortal.Infrastructure.Services;

public sealed class ResumeTextExtractor : IResumeTextExtractor
{
    private const int MaximumCharacters = 200_000;

    public Task<string> ExtractAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content.CanSeek) content.Position = 0;
        var text = extension.ToLowerInvariant() switch
        {
            ".pdf" => Pdf(content),
            ".docx" => Docx(content),
            ".doc" => LegacyDoc(content),
            _ => throw new NotSupportedException("Unsupported resume format.")
        };
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("No extractable resume text was found.");
        return Task.FromResult(text.Length <= MaximumCharacters ? text : text[..MaximumCharacters]);
    }

    private static string Pdf(Stream content)
    {
        using var document = PdfDocument.Open(content);
        // Preserve line boundaries needed by the factual source parser.
        return string.Join('\n', document.GetPages().Select(page => string.Join('\n', page.GetWords()
            .GroupBy(word => Math.Round(word.BoundingBox.Bottom / 3))
            .OrderByDescending(line => line.Key)
            .Select(line => string.Join(' ', line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text))))));
    }

    private static string Docx(Stream content)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 1000 || archive.Entries.Sum(x => x.Length) > 20 * 1024 * 1024 ||
            archive.Entries.GroupBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new InvalidDataException("DOCX package is invalid or exceeds supported limits.");

        var main = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("Invalid DOCX document.");
        var headers = archive.Entries
            .Where(entry => entry.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var footers = archive.Entries
            .Where(entry => entry.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Contact details commonly live in a Word header/footer. Use those parts only when
        // the body does not already contain contact details; boilerplate page headers/footers
        // must not be mistaken for candidate facts when the body is complete.
        var body = string.Join('\n', ReadParagraphs(main));
        if (ContainsContactData(body)) return body;
        return string.Join('\n', headers.SelectMany(ReadParagraphs).Concat(footers.SelectMany(ReadParagraphs)).Append(body));
    }

    private static bool ContainsContactData(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}") ||
        System.Text.RegularExpressions.Regex.Matches(text, @"\+?\d[\d ()-]{7,}\d")
            .Any(match => match.Value.Count(char.IsDigit) >= 9) ||
        System.Text.RegularExpressions.Regex.IsMatch(text, @"https?://[^\s|]+");

    private static IEnumerable<string> ReadParagraphs(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        });
        XDocument document;
        try { document = XDocument.Load(reader, LoadOptions.None); }
        catch (System.Xml.XmlException ex) { throw new InvalidDataException("DOCX XML is invalid.", ex); }

        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        foreach (var paragraph in document.Descendants(word + "p"))
        {
            var text = string.Concat(paragraph.Descendants().Where(x => x.Name == word + "t" || x.Name == word + "tab" || x.Name == word + "br")
                .Select(x => x.Name == word + "t" ? x.Value : x.Name == word + "tab" ? " | " : "\n"));
            if (paragraph.Descendants(word + "numPr").Any() &&
                !text.TrimStart().StartsWith('-') &&
                !text.TrimStart().StartsWith('•') && !text.TrimStart().StartsWith('*')) text = "• " + text;
            yield return text;
        }
    }

    private static string LegacyDoc(Stream content)
    {
        using var memory = new MemoryStream(); content.CopyTo(memory); var bytes = memory.ToArray();
        var unicode = ExtractRuns(Encoding.Unicode.GetString(bytes));
        var ascii = ExtractRuns(Encoding.Latin1.GetString(bytes));
        return unicode.Length >= ascii.Length ? unicode : ascii;
    }

    private static string ExtractRuns(string value) => string.Join(' ', value.Split('\0', '\r', '\n', '\t')
        .Select(x => new string(x.Where(c => !char.IsControl(c)).ToArray()).Trim())
        .Where(x => x.Length >= 3));
}
