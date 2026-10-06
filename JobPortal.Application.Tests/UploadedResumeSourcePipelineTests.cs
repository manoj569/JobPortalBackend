using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Features.AIResume;
using JobPortal.Domain.Entities;
using JobPortal.Infrastructure.AIResume;
using JobPortal.Infrastructure.Services;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class UploadedResumeSourcePipelineTests
{
    // Fictional fixture deliberately differs from the CareerHarbor profile.
    internal const string Resume = """
        Morgan Example
        Email: morgan@example.invalid
        Phone: +91 90000 00000
        Location: Pune, India
        https://example.invalid/morgan
        PROFESSIONAL SUMMARY
        Backend developer with C# and .NET experience.
        TECHNICAL SKILLS
        C# | .NET | ASP.NET | SQL Server | PostgreSQL | Redis | Kafka | Docker | Kubernetes | Azure
        Git | JavaScript | TypeScript | React | Angular | Python | Java | Linux | Terraform | AWS
        WORK EXPERIENCE
        Backend Developer | Cedar Software | Jan 2021 - Dec 2023
        - Built reliable data services using .NET and REST APIs.
        - Reduced processing time by 40%.
        Software Engineer | Willow Systems | Jan 2018 - Dec 2020
        - Built Java APIs using PostgreSQL.
        PROJECTS
        Project: Inventory Portal
        Technologies: C#, .NET, SQL Server
        - Built C# APIs using SQL Server.
        Project: Event Processor
        Technologies: Java, Kafka
        - Built Java services using Kafka.
        EDUCATION
        Bachelor of Engineering | Example Institute | 2014 - 2018
        Diploma in Computing | Example College | 2012 - 2014
        CERTIFICATIONS
        Cloud Fundamentals | Example Academy | 2023
        """;

    [Fact]
    public async Task PdfSourceExtractionPreservesSectionsAndFactualEntries()
    {
        var original = UploadedResumeFactParser.Parse(Resume);
        var download = new ProfessionalResumeDocumentRenderer().Render(original, "pdf");
        await using var stream = new MemoryStream(download.Content);
        var text = await new ResumeTextExtractor().ExtractAsync(stream, ".pdf");
        var source = UploadedResumeFactParser.Parse(text);
        Assert.Equal(original.Contact.Name, source.Contact.Name);
        Assert.Equal(original.Skills, source.Skills);
        Assert.Equal(original.Experience.Select(x => (x.Employer, x.Role, x.StartDate, x.EndDate)),
            source.Experience.Select(x => (x.Employer, x.Role, x.StartDate, x.EndDate)));
        Assert.Equal(original.Projects.Select(x => x.Name), source.Projects.Select(x => x.Name));
        Assert.Equal(original.Projects[0].Technologies, source.Projects[0].Technologies);
        Assert.Equal(original.Projects[1].Technologies, source.Projects[1].Technologies);
        Assert.Equal(original.Education, source.Education);
    }

    [Fact]
    public void ContactSupportsEitherPhoneLocationOrderWithoutAccountSubstitution()
    {
        var source = UploadedResumeFactParser.Parse("Morgan Example\n+91 90000 00000 | Pune, India\nmorgan@example.invalid\nSKILLS\nC#");
        Assert.Equal("Pune, India", source.Contact.Location);
        Assert.Equal("+91 90000 00000", source.Contact.Phone);
    }

    [Fact]
    public void CommonPdfEntryLayoutPreservesLiteralIdentitiesAndDates()
    {
        var source = UploadedResumeFactParser.Parse("""
            Morgan Example
            Pune, India | +91 90000 00000
            morgan@example.invalid | https://example.invalid/morgan
            PROFILE SUMMARY
            Backend developer with C# experience.
            TECHNICAL SKILLS
            Languages: C#, Java, Python
            Data: SQL Server, PostgreSQL
            EXPERIENCE
            Cedar Software Jan 2021 - Present
            Backend Developer, Pune
            Built C# APIs.
            Willow Systems Jan 2018 - Dec 2020
            Software Engineer, Pune
            Built Java APIs.
            PROJECTS
            Inventory Portal | C#, SQL Server
            - Built C# APIs using SQL Server.
            Event Processor | Java, Kafka
            - Built Java services using Kafka.
            EDUCATION
            Bachelor of Engineering 2018
            Example Institute
            Example College 2014
            Diploma in Computing
            """);
        Assert.Equal("Pune, India", source.Contact.Location);
        Assert.Equal(2, source.Experience.Length);
        Assert.Equal("Cedar Software", source.Experience[0].Employer);
        Assert.Equal("Backend Developer", source.Experience[0].Role);
        Assert.Equal("Present", source.Experience[0].EndDate);
        Assert.Equal(2, source.Projects.Length);
        Assert.Equal("Event Processor", source.Projects[1].Name);
        Assert.Equal(["Java", "Kafka"], source.Projects[1].Technologies);
        Assert.Equal(2, source.Education.Length);
        Assert.Equal("Bachelor of Engineering", source.Education[0].Qualification);
        Assert.Equal("", source.Education[0].StartDate);
        Assert.Equal("2018", source.Education[0].EndDate);
    }

    [Fact]
    public async Task OwnedUploadBecomesTypedSourceWithoutManualProfileTables()
    {
        var candidate = new User
        {
            FirstName = "Different", LastName = "Account", Email = "account@example.invalid", Headline = "Different headline",
            SkillsJson = "[\"Invented skill\"]", ResumeStorageKey = "owned.docx", ResumeFileName = "resume.docx",
            ResumeProfile = new CandidateResumeProfile { SkillsJson = "[\"Profile keyword\"]" }
        };
        var parser = new StructuredResumeSourceParser(new DocxStorage(), new ResumeTextExtractor());
        var source = await parser.ParseAsync(candidate, default);
        Assert.Equal("Morgan Example", source.Contact.Name);
        Assert.Equal("morgan@example.invalid", source.Contact.Email);
        Assert.Equal("+91 90000 00000", source.Contact.Phone);
        Assert.Equal("Pune, India", source.Contact.Location);
        Assert.Equal(20, source.Skills.Length);
        Assert.DoesNotContain("Invented skill", source.Skills);
        Assert.Equal(2, source.Experience.Length);
        Assert.Equal("Cedar Software", source.Experience[0].Employer);
        Assert.Equal("Backend Developer", source.Experience[0].Role);
        Assert.Equal("Jan 2021", source.Experience[0].StartDate);
        Assert.Equal("Dec 2023", source.Experience[0].EndDate);
        Assert.Equal(2, source.Experience[0].Bullets.Length);
        Assert.Equal(2, source.Projects.Length);
        Assert.Equal(2, source.Education.Length);
        var catalog = ResumeEvidenceCatalog.Create(source);
        Assert.Contains(catalog, x => x.Id == "SRC-EXP-001-IDENTITY-001" && x.Text.Contains("Cedar Software", StringComparison.Ordinal));
        Assert.Contains(catalog, x => x.Id == "SRC-PROJECT-001-TECH-001" && x.Text == "C#");
        Assert.Contains(catalog, x => x.Id == "SRC-EDU-001" && x.Text.Contains("Bachelor of Engineering", StringComparison.Ordinal));
        Assert.Equal(catalog, ResumeEvidenceCatalog.Create(source));
        var snapshot = JsonSerializer.Deserialize<TailoredResumeContent>(JsonSerializer.Serialize(source))!;
        Assert.Equal(catalog, ResumeEvidenceCatalog.Create(snapshot));
    }

    [Theory]
    [InlineData("SRC-EXP-001-BULLET-001", "Developed reliable data services using .NET and REST APIs.", true)]
    [InlineData("SRC-EXP-002-BULLET-001", "Developed reliable data services using .NET and REST APIs.", false)]
    [InlineData("SRC-EXP-001-IDENTITY-001", "Developed reliable data services using .NET and REST APIs.", false)]
    [InlineData("SRC-EXP-001-BULLET-001", "Built Python APIs.", false)]
    [InlineData("SRC-EXP-001-BULLET-001", "Reduced processing time by 40%.", false)]
    [InlineData("SRC-EXP-001-BULLET-002", "Improved processing performance by 40%.", true)]
    [InlineData("SRC-EXP-001-BULLET-002", "Improved processing performance by 90%.", false)]
    [InlineData("SRC-EXP-001-BULLET-001", "Led a team of 8 developers.", false)]
    public void ScopedEvidenceAuthorizesOnlySupportedClaims(string id, string text, bool valid)
    {
        var source = UploadedResumeFactParser.Parse(Resume);
        // Remove the original metric bullet so copying it cannot bypass the tested rewrite check.
        source = source with { Experience = [source.Experience[0] with { Bullets = [source.Experience[0].Bullets[0]] }, source.Experience[1]] };
        if (id == "SRC-EXP-001-BULLET-002") source = UploadedResumeFactParser.Parse(Resume);
        var generated = source with
        {
            Experience = [source.Experience[0] with { Bullets = [text] }, source.Experience[1]],
            Evidence = [new("experience/0/bullets/0", text, [id])]
        };
        Assert.Equal(valid, AIResumeContentGuard.Validate(source, generated).IsValid);
    }

    [Fact]
    public void ParsedIdentitiesCannotBeInventedEvenWithRealCatalogIds()
    {
        var source = UploadedResumeFactParser.Parse(Resume);
        Assert.True(AIResumeContentGuard.Validate(source, source).IsValid);
        var id = new ResumeProvenance("experience/0", "Invented", ["SRC-EXP-001-IDENTITY-001"]);
        Assert.False(AIResumeContentGuard.Validate(source, source with { Experience = [source.Experience[0] with { Employer = "Invented" }], Evidence = [id] }).IsValid);
        Assert.False(AIResumeContentGuard.Validate(source, source with { Experience = [source.Experience[0] with { Role = "Invented" }], Evidence = [id] }).IsValid);
        Assert.False(AIResumeContentGuard.Validate(source, source with { Experience = [source.Experience[0] with { StartDate = "2010" }], Evidence = [id] }).IsValid);
        Assert.False(AIResumeContentGuard.Validate(source, source with { Projects = [source.Projects[0] with { Name = "Invented" }], Evidence = [id] }).IsValid);
        Assert.False(AIResumeContentGuard.Validate(source, source with { Education = [source.Education[0] with { Qualification = "Invented PhD" }], Evidence = [id] }).IsValid);
        Assert.False(AIResumeContentGuard.Validate(source, source with { Certifications = [source.Certifications[0] with { Name = "Invented" }], Evidence = [id] }).IsValid);
    }

    [Fact]
    public void AmbiguousTimelineEntryRemainsAdditionalEvidenceRatherThanAnInventedRole()
    {
        var source = UploadedResumeFactParser.Parse("""
            Morgan Example
            SKILLS
            C#
            EXPERIENCE
            Example Academy 2018 - 2019
            Practical computing course
            Built C# APIs.
            Cedar Software 2020 - Present
            Backend Developer, Pune
            Built C# APIs.
            """);
        Assert.Single(source.Experience);
        Assert.Equal("Cedar Software", source.Experience[0].Employer);
        Assert.Contains("Practical computing course", source.AdditionalInfo);
        var text = "Developed C# APIs.";
        var generated = source with
        {
            Experience = [source.Experience[0] with { Bullets = [text] }],
            Evidence = [new("experience/0/bullets/0", text, ["SRC-ADDITIONAL-003"])]
        };
        Assert.False(AIResumeContentGuard.Validate(source, generated).IsValid);
    }

    [Fact]
    public void AmbiguousIdentityLayoutFailsBeforeItCanBecomeASession()
    {
        Assert.Throws<InvalidDataException>(() => UploadedResumeFactParser.Parse("Morgan Example\nWORK EXPERIENCE\nUnknown ordering of company and role\n2018 - 2023"));
    }

    internal sealed class DocxStorage : IResumeStorage
    {
        private readonly Dictionary<string, byte[]> copies = [];
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            if (copies.TryGetValue(storageKey, out var bytes)) return Task.FromResult<Stream?>(new MemoryStream(bytes, false));
            Assert.Equal("owned.docx", storageKey);
            var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var document = new XDocument(new XElement(w + "document", new XElement(w + "body", Resume.Split('\n')
                    .Select(line => new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", line)))))));
                using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), Encoding.UTF8);
                document.Save(writer);
            }
            stream.Position = 0;
            return Task.FromResult<Stream?>(stream);
        }
        public async Task<string> StoreAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream(); await content.CopyToAsync(memory, cancellationToken);
            var key = Guid.NewGuid().ToString("N") + extension; copies[key] = memory.ToArray(); return key;
        }
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) { copies.Remove(storageKey); return Task.CompletedTask; }
    }

}
