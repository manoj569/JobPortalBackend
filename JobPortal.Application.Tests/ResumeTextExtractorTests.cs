using System.IO.Compression;
using System.Text;
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
}
