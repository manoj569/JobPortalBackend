using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Features.AIResume;
using JobPortal.Domain.Entities;

namespace JobPortal.Infrastructure.AIResume;

public sealed class OriginalResumeDocuments(IResumeStorage storage, IAIResumeDocumentRenderer fallback) : IAIResumeMasterDocuments
{
    public async Task<ResumeMasterDocument> CaptureAsync(User candidate, CancellationToken ct)
    {
        var extension = Path.GetExtension(candidate.ResumeFileName ?? "").ToLowerInvariant();
        if (extension is not (".docx" or ".pdf" or ".doc") || candidate.ResumeStorageKey is null)
            throw new InvalidDataException("Original resume is unavailable.");
        var bytes = await Read(candidate.ResumeStorageKey, ct);
        if (extension == ".docx") _ = DocxTextEditor.Bind(bytes, []);
        await using var input = new MemoryStream(bytes, writable: false);
        var key = await storage.StoreAsync(input, extension, ct);
        return new(key, extension, Hash(bytes), []);
    }

    public async Task<ResumeMasterDocument> BindAsync(ResumeMasterDocument master, TailoredResumeContent source, CancellationToken ct)
    {
        var bytes = await VerifiedRead(master.StorageKey, master.Sha256, ct);
        return master with { Bindings = master.Extension == ".docx" ? DocxTextEditor.Bind(bytes, ResumePatchGuard.Targets(source)) : [] };
    }

    public async Task<ResumeArtifact> CreateArtifactAsync(ResumeMasterDocument master, TailoredResumeContent source,
        ResumePatchResult result, CancellationToken ct)
    {
        var original = await VerifiedRead(master.StorageKey, master.Sha256, ct);
        byte[] bytes;
        string extension;
        if (master.Extension == ".docx")
        {
            bytes = DocxTextEditor.Apply(original, master.Bindings, result.Decisions.Where(x => x.Status is "accepted" or "edited").Select(x => x.Proposal).ToArray());
            extension = ".docx";
        }
        else { bytes = fallback.Render(result.Content, "pdf").Content; extension = ".pdf"; }
        ct.ThrowIfCancellationRequested();
        await using var stream = new MemoryStream(bytes, writable: false);
        var key = await storage.StoreAsync(stream, extension, ct);
        return new(key, extension, Hash(bytes));
    }

    public async Task<AIResumeDownload> DownloadAsync(StoredTailoring tailoring, string format, CancellationToken ct)
    {
        var capabilities = Capabilities(tailoring.MasterDocument);
        format = format.ToLowerInvariant();
        if (format == "original") format = capabilities.DefaultDownloadFormat;
        if (!capabilities.DownloadFormats.Contains(format, StringComparer.Ordinal)) throw new ArgumentException("Unsupported download format.", nameof(format));
        if (format == tailoring.Artifact.Extension.TrimStart('.'))
            return new(await VerifiedRead(tailoring.Artifact.StorageKey, tailoring.Artifact.Sha256, ct),
                format == "docx" ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : "application/pdf", $"Tailored_Resume.{format}");
        return fallback.Render(tailoring.Content, format);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct) => storage.DeleteAsync(storageKey, ct);
    public ResumeDocumentCapabilities Capabilities(ResumeMasterDocument master) => master.Extension == ".docx"
        ? new("original_docx", "docx", true, "docx", ["docx", "txt"], "PDF conversion is unavailable. Text wrapping can change; mixed run styles are retained as closely as practical.")
        : new("rendered_fallback", master.Extension.TrimStart('.'), false, "pdf", ["pdf", "docx", "txt"],
            "Original layout and skill categories are not preserved by fallback rendering. Upload DOCX for original-format preservation.");

    private async Task<byte[]> VerifiedRead(string key, string expectedHash, CancellationToken ct)
    {
        var bytes = await Read(key, ct);
        if (Hash(bytes) != expectedHash) throw new InvalidDataException("Resume document integrity check failed.");
        return bytes;
    }
    private async Task<byte[]> Read(string key, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(key, ct) ?? throw new InvalidDataException("Resume document was not found.");
        using var result = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (result.Length + count > 10 * 1024 * 1024) throw new InvalidDataException("Resume document is too large.");
            await result.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return result.ToArray();
    }
    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}

public static class DocxTextEditor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static ResumeDocumentBinding[] Bind(byte[] original, ResumeSourceEvidence[] targets)
    {
        using var input = new MemoryStream(original, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var document = Load(archive);
        var paragraphs = document.Descendants(W + "p").ToArray();
        var result = new List<ResumeDocumentBinding>();
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.Text)) continue;
            var matches = new List<ResumeDocumentBinding>();
            for (var i = 0; i < paragraphs.Length; i++)
            {
                var paragraph = paragraphs[i];
                if (paragraph.Descendants().Any(x => x.Name.Namespace == W &&
                    x.Name.LocalName is "tab" or "br" or "fldChar" or "instrText" or "drawing" or "pict" or "object" or "del" or "ins" or "sdt")) continue;
                var text = string.Concat(paragraph.Descendants(W + "t").Select(x => x.Value));
                var offset = text.IndexOf(target.Text, StringComparison.Ordinal);
                if (offset >= 0 && text.IndexOf(target.Text, offset + 1, StringComparison.Ordinal) < 0)
                    matches.Add(new(target.Id, i, offset, target.Text));
            }
            // Ambiguous duplicates and multi-paragraph targets remain untouched, never guessed.
            if (matches.Count == 1) result.Add(matches[0]);
        }
        return result.Where(x => !result.Any(other => other.TargetId != x.TargetId && other.Paragraph == x.Paragraph &&
            other.Offset < x.Offset + x.OriginalText.Length && x.Offset < other.Offset + other.OriginalText.Length)).ToArray();
    }

    public static byte[] Apply(byte[] original, ResumeDocumentBinding[] bindings, ResumeTextReplacement[] replacements)
    {
        using var output = new MemoryStream();
        output.Write(original);
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            var document = Load(archive);
            var paragraphs = document.Descendants(W + "p").ToArray();
            var mapped = replacements.Select(x => (Replacement: x, Binding: bindings.SingleOrDefault(b => b.TargetId == x.TargetId)
                ?? throw new InvalidDataException("Document target is unmapped."))).ToArray();
            // Offsets describe the immutable master. Apply right-to-left when targets share a paragraph.
            foreach (var item in mapped.OrderByDescending(x => x.Binding.Paragraph).ThenByDescending(x => x.Binding.Offset))
            {
                if (item.Replacement.OriginalText != item.Binding.OriginalText) throw new InvalidDataException("Original document text differs.");
                var nodes = paragraphs[item.Binding.Paragraph].Descendants(W + "t").ToArray();
                var text = string.Concat(nodes.Select(x => x.Value));
                var start = item.Binding.Offset;
                var length = item.Binding.OriginalText.Length;
                if (start < 0 || start + length > text.Length || text.Substring(start, length) != item.Binding.OriginalText)
                    throw new InvalidDataException("Original document target differs.");
                var offset = 0; var replaced = 0; var covered = 0;
                foreach (var node in nodes)
                {
                    var value = node.Value;
                    var left = Math.Max(start, offset); var right = Math.Min(start + length, offset + value.Length);
                    if (left < right)
                    {
                        covered += right - left;
                        var end = (int)((long)item.Replacement.ReplacementText.Length * covered / length);
                        node.Value = value[..(left - offset)] + item.Replacement.ReplacementText[replaced..end] + value[(right - offset)..];
                        node.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                        replaced = end;
                    }
                    offset += value.Length;
                }
            }
            if (mapped.Length > 0)
            {
                var entry = archive.GetEntry("word/document.xml")!;
                using var stream = entry.Open();
                stream.SetLength(0);
                using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
                document.Save(writer);
            }
        }
        return output.ToArray();
    }

    private static XDocument Load(ZipArchive archive)
    {
        if (archive.Entries.Count > 1000 || archive.Entries.Sum(x => x.Length) > 20 * 1024 * 1024)
            throw new InvalidDataException("DOCX package is too large.");
        if (archive.Entries.GroupBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new InvalidDataException("DOCX package contains ambiguous parts.");
        var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("DOCX document is invalid.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
        try { return XDocument.Load(reader, LoadOptions.PreserveWhitespace); }
        catch (XmlException) { throw new InvalidDataException("DOCX XML is invalid."); }
    }
}
