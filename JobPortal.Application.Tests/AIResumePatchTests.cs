using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Features.AIResume;
using JobPortal.Infrastructure.AIResume;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIResumePatchTests
{
    [Fact]
    public void EightOfTenApplyIndependentlyAndAllSourceIdentitiesRemain()
    {
        var source = MasterResumeFixture.Source;
        var result = ResumePatchGuard.Apply(source, MasterResumeFixture.Patch, MasterResumeFixture.Jd);
        Assert.Equal(8, result.AcceptedCount);
        Assert.Equal(2, result.Decisions.Count(x => x.Status == "rejected"));
        Assert.Equal(source.ProfessionalSummary, result.Content.ProfessionalSummary);
        Assert.Equal(source.Skills, result.Content.Skills);
        Assert.Equal(source.Contact, result.Content.Contact);
        Assert.Equal(source.Education, result.Content.Education);
        Assert.Equal(source.Certifications, result.Content.Certifications);
        Assert.Equal(5, result.Content.Experience.Length); Assert.Equal(5, result.Content.Projects.Length);
        Assert.Equal(source.Experience.Select(x => (x.Employer, x.Role, x.StartDate, x.EndDate)), result.Content.Experience.Select(x => (x.Employer, x.Role, x.StartDate, x.EndDate)));
        Assert.Equal(source.Projects.Select(x => x.Name), result.Content.Projects.Select(x => x.Name));
        Assert.Equal(source.Experience[0].Bullets[1], result.Content.Experience[0].Bullets[1]);
        Assert.True(AIResumeContentGuard.Validate(source, result.Content).IsValid);
    }

    [Theory]
    [InlineData("CUDA")]
    [InlineData("cuda")]
    [InlineData("Triton")]
    [InlineData("triton")]
    [InlineData("GPU programming")]
    [InlineData("parallel computing")]
    [InlineData("Led")]
    [InlineData("owned")]
    [InlineData("机器学习")]
    [InlineData("Кубернетес")]
    public void UnsupportedRequirementsAndResponsibilityKeepOriginal(string claim)
    {
        var source = MasterResumeFixture.Source;
        var target = ResumePatchGuard.Targets(source).Single(x => x.Id == "SRC-EXP-001-BULLET-001");
        var proposal = MasterResumeFixture.Change(target) with { ReplacementText = target.Text.Replace("Built", claim, StringComparison.Ordinal) };
        var result = ResumePatchGuard.Apply(source, new([proposal], []), MasterResumeFixture.Jd);
        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal("rejected", Assert.Single(result.Decisions).Status);
        Assert.Equal(target.Text, result.Content.Experience[0].Bullets[0]);
    }

    [Theory]
    [InlineData("SRC-EXP-001-IDENTITY-001")]
    [InlineData("SRC-PROJECT-001-IDENTITY-001")]
    [InlineData("SRC-EDU-001")]
    [InlineData("SRC-CERT-001")]
    [InlineData("SRC-CONTACT-001")]
    [InlineData("SRC-SKILL-001")]
    public void IdentitySkillAndEducationChangesCannotBeTargets(string targetId)
    {
        var source = MasterResumeFixture.Source;
        var fact = ResumeEvidenceCatalog.Create(source).Single(x => x.Id == targetId);
        var result = ResumePatchGuard.Apply(source, new([new(targetId, fact.Text, "Invented replacement", [targetId], [], "JD alignment")], []), MasterResumeFixture.Jd);
        Assert.Equal("target_not_editable", Assert.Single(result.Decisions).RejectionReason);
        Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(result.Content));
    }

    [Theory]
    [InlineData("wrong_original")]
    [InlineData("wrong_scope")]
    [InlineData("missing_target_id")]
    [InlineData("unknown_id")]
    [InlineData("changed_metric")]
    [InlineData("new_domain")]
    public void RejectedPatchCannotUseUnrelatedFactsOrChangeMetricContext(string change)
    {
        var source = MasterResumeFixture.Source;
        var target = ResumePatchGuard.Targets(source).Single(x => x.Id == "SRC-EXP-001-BULLET-002");
        var proposal = new ResumeTextReplacement(target.Id, target.Text, target.Text.Replace("through", "using", StringComparison.Ordinal), [target.Id], [], "Supported wording");
        proposal = change switch
        {
            "wrong_original" => proposal with { OriginalText = "A different bullet" },
            "wrong_scope" => proposal with { SourceEvidenceIds = [target.Id, "SRC-EXP-002-BULLET-001"] },
            "missing_target_id" => proposal with { SourceEvidenceIds = ["SRC-EXP-001-BULLET-001"] },
            "unknown_id" => proposal with { SourceEvidenceIds = [target.Id, "candidate@example.invalid"] },
            "changed_metric" => proposal with { ReplacementText = proposal.ReplacementText.Replace("30%", "31%", StringComparison.Ordinal) },
            _ => proposal with { ReplacementText = proposal.ReplacementText.Replace("support incidents", "hosting costs", StringComparison.Ordinal) }
        };
        var diagnostics = new List<ResumeGroundingDiagnostic>();
        var result = ResumePatchGuard.Apply(source, new([proposal], []), MasterResumeFixture.Jd, diagnostic: diagnostics.Add);
        Assert.Equal(0, result.AcceptedCount); Assert.Single(diagnostics);
        Assert.DoesNotContain("candidate@example.invalid", JsonSerializer.Serialize(diagnostics));
        Assert.Equal(target.Text, result.Content.Experience[0].Bullets[1]);
    }

    [Fact]
    public void DuplicateTargetsAndUnmappedDocumentTargetsAreRejectedRatherThanGuessed()
    {
        var proposal = MasterResumeFixture.Patch.Replacements[0];
        Assert.Equal(0, ResumePatchGuard.Apply(MasterResumeFixture.Source, new([proposal, proposal], []), MasterResumeFixture.Jd).AcceptedCount);
        Assert.Equal(0, ResumePatchGuard.Apply(MasterResumeFixture.Source, new([proposal], []), MasterResumeFixture.Jd, new HashSet<string>()).AcceptedCount);
    }

    [Fact]
    public void MissingSummaryCannotBeCreatedAndWhitespaceOnlyChangesAreNotUseful()
    {
        var source = MasterResumeFixture.Source with { ProfessionalSummary = "" };
        Assert.DoesNotContain(ResumePatchGuard.Targets(source), x => x.Id == "SRC-SUMMARY-001");
        var fabricated = new ResumeTextReplacement("SRC-SUMMARY-001", "", "Backend developer", ["SRC-SUMMARY-001"], [], "Summary");
        var target = ResumePatchGuard.Targets(source).First();
        var whitespace = new ResumeTextReplacement(target.Id, target.Text, " " + target.Text.Replace(" ", "  ", StringComparison.Ordinal) + " ", [target.Id], [], "Whitespace");
        var result = ResumePatchGuard.Apply(source, new([fabricated, whitespace], []), MasterResumeFixture.Jd);
        Assert.Equal(0, result.AcceptedCount); Assert.Equal("", result.Content.ProfessionalSummary);
        Assert.Equal("original", result.Decisions[1].Status);
    }

    [Fact]
    public async Task ClaudeReceivesOnlyPatchSchemaStableTargetsAndExistingWorkspaceHeaders()
    {
        var source = MasterResumeFixture.Source;
        var handler = new ClaudeAIResumeProviderTests.Handler((request, _) =>
        {
            Assert.Equal("fixture-workspace", Assert.Single(request.Headers.GetValues("anthropic-workspace-id")));
            Assert.Equal("fixture-key", Assert.Single(request.Headers.GetValues("x-api-key")));
            return Task.FromResult(ClaudeAIResumeProviderTests.Response(MasterResumeFixture.Patch));
        });
        var provider = new ClaudeAIResumeProvider(new ClaudeAIResumeProviderTests.Factory(handler),
            Options.Create(new AIResumeOptions { ApiKey = "fixture-key", WorkspaceId = "fixture-workspace" }), new ClaudeAIResumeProviderTests.CapturingLogger());
        var result = await provider.GenerateTailoringPatchAsync(new(source, MasterResumeFixture.Jd, MasterResumeFixture.Analysis,
            [new("FAKE", "skills", "CUDA")], ResumePatchGuard.Targets(source)));
        Assert.Equal(10, result.Content.Replacements.Length); Assert.Equal(1, handler.Calls);
        using var body = JsonDocument.Parse(handler.Body!);
        var schema = body.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("schema");
        Assert.Equal(new[] { "replacements", "emphasizedSkillEvidenceIds" }, schema.GetProperty("properties").EnumerateObject().Select(x => x.Name));
        var targetIds = schema.GetProperty("properties").GetProperty("replacements").GetProperty("items").GetProperty("properties").GetProperty("targetId").GetProperty("enum");
        Assert.DoesNotContain(targetIds.EnumerateArray(), x => x.GetString()!.Contains("IDENTITY", StringComparison.Ordinal));
        Assert.Contains("Return ONLY TailoringPatch", body.RootElement.GetProperty("system").GetString());
        using var input = JsonDocument.Parse(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!);
        Assert.DoesNotContain(input.RootElement.GetProperty("sourceEvidence").EnumerateArray(), x => x.GetProperty("id").GetString() == "FAKE");
    }

    [Fact]
    public async Task AnalysisKeepsUnsupportedGpuRequirementsMissingAndRemainsOneCall()
    {
        var handler = new ClaudeAIResumeProviderTests.Handler((_, _) => Task.FromResult(ClaudeAIResumeProviderTests.Response(MasterResumeFixture.Analysis)));
        var provider = new ClaudeAIResumeProvider(new ClaudeAIResumeProviderTests.Factory(handler), Options.Create(new AIResumeOptions { ApiKey = "fixture-key" }), new ClaudeAIResumeProviderTests.CapturingLogger());
        var result = await provider.AnalyzeAsync(new(MasterResumeFixture.Source, MasterResumeFixture.Jd));
        Assert.Contains("Performance Optimization", result.Content.MatchedSkills);
        Assert.Contains("Scalability", result.Content.MatchedSkills);
        Assert.Equal(["CUDA", "Triton", "GPU programming", "parallel computing"], result.Content.MissingSkills);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void DocxPreservesStylesUnchangedParagraphsPackagePartsAndOriginalBytes()
    {
        var original = MasterResumeFixture.Docx();
        var copy = original.ToArray();
        var source = MasterResumeFixture.Source;
        var bindings = DocxTextEditor.Bind(original, ResumePatchGuard.Targets(source));
        var applied = ResumePatchGuard.Apply(source, MasterResumeFixture.Patch, MasterResumeFixture.Jd, bindings.Select(x => x.TargetId).ToHashSet());
        Assert.Equal(8, applied.AcceptedCount);
        var changes = applied.Decisions.Where(x => x.Status == "accepted").Select(x => x.Proposal).ToArray();
        var output = DocxTextEditor.Apply(original, bindings, changes);
        Assert.Equal(copy, original); Assert.NotEqual(original, output);
        var before = MasterResumeFixture.Parts(original); var after = MasterResumeFixture.Parts(output);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var part in before.Keys.Where(x => x != "word/document.xml")) Assert.Equal(before[part], after[part]);
        XNamespace w = MasterResumeFixture.W;
        var originalDocument = XDocument.Parse(Encoding.UTF8.GetString(before["word/document.xml"]), LoadOptions.PreserveWhitespace);
        var tailoredDocument = XDocument.Parse(Encoding.UTF8.GetString(after["word/document.xml"]), LoadOptions.PreserveWhitespace);
        var originalParagraphs = originalDocument.Descendants(w + "p").ToArray();
        var tailoredParagraphs = tailoredDocument.Descendants(w + "p").ToArray();
        var changedParagraphs = changes.Select(x => bindings.Single(b => b.TargetId == x.TargetId).Paragraph).ToHashSet();
        Assert.Equal(originalParagraphs.Length, tailoredParagraphs.Length);
        for (var i = 0; i < originalParagraphs.Length; i++)
        {
            if (!changedParagraphs.Contains(i)) Assert.Equal(originalParagraphs[i].ToString(), tailoredParagraphs[i].ToString());
            else
            {
                Assert.Equal(originalParagraphs[i].Element(w + "pPr")!.ToString(), tailoredParagraphs[i].Element(w + "pPr")!.ToString());
                Assert.Equal(originalParagraphs[i].Descendants(w + "rPr").Select(x => x.ToString()), tailoredParagraphs[i].Descendants(w + "rPr").Select(x => x.ToString()));
            }
        }
        Assert.Equal(originalDocument.Descendants(w + "sectPr").Single().ToString(), tailoredDocument.Descendants(w + "sectPr").Single().ToString());
        Assert.Contains("Engineering:", Encoding.UTF8.GetString(after["word/document.xml"]));
    }
}

internal static class MasterResumeFixture
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string Jd = "Optimize application performance and scalability using problem solving and cross-functional collaboration. Requirements include CUDA, Triton, GPU programming and parallel computing.";
    internal static ResumeAnalysis Analysis => new(68, ["Performance Optimization", "Scalability", "Problem Solving", "Cross-functional Collaboration"],
        ["Application optimization supports related performance work"], ["CUDA", "Triton", "GPU programming", "parallel computing"],
        ["performance", "scalability"], ["Problem Solving"], ["GPU programming missing"], ["Emphasize supported optimization"]);
    internal static string Text => string.Join('\n', new[]
    {
        "Morgan Example", "Email: morgan@example.invalid", "Phone: +91 90000 00000", "Location: Pune",
        "PROFESSIONAL SUMMARY", "Backend developer with C# experience in performance optimization and scalable application solutions.",
        "TECHNICAL SKILLS", "Engineering: Performance Optimization | Scalability | Problem Solving | Cross-functional Collaboration",
        "Languages: C# | Java | Python | TypeScript", "Platforms: .NET | ASP.NET | SQL Server | PostgreSQL | Redis | Docker | Azure",
        "WORK EXPERIENCE"
    }.Concat(Enumerable.Range(1, 5).SelectMany(i => new[] { $"Backend Developer | Cedar Software {i} | Jan {2010 + i} - Dec {2010 + i}",
        $"- Built reliable C# APIs for service {i} using performance optimization and scalable solutions.",
        $"- Reduced recurring support incidents for service {i} by 30% through problem solving and workflow improvements." }))
        .Concat(new[] { "PROJECTS" }).Concat(Enumerable.Range(1, 5).SelectMany(i => new[] { $"Project: Inventory Portal {i}", "Technologies: C#, SQL Server",
            $"- Built C# APIs using SQL Server for project {i}." }))
        .Concat(new[] { "EDUCATION", "Bachelor of Engineering | Example Institute | 2006 - 2010", "Diploma | Example College | 2004 - 2006",
            "CERTIFICATIONS", "Cloud Fundamentals | Example Academy | 2023", "ADDITIONAL INFORMATION", "Supported cross-functional collaboration through problem solving." }));
    internal static TailoredResumeContent Source => UploadedResumeFactParser.Parse(Text);
    internal static ResumeTextReplacement Change(ResumeSourceEvidence target) => new(target.Id, target.Text,
        target.Text.Replace("Built", "Developed", StringComparison.Ordinal), [target.Id], ["performance"], "Emphasize supported source work");
    internal static TailoringPatch Patch
    {
        get
        {
            var targets = ResumePatchGuard.Targets(Source);
            var safe = targets.Where(x => x.Id.StartsWith("SRC-EXP-", StringComparison.Ordinal) && x.Id.EndsWith("BULLET-001", StringComparison.Ordinal))
                .Concat(targets.Where(x => x.Id.StartsWith("SRC-PROJECT-", StringComparison.Ordinal)).Take(3)).Select(Change).ToArray();
            var summary = targets.Single(x => x.Id == "SRC-SUMMARY-001");
            var metric = targets.Single(x => x.Id == "SRC-EXP-001-BULLET-002");
            return new([.. safe, new(summary.Id, summary.Text, "Experienced CUDA developer.", [summary.Id], ["CUDA"], "Unsafe summary"),
                new(metric.Id, metric.Text, metric.Text.Replace("30%", "31%", StringComparison.Ordinal), [metric.Id], ["performance"], "Unsafe metric")], ["SRC-SKILL-001", "SRC-SKILL-002", "SRC-SKILL-999"]);
        }
    }
    internal static byte[] Docx()
    {
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var paragraphs = Text.Split('\n').Select(line => new XElement(W + "p", new XElement(W + "pPr",
            new XElement(W + "pStyle", new XAttribute(W + "val", line.StartsWith("- ", StringComparison.Ordinal) ? "OriginalBullet" : "OriginalBody")),
            line.StartsWith("- ", StringComparison.Ordinal) ? new XElement(W + "numPr", new XElement(W + "ilvl", new XAttribute(W + "val", "0")), new XElement(W + "numId", new XAttribute(W + "val", "1"))) : null,
            new XElement(W + "jc", new XAttribute(W + "val", "left")), new XElement(W + "ind", new XAttribute(W + "left", "240"))),
            Run(line[..(line.Length / 2)], true), Run(line[(line.Length / 2)..], false))).ToArray();
        var document = new XDocument(new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), new XAttribute(XNamespace.Xmlns + "r", r), new XElement(W + "body",
            new XElement(W + "tbl", new XElement(W + "tr", new XElement(W + "tc", paragraphs[0]))), paragraphs.Skip(1),
            new XElement(W + "sectPr", new XElement(W + "headerReference", new XAttribute(W + "type", "default"), new XAttribute(r + "id", "rId4")),
                new XElement(W + "footerReference", new XAttribute(W + "type", "default"), new XAttribute(r + "id", "rId5")),
                new XElement(W + "pgSz", new XAttribute(W + "w", "12240"), new XAttribute(W + "h", "15840")),
                new XElement(W + "pgMar", new XAttribute(W + "top", "720"), new XAttribute(W + "bottom", "720"))))));
        using var result = new MemoryStream();
        using (var zip = new ZipArchive(result, ZipArchiveMode.Create, true))
            foreach (var (name, text) in new[] { ("word/document.xml", document.ToString(SaveOptions.DisableFormatting)),
                ("word/styles.xml", """
                    <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                    <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Carlito" w:hAnsi="Carlito"/><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
                    <w:style w:type="paragraph" w:styleId="OriginalBody"><w:name w:val="Original Body"/><w:pPr><w:spacing w:after="120"/></w:pPr></w:style>
                    <w:style w:type="paragraph" w:styleId="OriginalBullet"><w:name w:val="Original Bullet"/><w:basedOn w:val="OriginalBody"/><w:pPr><w:ind w:left="240" w:hanging="120"/></w:pPr></w:style>
                    </w:styles>
                    """),
                ("word/header1.xml", $"<w:hdr xmlns:w=\"{W}\"><w:p><w:r><w:t>Original Header</w:t></w:r></w:p></w:hdr>"),
                ("word/footer1.xml", $"<w:ftr xmlns:w=\"{W}\"><w:p><w:r><w:t>Original Footer</w:t></w:r></w:p></w:ftr>"),
                ("word/numbering.xml", $"<w:numbering xmlns:w=\"{W}\"><w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"•\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>"),
                ("word/media/logo.bin", "original-logo-bytes"),
                ("_rels/.rels", """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                    """),
                ("word/_rels/document.xml.rels", """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                    <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
                    <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
                    <Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>
                    </Relationships>
                    """),
                ("[Content_Types].xml", """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                    <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="bin" ContentType="application/octet-stream"/>
                    <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                    <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                    <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
                    <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/><Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
                    </Types>
                    """) })
            { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
        return result.ToArray();
    }
    private static XElement Run(string text, bool bold) => new(W + "r", new XElement(W + "rPr",
        new XElement(W + "rFonts", new XAttribute(W + "ascii", "Carlito")), new XElement(W + "sz", new XAttribute(W + "val", "22")),
        bold ? new XElement(W + "b") : new XElement(W + "i")), new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
    internal static Dictionary<string, byte[]> Parts(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(x => x.FullName, x => { using var stream = x.Open(); using var result = new MemoryStream(); stream.CopyTo(result); return result.ToArray(); });
    }
}

internal sealed class MasterResumeMemoryStorage : IResumeStorage
{
    internal Dictionary<string, byte[]> Files { get; } = new() { ["owned.docx"] = MasterResumeFixture.Docx() };
    internal bool FailWrites { get; set; }
    public async Task<string> StoreAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        if (FailWrites) throw new IOException("Sanitized test failure");
        using var result = new MemoryStream(); await content.CopyToAsync(result, cancellationToken);
        var key = Guid.NewGuid().ToString("N") + extension; Files[key] = result.ToArray(); return key;
    }
    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(Files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes.ToArray(), false) : null);
    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) { Files.Remove(storageKey); return Task.CompletedTask; }
}
