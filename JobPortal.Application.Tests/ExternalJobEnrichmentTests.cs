using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ExternalJobEnrichmentTests
{
    private static RawExternalJob Enrich(string text, string title = "Engineer", string? requirements = null) =>
        new ExternalJobMetadataEnricher().Enrich(new RawExternalJob { Title = title, Description = text, Requirements = requirements });

    [Theory]
    [InlineData("This is a full-time position.", EmploymentType.FullTime)]
    [InlineData("part-time position", EmploymentType.PartTime)]
    [InlineData("employment type: full-time", EmploymentType.FullTime)]
    [InlineData("6 month contract", EmploymentType.Contract)]
    [InlineData("contract position", EmploymentType.Contract)]
    [InlineData("Experience working with full-time employees", null)]
    [InlineData("Full-time or contract position", null)]
    [InlineData("Not a full-time position", null)]
    [InlineData("Full-time position or contract", null)]
    [InlineData("Full-time position is not available", null)]
    [InlineData("Internship coordination is part of your duties", null)]
    public void EmploymentRequiresJobAssertion(string text, EmploymentType? expected) => Assert.Equal(expected, Enrich(text).EmploymentType);

    [Theory]
    [InlineData("7+ years of professional software development experience.", 7, null, ExperienceLevel.Senior)]
    [InlineData("5+ years of recruiting experience", 5, null, ExperienceLevel.Mid)]
    [InlineData("3-5 years of experience", 3, 5, ExperienceLevel.Mid)]
    [InlineData("3 to 5 years of experience", 3, 5, ExperienceLevel.Mid)]
    [InlineData("minimum 4 years experience", 4, null, ExperienceLevel.Mid)]
    [InlineData("at least 2 years of experience", 2, null, ExperienceLevel.Junior)]
    [InlineData("0-1 years experience", 0, 1, ExperienceLevel.Entry)]
    [InlineData("$165,000 - $210,000", null, null, null)]
    [InlineData("3 days per week in office", null, null, null)]
    [InlineData("Our company is 10 years old", null, null, null)]
    [InlineData("We serve 500 customers with 100 GPUs", null, null, null)]
    [InlineData("7+ years experience. 2+ years experience.", null, null, null)]
    [InlineData("5-3 years experience", null, null, null)]
    [InlineData("7+ years experience is not required", null, null, null)]
    [InlineData("7+ years experience preferred", null, null, null)]
    public void NumericExperienceIsExplicit(string text, int? min, int? max, ExperienceLevel? level)
    {
        var result = Enrich(text);
        Assert.Equal(min, result.MinimumExperienceYears);
        Assert.Equal(max, result.MaximumExperienceYears);
        Assert.Equal(level, result.ExperienceLevel);
    }

    [Fact]
    public void BareYearsOnlyInRequirements()
    {
        Assert.Null(Enrich("10+ years").MinimumExperienceYears);
        Assert.Equal(10, Enrich("", requirements: "10+ years").MinimumExperienceYears);
    }

    [Theory]
    [InlineData("This is a fully remote position", WorkplaceType.Remote)]
    [InlineData("hybrid role", WorkplaceType.Hybrid)]
    [InlineData("3 days per week in office", WorkplaceType.Hybrid)]
    [InlineData("onsite position", WorkplaceType.OnSite)]
    [InlineData("on-site role", WorkplaceType.OnSite)]
    [InlineData("Experience managing remote engineering teams", null)]
    [InlineData("Experience collaborating with remote and office-based teams", null)]
    [InlineData("remote or onsite role", null)]
    [InlineData("Office collaboration is important", null)]
    [InlineData("Remote position is not available", null)]
    [InlineData("Hybrid role or remote", null)]
    public void WorkplaceRequiresExplicitArrangement(string text, WorkplaceType? expected) => Assert.Equal(expected, Enrich(text).WorkplaceType);

    [Theory]
    [InlineData("Bachelor's degree required", "Graduate")]
    [InlineData("B.Tech required", "B.Tech")]
    [InlineData("B.E. required", "B.E.")]
    [InlineData("B.Sc required", "B.Sc")]
    [InlineData("Diploma required", "Diploma")]
    [InlineData("ITI required", "ITI")]
    [InlineData("HSC required", "HSC")]
    [InlineData("SSC required", "SSC")]
    [InlineData("We value a degree of independence", null)]
    [InlineData("Bachelor's degree preferred", null)]
    public void EducationRequiresRequirement(string text, string? expected) => Assert.Equal(expected, Enrich(text).EducationRequirement);

    [Fact]
    public void RemoteLocationAndAliasesRemainSeparate()
    {
        var result = new ExternalJobMetadataEnricher().Enrich(new RawExternalJob { Title = "Engineer", Location = "Remote - India" });
        Assert.Equal("India", result.Location);
        Assert.Equal(WorkplaceType.Remote, result.WorkplaceType);
        result = Enrich("work remotely from anywhere in India");
        Assert.Equal("India", result.Location);
        Assert.Equal(WorkplaceType.Remote, result.WorkplaceType);
        Assert.Equal("Bengaluru", Enrich("Location: Bangalore").Location);
        Assert.Equal("Mumbai", Enrich("based in Bombay").Location);
    }

    [Fact]
    public void ProviderValuesWinAndSalaryExpiryAreNeverInferred()
    {
        var source = new RawExternalJob
        {
            Title = "Engineer", Location = "Pune", EmploymentType = EmploymentType.Contract,
            WorkplaceType = WorkplaceType.OnSite, MinimumExperienceYears = 2, MaximumExperienceYears = 4,
            ExperienceLevel = ExperienceLevel.Junior, EducationRequirement = "Diploma",
            Description = "This is a full-time position. Fully remote role. 7+ years experience. Based in San Francisco. Bachelor's degree required. $165,000 - $210,000. INR 15 lakh per annum."
        };
        var result = new ExternalJobMetadataEnricher().Enrich(source);
        Assert.Equal(source, result);
        Assert.Null(result.SalaryMin);
        Assert.Null(result.SalaryMax);
        Assert.Null(result.ExpiresAtUtc);
    }

    [Theory]
    [InlineData("Senior Recruiter, GTM & Business", "human-resources-recruitment")]
    [InlineData("Staff Software Engineer - AI Compute, Together Cloud", "software-engineering")]
    [InlineData("QA Engineer", "quality-assurance-testing")]
    [InlineData("DevOps Engineer", "devops-cloud-engineering")]
    [InlineData("Product Manager", "product-management")]
    [InlineData("Product Engineer", null)]
    [InlineData("Security-minded software engineer", "software-engineering")]
    [InlineData("Recruiting-platform engineer", null)]
    [InlineData("Machine Learning Engineer", "ai-machine-learning")]
    [InlineData("Data Analyst", "data-science-analytics")]
    [InlineData("UX Designer", "ui-ux-design")]
    [InlineData("Security Analyst", "cybersecurity")]
    [InlineData("Systems Administrator", "it-support-administration")]
    [InlineData("Account Executive", "sales-business-development")]
    [InlineData("Accountant", "finance-accounting")]
    [InlineData("Customer Success Manager", "customer-success-support")]
    [InlineData("Legal Counsel", "legal-compliance")]
    [InlineData("Marketing Specialist", "marketing")]
    [InlineData("Operations Manager", "operations")]
    [InlineData("Recruiting", "human-resources-recruitment")]
    [InlineData("Sales", "sales-business-development")]
    [InlineData("Finance", "finance-accounting")]
    public void StrongTitlesClassify(string title, string? slug) => Assert.Equal(slug,
        new ExternalJobCategoryClassifier().Classify(new RawExternalJob { Title = title }));

    [Theory]
    [InlineData("Civil Engineer", "Infrastructure")]
    [InlineData("Mechanical Designer", "Design")]
    public void BroadDepartmentsDoNotImplyTechnologyRoles(string title, string department) => Assert.Null(
        new ExternalJobCategoryClassifier().Classify(new RawExternalJob { Title = title, ExternalCategory = department }));

    [Fact]
    public void VagueHeadquartersDoesNotInventGeographicLocation() => Assert.Null(Enrich("Based in our headquarters.").Location);

    [Fact]
    public void InternshipTitleIsExplicit() => Assert.Equal(EmploymentType.Internship, Enrich("", "Summer Intern").EmploymentType);

    [Fact]
    public void SanitizedRecruiterExample()
    {
        var result = Enrich("This role is based out of our Headquarters in San Francisco.\n5+ years of business or GTM recruiting experience.\nThe US base salary range for this full-time position is $165,000 - $210,000.", "Senior Recruiter, GTM & Business");
        Assert.Equal(EmploymentType.FullTime, result.EmploymentType);
        Assert.Equal(5, result.MinimumExperienceYears);
        Assert.Equal("San Francisco", result.Location);
        Assert.Null(result.MaximumExperienceYears);
        Assert.Null(result.SalaryMin);
        Assert.Null(result.ExpiresAtUtc);
        Assert.Null(result.WorkplaceType);
    }

    [Fact]
    public async Task CategoryPrecedenceAndMissingTaxonomyFailClosed()
    {
        using var f = new JobSourceFixture();
        var hr = new Category { Name = "Human Resources", Slug = "existing-hr" };
        f.Context.Categories.Add(hr);
        await f.Context.SaveChangesAsync();
        var options = new JobAggregationOptions();
        options.SourceCategories[f.Source.Id.ToString("D")] = f.Category.Id.ToString();
        options.CategoryMappings["ATS"] = f.Category.Id.ToString();
        var resolver = new JobSourceCategoryResolver(new Monitor(options), new CategoryManagementRepository(f.Context), new ExternalJobCategoryClassifier());
        var raw = new RawExternalJob { Title = "Senior Recruiter" };
        Assert.Equal(hr.Id, await resolver.ResolveCategoryIdAsync(f.Source, raw));
        Assert.Equal(f.Category.Id, await resolver.ResolveCategoryIdAsync(f.Source, raw with { ExternalCategory = "ATS" }));
        Assert.Equal(hr.Id, await resolver.ResolveCategoryIdAsync(f.Source, raw with { CategoryId = hr.Id, ExternalCategory = "ATS" }));
        Assert.Equal(f.Category.Id, await resolver.ResolveCategoryIdAsync(f.Source, raw with { Title = "Unclassified professional" }));
        Assert.Null(await resolver.ResolveCategoryIdAsync(f.Source, raw with { Title = "Legal Counsel" }));
        Assert.Equal(f.Category.Id, await resolver.ResolveCategoryIdAsync(f.Source));
    }

    private sealed class Monitor(JobAggregationOptions value) : IOptionsMonitor<JobAggregationOptions>
    {
        public JobAggregationOptions CurrentValue => value;
        public JobAggregationOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<JobAggregationOptions, string?> listener) => null;
    }
}
