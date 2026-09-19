using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ExternalJobNormalizationTests
{
    private readonly ExternalJobNormalizer normalizer = new();

    [Theory]
    [InlineData("  .NET   Developer  ", ".NET Developer")]
    [InlineData(" C#\tDeveloper ", "C# Developer")]
    [InlineData(" C++\nDeveloper ", "C++ Developer")]
    [InlineData(" Node.js  Developer ", "Node.js Developer")]
    public void DisplayTitlePreservesSymbolsAndCase(string input, string expected) =>
        Assert.Equal(expected, normalizer.Normalize(new() { Title = input }).Title);

    [Theory]
    [InlineData(" Pune,   Maharashtra ", "Pune, Maharashtra")]
    [InlineData("New York,NY", "New York, NY")]
    [InlineData("Pune ,, , India", "Pune, India")]
    [InlineData(" Remote - US ", "Remote - US")]
    [InlineData("Washington D.C. / Arlington", "Washington D.C. / Arlington")]
    [InlineData(null, null)]
    [InlineData(" , , ", null)]
    public void LocationIsConservative(string? input, string? expected) =>
        Assert.Equal(expected, normalizer.Normalize(new() { Location = input }).Location);

    [Fact]
    public void CopyIsDeterministicAndIdempotentWithoutMutatingInput()
    {
        var raw = new RawExternalJob
        {
            Title = "  .NET   Developer ", CompanyName = " Acme\t Corp ", Location = " Pune,India ",
            Description = " First   paragraph.\r\n\r\n\r\n Second paragraph. ",
            Requirements = "  C#\n .NET ", Responsibilities = " ", Benefits = null,
            ApplicationUrl = " https://example.test/Job?utm_source=A ", ExternalId = "  A-1  "
        };
        var result = normalizer.Normalize(raw);
        Assert.NotSame(raw, result);
        Assert.Equal("  .NET   Developer ", raw.Title);
        Assert.Equal("Acme Corp", result.CompanyName);
        Assert.Equal("First paragraph.\n\nSecond paragraph.", result.Description);
        Assert.Equal("C#\n.NET", result.Requirements);
        Assert.Null(result.Responsibilities);
        Assert.Null(result.Benefits);
        Assert.Equal("https://example.test/Job?utm_source=A", result.ApplicationUrl);
        Assert.Equal("A-1", result.ExternalId);
        Assert.Equal(result, normalizer.Normalize(raw));
        Assert.Equal(result, normalizer.Normalize(result));
    }

    [Theory]
    [InlineData("<p>Hello &amp; welcome</p><ul><li>One</li><li>Two</li></ul>", "Hello & welcome\n\nOne\n\nTwo")]
    [InlineData("&lt;p&gt;Hello&nbsp;world&lt;/p&gt;", "Hello world")]
    [InlineData("<style>.hidden{}</style><p>Visible</p><script>alert(1)</script>", "Visible")]
    [InlineData("<p title='a > b'>Hello <b>world</b></p>", "Hello world")]
    [InlineData("<!-- secret -->Visible<script>unclosed secret", "Visible")]
    [InlineData("<p>Malformed <b>but readable", "Malformed but readable")]
    [InlineData("2 < 3 and 5 > 4", "2 < 3 and 5 > 4")]
    [InlineData("<script>secret", null)]
    [InlineData("<p>Use List&lt;T&gt; &amp; C#</p>", "Use List<T> & C#")]
    [InlineData("Use List&lt;T&gt;", "Use List<T>")]
    [InlineData("&lt;p&gt;Use List&amp;lt;T&amp;gt;&lt;/p&gt;", "Use List<T>")]
    [InlineData(null, null)]
    public void ExplicitHtmlIsExtractedWithoutExecutingOrExposingScripts(string? input, string? expected)
    {
        var result = normalizer.Normalize(new() { Description = input, DescriptionIsHtml = true });
        Assert.Equal(expected, result.Description);
        Assert.False(result.DescriptionIsHtml);
        Assert.Equal(result, normalizer.Normalize(result));
    }

    [Fact]
    public void PlainTextIsNotTreatedAsHtmlOrUsedForWorkplaceInference()
    {
        var result = normalizer.Normalize(new() { Description = "Use List<T> &amp; remote tools", Location = "Remote - US", Title = "Remote intern" });
        Assert.Equal("Use List<T> &amp; remote tools", result.Description);
        Assert.Null(result.WorkplaceType);
        Assert.Null(result.EmploymentType);
    }

    [Theory]
    [InlineData("full time", EmploymentType.FullTime)]
    [InlineData("FULL-TIME", EmploymentType.FullTime)]
    [InlineData("FullTime", EmploymentType.FullTime)]
    [InlineData("part-time", EmploymentType.PartTime)]
    [InlineData("part time", EmploymentType.PartTime)]
    [InlineData("contract", EmploymentType.Contract)]
    [InlineData("Contractor", EmploymentType.Contract)]
    [InlineData("intern", EmploymentType.Internship)]
    [InlineData("internship", EmploymentType.Internship)]
    [InlineData("freelance", EmploymentType.Freelance)]
    [InlineData("temporary", EmploymentType.Temporary)]
    [InlineData("full-time or contract", null)]
    [InlineData("permanent", null)]
    [InlineData(null, null)]
    public void EmploymentUsesOnlyKnownStructuredValues(string? text, EmploymentType? expected) =>
        Assert.Equal(expected, normalizer.Normalize(new() { EmploymentTypeText = text }).EmploymentType);

    [Theory]
    [InlineData("REMOTE", WorkplaceType.Remote)]
    [InlineData("hybrid", WorkplaceType.Hybrid)]
    [InlineData("on-site", WorkplaceType.OnSite)]
    [InlineData("OnSite", WorkplaceType.OnSite)]
    [InlineData("office", WorkplaceType.OnSite)]
    [InlineData("remote - us", null)]
    [InlineData("remote/hybrid", null)]
    [InlineData("unspecified", null)]
    [InlineData(null, null)]
    public void WorkplaceUsesOnlyUnambiguousStructuredValues(string? text, WorkplaceType? expected) =>
        Assert.Equal(expected, normalizer.Normalize(new() { WorkplaceTypeText = text }).WorkplaceType);

    [Fact]
    public void ExplicitEnumsWinAndInvalidEnumsAreNotPersisted()
    {
        var result = normalizer.Normalize(new() { EmploymentType = EmploymentType.PartTime, EmploymentTypeText = "full-time",
            WorkplaceType = WorkplaceType.Hybrid, WorkplaceTypeText = "remote" });
        Assert.Equal(EmploymentType.PartTime, result.EmploymentType);
        Assert.Equal(WorkplaceType.Hybrid, result.WorkplaceType);
        result = normalizer.Normalize(new() { EmploymentType = (EmploymentType)999, WorkplaceType = (WorkplaceType)999 });
        Assert.Null(result.EmploymentType);
        Assert.Null(result.WorkplaceType);
    }
}

public sealed class ExternalJobCategoryMappingTests
{
    [Fact]
    public async Task PriorityIsExplicitThenExternalThenSourceThenNull()
    {
        using var f = new JobSourceFixture();
        var explicitCategory = new Category { Name = "Explicit", Slug = "explicit" };
        var externalCategory = new Category { Name = "External", Slug = "external" };
        f.Context.AddRange(explicitCategory, externalCategory);
        await f.Context.SaveChangesAsync();
        f.Map(f.Category.Id.ToString());
        f.MapExternal(" Software   Engineering ", externalCategory.Id.ToString());
        var raw = new RawExternalJob { ExternalCategory = "software engineering", CategoryId = explicitCategory.Id };
        Assert.Equal(explicitCategory.Id, await f.Resolver.ResolveCategoryIdAsync(f.Source, raw));
        Assert.Equal(externalCategory.Id, await f.Resolver.ResolveCategoryIdAsync(f.Source, raw with { CategoryId = Guid.NewGuid() }));
        Assert.Equal(f.Category.Id, await f.Resolver.ResolveCategoryIdAsync(f.Source, new RawExternalJob()));
        f.Map(null);
        Assert.Null(await f.Resolver.ResolveCategoryIdAsync(f.Source, new RawExternalJob()));
        Assert.Equal(3, await f.Context.Categories.CountAsync());
    }

    [Theory]
    [InlineData("bad-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData(null)]
    public async Task InvalidOptionalMappingFallsBackWithoutCreatingCategory(string? mapping)
    {
        using var f = new JobSourceFixture();
        f.Map(f.Category.Id.ToString());
        f.MapExternal("engineering", mapping);
        Assert.Equal(f.Category.Id, await f.Resolver.ResolveCategoryIdAsync(f.Source, new RawExternalJob { ExternalCategory = "Engineering" }));
        Assert.Single(await f.Context.Categories.ToArrayAsync());
    }

    [Fact]
    public async Task DeletedAndAmbiguousMappingsFailClosed()
    {
        using var f = new JobSourceFixture();
        f.MapExternal("engineering", f.Category.Id.ToString());
        f.MapExternal(" engineering ", Guid.NewGuid().ToString());
        var raw = new RawExternalJob { ExternalCategory = "engineering" };
        Assert.Null(await f.Resolver.ResolveCategoryIdAsync(f.Source, raw));
        f.Context.Categories.Remove(f.Category);
        await f.Context.SaveChangesAsync();
        Assert.Null(await f.Resolver.ResolveCategoryIdAsync(f.Source, raw with { CategoryId = f.Category.Id }));
    }
}

public sealed class ExternalJobNormalizationPipelineTests
{
    [Fact]
    public async Task SharedManualRunnerNormalizesMapsCreatesDraftAndRefreshesDuplicate()
    {
        using var f = new JobSourceFixture();
        f.MapExternal("engineering", f.Category.Id.ToString());
        f.Provider.Jobs = [new() { Title = " .NET   Developer ", CompanyName = " Acme ", Location = " Pune,India ",
            Description = "<p>Build &amp; test</p>", DescriptionIsHtml = true, ExternalCategory = " Engineering ",
            EmploymentTypeText = "full-time", WorkplaceTypeText = "remote", ApplicationUrl = " https://example.test/jobs/1 " }];
        var result = await f.Service.RunAsync(f.Source.Id);
        Assert.Equal(1, result.Created);
        var job = await f.Context.Jobs.SingleAsync();
        Assert.Equal(".NET Developer", job.Title);
        Assert.Equal("Pune, India", job.Location);
        Assert.Equal("Build & test", job.Description);
        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Equal(EmploymentType.FullTime, job.EmploymentType);
        Assert.Equal(WorkplaceType.Remote, job.WorkplaceType);
        Assert.Equal(f.Category.Id, job.CategoryId);
        job.Description = "Curated";
        job.Requirements = "Required";
        job.Benefits = "Benefits";
        job.Responsibilities = "Responsibilities";
        job.Status = JobStatus.Published;
        job.MinimumSalary = 123;
        job.MaximumSalary = 456;
        job.WorkplaceType = WorkplaceType.Hybrid;
        job.PublishedAtUtc = JobSourceFixture.Now.AddDays(-1);
        job.Referral = new JobReferral { Job = job, JobId = job.Id, ApprovalStatus = JobReferralApprovalStatus.Approved };
        job.RecruiterContact = new JobRecruiterContact { Job = job, JobId = job.Id, ContactName = "Curated", IsSharingApproved = true };
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        f.MapExternal("engineering", null);
        // Different URL forces fingerprint/fuzzy path after normalization; no category needed.
        f.Provider.Jobs = [f.Provider.Jobs.Single() with { ApplicationUrl = "https://example.test/other", Description = "Replacement" }];
        result = await f.Service.RunAsync(f.Source.Id);
        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Created);
        f.Context.ChangeTracker.Clear();
        var saved = await new JobRepository(f.Context).GetByIdAsync(job.Id);
        Assert.NotNull(saved);
        Assert.Equal("Curated", saved.Description);
        Assert.Equal("Required", saved.Requirements);
        Assert.Equal("Benefits", saved.Benefits);
        Assert.Equal("Responsibilities", saved.Responsibilities);
        Assert.Equal(JobStatus.Published, saved.Status);
        Assert.Equal(123, saved.MinimumSalary);
        Assert.Equal(456, saved.MaximumSalary);
        Assert.Equal(WorkplaceType.Hybrid, saved.WorkplaceType);
        Assert.Equal(job.PublishedAtUtc, saved.PublishedAtUtc);
        Assert.Equal(JobSourceFixture.Now, saved.LastSeenAtUtc);
        Assert.Equal(f.Category.Id, saved.CategoryId);
        Assert.Equal(f.Company.Id, saved.CompanyId);
        Assert.Equal(JobReferralApprovalStatus.Approved, saved.Referral!.ApprovalStatus);
        Assert.True(saved.RecruiterContact!.IsSharingApproved);
        Assert.Single(await f.Context.Jobs.ToArrayAsync());
    }

    [Fact]
    public async Task UnmappedNewAndMalformedRecordsSkipWithoutLosingValidRecords()
    {
        using var f = new JobSourceFixture();
        f.Provider.Jobs = [new() { Title = " New ", CompanyName = " Acme " }, new(),
            new() { Title = "Good", CompanyName = "Acme", CategoryId = f.Category.Id },
            new() { Title = "Bad URL", CompanyName = "Acme", CategoryId = f.Category.Id, ApplicationUrl = "javascript:bad" }];
        var result = await f.Runner.RunAsync(f.Source.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(4, result.TotalReceived);
        Assert.Equal(3, result.Skipped);
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Failed);
    }
}
