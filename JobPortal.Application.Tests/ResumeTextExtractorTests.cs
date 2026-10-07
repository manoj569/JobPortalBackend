using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using JobPortal.Infrastructure.AIResume;
using JobPortal.Infrastructure.Services;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ResumeTextExtractorTests
{
    private readonly ResumeTextExtractor extractor = new();

    [Fact]
    public async Task DocxExtractionReadsDocumentText()
    {
        await using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            await writer.WriteAsync("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>C# SQL Engineer</w:t></w:r></w:p></w:body></w:document>");
        }
        stream.Position = 0;
        Assert.Contains("C# SQL Engineer", await extractor.ExtractAsync(stream, ".docx"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DocxExtractionPreservesParagraphAndRunBoundaries()
    {
        await using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            await writer.WriteAsync("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>TECHNICAL SKILLS</w:t></w:r></w:p><w:p><w:r><w:t>C</w:t></w:r><w:r><w:t># | .NET</w:t></w:r></w:p><w:p><w:r><w:t>WORK EXPERIENCE</w:t></w:r></w:p></w:body></w:document>");
        }
        stream.Position = 0;
        Assert.Equal("TECHNICAL SKILLS\nC# | .NET\nWORK EXPERIENCE", await extractor.ExtractAsync(stream, ".docx"));
    }

    [Fact]
    public async Task DocxExtractionIncludesTablesFormattedRunsHeadersFootersAndListItems()
    {
        await using var stream = BuildStructuredDocx();

        var text = await extractor.ExtractAsync(stream, ".docx");

        Assert.StartsWith("Morgan Example\nEmail: morgan@example.invalid", text, StringComparison.Ordinal);
        Assert.Contains("TECHNICAL SKILLS\nC#\n.NET", text, StringComparison.Ordinal);
        Assert.Contains("WORK EXPERIENCE\nBackend Developer | Cedar Software | 2021 - 2024\n• Built reliable APIs", text, StringComparison.Ordinal);
        Assert.Contains("Confidential footer", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DocxExtractionPreservesExistingMasterWorkflowFacts()
    {
        await using var stream = new MemoryStream(MasterResumeFixture.Docx());

        var text = await extractor.ExtractAsync(stream, ".docx");
        var parsed = UploadedResumeFactParser.Parse(text);
        var expected = MasterResumeFixture.Source;

        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(parsed));
    }

    [Fact]
    public async Task ThreeColumnExperienceRowWithLocationDoesNotTreatLocationAsDates()
    {
        await using var stream = BuildStructuredDocx(experienceDetails: "Pune, India");

        var text = await extractor.ExtractAsync(stream, ".docx");
        var parsed = UploadedResumeFactParser.Parse(text);
        var experience = Assert.Single(parsed.Experience);

        Assert.Equal("Cedar Software", experience.Employer);
        Assert.Equal("Backend Developer", experience.Role);
        Assert.Empty(experience.StartDate);
        Assert.Empty(experience.EndDate);

        var splitTable = UploadedResumeFactParser.Parse("Morgan Example\nSKILLS\nC#\nWORK EXPERIENCE\n" +
            " | Backend Developer | 2021 - 2024\n | Cedar Software | Pune, India\n• Built reliable APIs");
        var tableExperience = Assert.Single(splitTable.Experience);
        Assert.Equal("Backend Developer", tableExperience.Role);
        Assert.Equal("Cedar Software", tableExperience.Employer);
        Assert.Equal("2021", tableExperience.StartDate);
        Assert.Equal("2024", tableExperience.EndDate);
        Assert.Equal(["Built reliable APIs"], tableExperience.Bullets);
    }

    [Fact]
    public async Task LegacyDocExtractionReadsBoundedPrintableText()
    {
        await using var stream = new MemoryStream(Encoding.Latin1.GetBytes("\0\0Software Engineer\0C# and SQL\0"));
        var text = await extractor.ExtractAsync(stream, ".doc");
        Assert.Contains("Software Engineer", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PdfExtractionReadsPageText()
    {
        await using var stream = BuildPdf("Backend Engineer SQL");
        var text = await extractor.ExtractAsync(stream, ".pdf");
        Assert.Contains("Backend Engineer SQL", text, StringComparison.Ordinal);
    }

    private static MemoryStream BuildPdf(string text)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {text.Length + 31} >>\nstream\nBT /F1 12 Tf 72 720 Td ({text}) Tj ET\nendstream"
        };
        var builder = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString())); builder.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) builder.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        builder.Append("trailer << /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return new MemoryStream(Encoding.ASCII.GetBytes(builder.ToString()));
    }

    internal static MemoryStream BuildStructuredDocx(string bullet = "Built reliable APIs", string experienceDetails = "2021 - 2024")
    {
        const string wordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace w = wordNs;
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePart(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/><Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>
                """);
            WritePart(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            WritePart(archive, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/><Relationship Id="rIdFooter" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/></Relationships>
                """);

            var body = new XElement(w + "body",
                Paragraph(w, "TECHNICAL SKILLS"),
                new XElement(w + "tbl", new XElement(w + "tr",
                    new XElement(w + "tc", Paragraph(w, "C#")),
                    new XElement(w + "tc", Paragraph(w, ".NET")))),
                Paragraph(w, "WORK EXPERIENCE"),
                new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "Backend Developer | ")),
                    new XElement(w + "r", new XElement(w + "rPr", new XElement(w + "b")), new XElement(w + "t", $"Cedar Software | {experienceDetails}"))),
                new XElement(w + "p", new XElement(w + "pPr", new XElement(w + "numPr", new XElement(w + "ilvl", new XAttribute(w + "val", "0")), new XElement(w + "numId", new XAttribute(w + "val", "1")))),
                    new XElement(w + "r", new XElement(w + "t", bullet))),
                new XElement(w + "sectPr", new XElement(w + "headerReference", new XAttribute(w + "type", "default"), new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "id", "rIdHeader")),
                    new XElement(w + "footerReference", new XAttribute(w + "type", "default"), new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "id", "rIdFooter"))));
            WritePart(archive, "word/document.xml", new XDocument(new XElement(w + "document", new XAttribute(XNamespace.Xmlns + "w", wordNs), body)).ToString(SaveOptions.DisableFormatting));
            WritePart(archive, "word/header1.xml", new XDocument(new XElement(w + "hdr", new XAttribute(XNamespace.Xmlns + "w", wordNs),
                Paragraph(w, "Morgan Example"), Paragraph(w, "Email: morgan@example.invalid"))).ToString(SaveOptions.DisableFormatting));
            WritePart(archive, "word/footer1.xml", new XDocument(new XElement(w + "ftr", new XAttribute(XNamespace.Xmlns + "w", wordNs),
                Paragraph(w, "Confidential footer"))).ToString(SaveOptions.DisableFormatting));
        }
        stream.Position = 0;
        return stream;
    }

    private static XElement Paragraph(XNamespace w, string text) => new(w + "p", new XElement(w + "r", new XElement(w + "t", text)));

    private static void WritePart(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
