using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using JobPortal.Application.Features.AIResume;

namespace JobPortal.Infrastructure.AIResume;

public sealed class ProfessionalResumeDocumentRenderer : IAIResumeDocumentRenderer
{
    public AIResumeDownload Render(TailoredResumeContent content, string format)
    {
        if (!AIResumeContentGuard.WellFormed(content)) throw new InvalidDataException("Resume content is invalid.");
        var lines = Lines(content).Take(1000).ToArray();
        var safeName = SafeName(content.Contact.Name);
        return format.ToLowerInvariant() switch
        {
            "txt" => new(Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines)), "text/plain; charset=utf-8", $"{safeName}_Resume.txt"),
            "docx" => new(Docx(lines), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{safeName}_Resume.docx"),
            "pdf" => new(Pdf(lines), "application/pdf", $"{safeName}_Resume.pdf"),
            _ => throw new ArgumentException("Unsupported resume format.", nameof(format))
        };
    }

    private static IEnumerable<string> Lines(TailoredResumeContent content)
    {
        yield return content.Contact.Name;
        yield return string.Join(" | ", new[] { content.Contact.Email, content.Contact.Phone, content.Contact.Location }.Where(x => !string.IsNullOrWhiteSpace(x)));
        foreach (var link in content.Contact.Links) yield return link;
        if (!string.IsNullOrWhiteSpace(content.ProfessionalSummary)) { yield return "PROFESSIONAL SUMMARY"; yield return content.ProfessionalSummary; }
        if (content.Skills.Length > 0) { yield return "SKILLS"; yield return string.Join(" | ", content.Skills); }
        foreach (var item in content.Experience)
        {
            yield return "WORK EXPERIENCE";
            yield return $"{item.Role} | {item.Employer} | {item.StartDate} - {item.EndDate}";
            foreach (var bullet in item.Bullets) yield return $"- {bullet}";
        }
        foreach (var item in content.Projects)
        {
            yield return "PROJECTS"; yield return item.Name;
            if (item.Technologies.Length > 0) yield return string.Join(" | ", item.Technologies);
            foreach (var bullet in item.Bullets) yield return $"- {bullet}";
        }
        foreach (var item in content.Education) { yield return "EDUCATION"; yield return $"{item.Qualification} | {item.Institution} | {item.StartDate} - {item.EndDate}"; }
        foreach (var item in content.Certifications) { yield return "CERTIFICATIONS"; yield return $"{item.Name} | {item.Issuer} | {item.Date}"; }
        if (content.AdditionalInfo.Length > 0) { yield return "ADDITIONAL INFORMATION"; foreach (var line in content.AdditionalInfo) yield return line; }
    }

    private static byte[] Docx(string[] lines)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                """);
            Write(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            var body = new XElement(W + "body",
                lines.Select(line => new XElement(W + "p",
                    new XElement(W + "r",
                        new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), line)))),
                new XElement(W + "sectPr", new XElement(W + "pgSz", new XAttribute(W + "w", "12240"), new XAttribute(W + "h", "15840")),
                    new XElement(W + "pgMar", new XAttribute(W + "top", "1080"), new XAttribute(W + "bottom", "1080"), new XAttribute(W + "left", "1080"), new XAttribute(W + "right", "1080"))));
            Write(archive, "word/document.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "document", body)).ToString(SaveOptions.DisableFormatting));
        }
        return output.ToArray();
    }
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open(); using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(content);
    }

    private static byte[] Pdf(string[] lines)
    {
        const int linesPerPage = 48;
        var pages = lines.SelectMany(WrapPdfLine).Chunk(linesPerPage).ToArray();
        using var output = new MemoryStream();
        var offsets = new List<long> { 0 };
        void Object(int id, string value)
        {
            offsets.Add(output.Position);
            var bytes = Encoding.ASCII.GetBytes($"{id} 0 obj\n{value}\nendobj\n"); output.Write(bytes);
        }
        output.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));
        Object(1, $"<< /Type /Catalog /Pages 2 0 R >>");
        var pageIds = Enumerable.Range(0, pages.Length).Select(i => 3 + i * 2).ToArray();
        Object(2, $"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(x => $"{x} 0 R"))}] /Count {pageIds.Length} >>");
        var fontId = 3 + pages.Length * 2;
        for (var i = 0; i < pages.Length; i++)
        {
            var pageId = pageIds[i]; var contentId = pageId + 1;
            Object(pageId, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {contentId} 0 R >>");
            var commands = new StringBuilder("BT\n/F1 10 Tf\n50 750 Td\n14 TL\n");
            foreach (var line in pages[i]) commands.Append('(').Append(PdfEscape(Ascii(line))).Append(") Tj\nT*\n");
            commands.Append("ET\n"); var stream = Encoding.ASCII.GetBytes(commands.ToString());
            offsets.Add(output.Position);
            output.Write(Encoding.ASCII.GetBytes($"{contentId} 0 obj\n<< /Length {stream.Length} >>\nstream\n"));
            output.Write(stream); output.Write(Encoding.ASCII.GetBytes("endstream\nendobj\n"));
        }
        Object(fontId, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        var xref = output.Position; output.Write(Encoding.ASCII.GetBytes($"xref\n0 {offsets.Count}\n0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1)) output.Write(Encoding.ASCII.GetBytes($"{offset:0000000000} 00000 n \n"));
        output.Write(Encoding.ASCII.GetBytes($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return output.ToArray();
    }
    private static string Ascii(string value) => new(value.Select(x => x is >= ' ' and <= '~' ? x : '?').ToArray());
    private static IEnumerable<string> WrapPdfLine(string value)
    {
        const int maximumLineLength = 96;
        var remaining = value;
        while (remaining.Length > maximumLineLength)
        {
            var split = remaining.LastIndexOf(' ', maximumLineLength);
            if (split <= 0) split = maximumLineLength;
            yield return remaining[..split];
            remaining = remaining[split..].TrimStart();
        }
        yield return remaining;
    }
    private static string PdfEscape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
    private static string SafeName(string value)
    {
        var name = string.Join('_', value.Split(Path.GetInvalidFileNameChars().Concat(['/', '\\']).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries));
        name = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ' ').ToArray()).Trim().Replace(' ', '_');
        return name.Length is > 0 and <= 80 ? name : "CareerHarbor";
    }
}
