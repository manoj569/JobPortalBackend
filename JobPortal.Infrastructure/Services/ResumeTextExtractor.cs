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
        return string.Join('\n', document.GetPages().Select(page => page.Text));
    }

    private static string Docx(Stream content)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("Invalid DOCX document.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream, LoadOptions.None);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Join(' ', document.Descendants(word + "t").Select(x => x.Value));
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
