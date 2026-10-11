using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PwcCategoryIngestionTests
{
    [Theory]
    [InlineData("IN_Associate_Software Engineer_Advisory", null, "software-engineering")]
    [InlineData("IN_Senior Associate_Tax_Advisory", "Tax", "finance-accounting")]
    [InlineData("IN_Manager_Human Resources", null, "human-resources-recruitment")]
    [InlineData("IN_Manager_SAP_Advisory", "SAP", "general")]
    [InlineData("Mechanical Engineer", null, "general")]
    [InlineData("IN_Associate_Data Engineer_Data and Analytics", null, "data-science-analytics")]
    [InlineData("IN_Manager_SAP GRC_SAP_Advisory", "SAP", "cybersecurity")]
    [InlineData("Associate", "Assurance", "finance-accounting")]
    [InlineData("Software Developer", "Tax", "software-engineering")]
    [InlineData("IN_Associate_.NET Developer_Advisory", null, "software-engineering")]
    [InlineData("Java Developer", "Tax", "software-engineering")]
    [InlineData("IN_Associate_Java Developer_Tax_Advisory", "Tax", "software-engineering")]
    [InlineData("IN_Associate_Data Engineer_Finance_Advisory", "Finance", "data-science-analytics")]
    [InlineData("IN_Associate_Finance_Advisory", null, "finance-accounting")]
    [InlineData("IN_Manager_HR_Advisory", null, "human-resources-recruitment")]
    [InlineData("IN_Associate_Manual Testing_Advisory", null, "quality-assurance-testing")]
    [InlineData("IN_Manager_Legal_Advisory", null, "legal-compliance")]
    [InlineData("Associate", "Not Applicable", null)]
    [InlineData("Tax and Cybersecurity Advisor", null, null)]
    public void AccuratePwcRoleClassification(string title, string? specialism, string? expected) =>
        Assert.Equal(expected, new ExternalJobCategoryClassifier().Classify(new()
        { CompanyName = "PwC", Title = title, ExternalCategory = specialism }));

    [Fact]
    public void PwcRulesAreScopedAndUseVerifiedBoardIdentityForCompanyNameVariants()
    {
        var classifier = new ExternalJobCategoryClassifier();
        Assert.Null(classifier.Classify(new() { CompanyName = "Other company", Title = "SAP Consultant" }));
        Assert.Equal("general", classifier.Classify(new() { CompanyName = "PwC India", Title = "SAP Consultant",
            ApplicationUrl = "https://pwc.wd3.myworkdayjobs.com/en-US/Global_Experienced_Careers/job/Pune/REQ1" }));
    }

    [Fact]
    public void SpecialismComesOnlyFromLabelledPwcMetadata()
    {
        var source = new JobSource { Company = new Company { Name = "PwC" } };
        var listing = new WorkdayJobSourceProvider.ListingEntry("Associate", "/job/Pune/Associate_REQ1", "Pune", null, "REQ1");
        var target = new WorkdayJobSourceProvider.WorkdayTarget(new("https://pwc.wd3.myworkdayjobs.com/"),
            new("https://pwc.wd3.myworkdayjobs.com/wday/cxs/pwc/Global_Experienced_Careers"), "pwc", "Global_Experienced_Careers");
        string Detail(string description) => JsonSerializer.Serialize(new { jobPostingInfo = new
        { title = "Associate", jobReqId = "REQ1", jobDescription = description, location = "Pune, India" } });
        var job = WorkdayJobSourceProvider.ParseDetail(Detail("<p><b>Specialism</b></p>Assurance<p><b>Management Level</b></p>Associate"), target, source, listing);
        Assert.Equal("Assurance", job!.ExternalCategory);
        Assert.Null(WorkdayJobSourceProvider.ParseDetail(Detail("<p>Our teams work in Assurance and Software Engineering.</p>"), target, source, listing)!.ExternalCategory);
        Assert.Null(WorkdayJobSourceProvider.ParseDetail(Detail("<p><b>Specialism</b></p>Assurance<p>Details</p>"),
            target with { Tenant = "other" }, source, listing)!.ExternalCategory);
    }

    [Fact]
    public async Task ClassifiablePwcJobsPersistOnceAndUnclassifiedJobsBlockReconciliation()
    {
        using var f = new JobSourceFixture();
        f.Company.Name = "PwC";
        f.Source.AtsType = AtsType.Workday;
        string[] slugs = ["software-engineering", "finance-accounting", "human-resources-recruitment", "general", "data-science-analytics", "cybersecurity"];
        foreach (var slug in slugs)
            f.Context.Categories.Add(new Category { Slug = slug, Name = slug == "general" ? "General" : slug });
        await f.Context.SaveChangesAsync();
        string[] titles = ["IN_Associate_Software Engineer", "Tax Accountant", "Human Resources Manager", "SAP Consultant", "Mechanical Engineer", "Data Engineer", "Cybersecurity Analyst"];
        var provider = new SnapshotProvider(titles.Select((title, index) => new RawExternalJob
        {
            ExternalId = $"REQ{index}", Title = title, CompanyName = "PwC", CountryCodes = ["IN"],
            Location = "Pune, India", Description = "Deliver professional services in the stated discipline.",
            ApplicationUrl = $"https://pwc.wd3.myworkdayjobs.com/en-US/Global_Experienced_Careers/job/Pune/REQ{index}"
        }).ToArray());
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, f.Locks, canonical);
        var resolver = new JobSourceCategoryResolver(new Monitor(), new CategoryManagementRepository(f.Context), new ExternalJobCategoryClassifier());
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit, TimeProvider.System, resolver,
            new ExternalJobNormalizer(), jobRepository: jobs,
            publicationPolicy: new JobSourcePublicationPolicy(Options.Create(new JobAggregationOptions()), TimeProvider.System));
        var first = await runner.RunAsync(f.Source.Id);
        Assert.True(first.Succeeded);
        Assert.Equal(7, first.Created);
        Assert.Equal(0, first.Existing);
        Assert.Equal(0, first.ValidationRejected);
        var ids = await f.Context.Jobs.AsNoTracking().OrderBy(x => x.ExternalJobId).Select(x => x.Id).ToArrayAsync();
        var repeated = await runner.RunAsync(f.Source.Id);
        Assert.True(repeated.Succeeded);
        Assert.Equal(0, repeated.Created);
        Assert.Equal(7, repeated.Existing);
        Assert.Equal(ids, await f.Context.Jobs.AsNoTracking().OrderBy(x => x.ExternalJobId).Select(x => x.Id).ToArrayAsync());
        var expected = new[] { slugs[0], slugs[1], slugs[2], slugs[3], slugs[3], slugs[4], slugs[5] };
        var saved = await f.Context.Jobs.AsNoTracking().Include(x => x.Category).OrderBy(x => x.ExternalJobId).ToArrayAsync();
        Assert.Equal(expected, saved.Select(x => x.Category.Slug).ToArray());
        provider.Jobs = [provider.Jobs[0] with { ExternalId = "UNKNOWN", Title = "Unclassified Associate",
            ApplicationUrl = "https://pwc.wd3.myworkdayjobs.com/en-US/Global_Experienced_Careers/job/Pune/UNKNOWN" }];
        var unsafeRun = await runner.RunAsync(f.Source.Id);
        Assert.False(unsafeRun.Succeeded);
        Assert.Equal(0, unsafeRun.Created);
        Assert.Equal(1, unsafeRun.ValidationRejected);
        Assert.Equal(1, unsafeRun.ValidationReasonCounts!["MissingCategory"]);
        Assert.Equal(0, unsafeRun.ProviderSkipped);
        Assert.Equal(0, unsafeRun.Closed);
        Assert.Equal(7, await f.Context.Jobs.CountAsync());
    }

    private sealed class Monitor : IOptionsMonitor<JobAggregationOptions>
    {
        public JobAggregationOptions CurrentValue { get; } = new();
        public JobAggregationOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<JobAggregationOptions, string?> listener) => null;
    }
    private sealed class SnapshotProvider(RawExternalJob[] jobs) : ICompleteExternalJobProvider
    {
        public RawExternalJob[] Jobs { get; set; } = jobs;
        public AtsType AtsType => AtsType.Workday;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<RawExternalJob>>(Jobs);
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) => Task.FromResult(new ExternalJobSourceSnapshot(Jobs, 0, true));
    }
}
