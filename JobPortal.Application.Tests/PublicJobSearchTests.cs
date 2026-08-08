using FluentValidation.TestHelper;
using JobPortal.API.Controllers;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PublicJobSearchTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PublicJobDetailsIncludesApplicationUrlButSummaryContractDoesNot()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        var service = new PublicJobService(
            fixture.Repository,
            new PublicJobQueryValidator());
        var controller = new PublicJobsController(service);

        var action = await controller.Details(fixture.Featured.Slug, default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<PublicJobDetails>>(ok.Value);
        Assert.Equal(fixture.Featured.ApplicationUrl, response.Data.ApplicationUrl);
        Assert.Null(typeof(PublicJobSummary).GetProperty("ApplicationUrl"));
    }

    [Fact]
    public void SearchAndFacetQueriesAreSqlServerTranslatable()
    {
        using var context = new JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseSqlServer(
                    "Server=(localdb)\\MSSQLLocalDB;Database=TranslationOnly;Trusted_Connection=True")
                .Options);
        var repository = new PublicJobRepository(context, new FixedTimeProvider(Now));
        var query = new PublicJobQuery(
            Keyword: "engineer",
            Locations: ["Pune", "Mumbai"],
            WorkModes: [WorkplaceType.Hybrid],
            MinExperienceYears: 1,
            MaxExperienceYears: 5,
            Departments: ["Engineering"],
            RoleCategories: ["Software Development"],
            EmploymentTypes: [EmploymentType.FullTime],
            MinAmount: 100,
            MaxAmount: 2000,
            InternshipDurationMonths: [3],
            FlexibleDuration: false,
            EducationRequirements: ["B.Tech/B.E."],
            CompanyTypes: [CompanyType.Startup],
            CompanyIds: [Guid.NewGuid()],
            Industries: ["Technology"],
            PostedByTypes: [PostedByType.Company],
            FreshnessDays: 7,
            FeaturedOnly: true,
            CompanyName: "alpha",
            CategoryName: "engineering");

        var searchSql = repository.FilteredJobsQuery(query)
            .Select(PublicJobProjections.Summary)
            .ToQueryString();
        var facetSql = repository.LocationFacetQuery(query).ToQueryString();

        Assert.Contains("JobSkills", searchSql, StringComparison.Ordinal);
        Assert.Contains("PublishedAtUtc", searchSql, StringComparison.Ordinal);
        Assert.Contains("LOWER", searchSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", facetSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchEnforcesEveryPublicVisibilityRuleAndAllowsNoExpiry()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        var unpublished = fixture.Copy("unpublished", JobStatus.Published);
        unpublished.PublishedAtUtc = null;
        fixture.Context.Jobs.AddRange(
            fixture.Copy("draft", JobStatus.Draft),
            fixture.Copy("hidden", JobStatus.Published, isHidden: true),
            fixture.Copy("deleted", JobStatus.Published, isDeleted: true),
            unpublished,
            fixture.Copy("expired", JobStatus.Published, expiresAtUtc: Now),
            fixture.Copy("closed", JobStatus.Closed));
        await fixture.Context.SaveChangesAsync();

        var (items, count) = await fixture.Repository.SearchAsync(
            new PublicJobQuery(PageSize: 100));

        Assert.Equal(3, count);
        Assert.Equal(
            new[] { fixture.Featured.Id, fixture.Latest.Id, fixture.Flexible.Id }.Order(),
            items.Select(item => item.Id).Order());
        Assert.Contains(items, item => item.Id == fixture.Flexible.Id && item.ExpiresAtUtc is null);
    }

    [Fact]
    public async Task CandidateSaveAndApplicationEligibilityAlsoAllowsNoExpiry()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        var timeProvider = new FixedTimeProvider(Now);
        var candidates = new CandidateRepository(fixture.Context, timeProvider);
        var dashboard = new DashboardRepository(fixture.Context, timeProvider);

        var candidateJob = await candidates.GetAvailableJobAsync(fixture.Flexible.Id);
        var dashboardAvailable = await dashboard.IsAvailableJobAsync(fixture.Flexible.Id);

        Assert.NotNull(candidateJob);
        Assert.True(dashboardAvailable);
    }

    [Fact]
    public async Task KeywordSearchCoversTitleCompanyCategoryDescriptionAndSkills()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        var cases = new (string Term, Guid[] Expected)[]
        {
            ("Platform Intern", [fixture.Featured.Id]),
            ("Alpha Labs", [fixture.Featured.Id, fixture.Flexible.Id]),
            ("Engineering", [fixture.Featured.Id]),
            ("distributed", [fixture.Featured.Id]),
            ("C#", [fixture.Featured.Id])
        };

        foreach (var (term, expected) in cases)
        {
            var (items, count) = await fixture.Repository.SearchAsync(
                new PublicJobQuery(Keyword: term));
            Assert.Equal(expected.Length, count);
            Assert.Equal(expected.Order(), items.Select(item => item.Id).Order());
        }
    }

    [Fact]
    public async Task SearchAppliesEveryTypedFilterUsingPersistedValues()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        var cases = new (PublicJobQuery Query, Guid[] Expected)[]
        {
            (new(Locations: ["Pune"]), [fixture.Featured.Id]),
            (new(WorkModes: [WorkplaceType.Remote]), [fixture.Latest.Id]),
            (new(MinExperienceYears: 0, MaxExperienceYears: 0), [fixture.Featured.Id]),
            (new(Departments: ["Engineering"]), [fixture.Featured.Id]),
            (new(RoleCategories: ["Software Development"]), [fixture.Featured.Id]),
            (new(EmploymentTypes: [EmploymentType.FullTime]), [fixture.Latest.Id]),
            (new(MinAmount: 450, MaxAmount: 600), [fixture.Featured.Id]),
            (new(InternshipDurationMonths: [3]), [fixture.Featured.Id]),
            (new(FlexibleDuration: true), [fixture.Flexible.Id]),
            (new(EducationRequirements: ["B.Tech/B.E."]), [fixture.Featured.Id]),
            (new(CompanyTypes: [CompanyType.Consultant]), [fixture.Latest.Id]),
            (new(CompanyIds: [fixture.Beta.Id]), [fixture.Latest.Id]),
            (new(CompanyId: fixture.Alpha.Id), [fixture.Featured.Id, fixture.Flexible.Id]),
            (new(CategoryId: fixture.Engineering.Id), [fixture.Featured.Id]),
            (new(Industries: ["Consulting"]), [fixture.Latest.Id]),
            (new(PostedByTypes: [PostedByType.Consultant]), [fixture.Latest.Id]),
            (new(FreshnessDays: 1), [fixture.Latest.Id]),
            (new(FeaturedOnly: true), [fixture.Featured.Id])
        };

        foreach (var (query, expected) in cases)
        {
            var (items, count) = await fixture.Repository.SearchAsync(query);
            Assert.Equal(expected.Length, count);
            Assert.Equal(expected.Order(), items.Select(item => item.Id).Order());
        }
    }

    [Fact]
    public async Task CompanyAndCategoryNamesSupportTrimmedCaseInsensitivePartialFiltering()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        var cases = new (PublicJobQuery Query, Guid[] Expected)[]
        {
            (new(CompanyName: "Alpha"), [fixture.Featured.Id, fixture.Flexible.Id]),
            (new(CategoryName: "Engineering"), [fixture.Featured.Id]),
            (new(CompanyName: "alpha", CategoryName: "engineer"), [fixture.Featured.Id]),
            (new(CompanyName: "  ALPHA  "), [fixture.Featured.Id, fixture.Flexible.Id]),
            (new(CompanyName: "   ", CategoryName: "  engineering "), [fixture.Featured.Id]),
            (new(CompanyName: "missing"), [])
        };

        foreach (var (query, expected) in cases)
        {
            var (items, count) = await fixture.Repository.SearchAsync(query);
            Assert.Equal(expected.Length, count);
            Assert.Equal(expected.Order(), items.Select(item => item.Id).Order());
        }
    }

    [Fact]
    public async Task NameFiltersStillEnforcePublicVisibilityRules()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        fixture.Context.Jobs.AddRange(
            fixture.Copy("hidden-alpha", JobStatus.Published, isHidden: true),
            fixture.Copy("draft-alpha", JobStatus.Draft));
        await fixture.Context.SaveChangesAsync();

        var (items, count) = await fixture.Repository.SearchAsync(
            new PublicJobQuery(CompanyName: "alpha"));

        Assert.Equal(2, count);
        Assert.Equal(
            new[] { fixture.Featured.Id, fixture.Flexible.Id }.Order(),
            items.Select(item => item.Id).Order());
    }

    [Fact]
    public async Task MultiSelectRangesPaginationAndSortsAreDeterministic()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        var (multi, multiCount) = await fixture.Repository.SearchAsync(
            new PublicJobQuery(Locations: ["Pune", "Mumbai"]));
        Assert.Equal(2, multiCount);
        Assert.Equal(
            new[] { fixture.Featured.Id, fixture.Latest.Id }.Order(),
            multi.Select(item => item.Id).Order());

        var expected = new Dictionary<PublicJobSort, Guid[]>
        {
            [PublicJobSort.Recommended] =
                [fixture.Featured.Id, fixture.Latest.Id, fixture.Flexible.Id],
            [PublicJobSort.LatestPublished] =
                [fixture.Latest.Id, fixture.Featured.Id, fixture.Flexible.Id],
            [PublicJobSort.NewestAdded] =
                [fixture.Latest.Id, fixture.Featured.Id, fixture.Flexible.Id],
            [PublicJobSort.ClosingSoon] =
                [fixture.Latest.Id, fixture.Featured.Id, fixture.Flexible.Id],
            [PublicJobSort.SalaryHighToLow] =
                [fixture.Latest.Id, fixture.Featured.Id, fixture.Flexible.Id]
        };
        foreach (var (sort, ids) in expected)
        {
            var (items, _) = await fixture.Repository.SearchAsync(
                new PublicJobQuery(PageSize: 100, SortBy: sort));
            var actual = items.Select(item => item.Id).ToArray();
            Assert.True(
                ids.SequenceEqual(actual),
                $"Unexpected {sort} order. Expected {string.Join(",", ids)}; actual {string.Join(",", actual)}.");
        }

        var service = new PublicJobService(
            fixture.Repository,
            new PublicJobQueryValidator());
        var page = await service.SearchAsync(new PublicJobQuery(
            PageSize: 1,
            Page: 2,
            SortBy: PublicJobSort.LatestPublished));
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(PublicJobSort.LatestPublished, page.AppliedSort);
        Assert.Equal(fixture.Featured.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task FilterOptionsUseEligibleJobsAndExcludeOnlyTheirOwnDimension()
    {
        await using var fixture = await SearchFixture.CreateAsync();

        var options = await fixture.Repository.GetFilterOptionsAsync(
            new PublicJobQuery(Departments: ["Engineering"]));

        Assert.Equal(
            new[] { ("Pune", 1) },
            options.Locations.Select(option => (option.Value, option.Count)));
        Assert.Equal(3, options.Departments.Sum(option => option.Count));
        Assert.Contains(options.Departments, option =>
            option.Value == "Engineering" && option.Count == 1);
        Assert.Contains(options.Departments, option =>
            option.Value == "Sales" && option.Count == 1);
        Assert.Contains(options.Departments, option =>
            option.Value == "Operations" && option.Count == 1);
        Assert.Equal(new DecimalRangeFacet(300, 500, 1), options.SalaryRange);
        Assert.Equal(new IntegerRangeFacet(0, 2, 1), options.ExperienceRange);

        var nameFilteredOptions = await fixture.Repository.GetFilterOptionsAsync(
            new PublicJobQuery(CompanyName: " alpha ", CategoryName: "sales"));
        Assert.Equal(
            new[] { ("Delhi", 1) },
            nameFilteredOptions.Locations.Select(option => (option.Value, option.Count)));
    }

    [Fact]
    public void QueryValidatorRejectsUnreasonableRangesCollectionsAndEnums()
    {
        var validator = new PublicJobQueryValidator();
        var query = new PublicJobQuery(
            Page: 0,
            PageSize: 101,
            MinExperienceYears: 20,
            MaxExperienceYears: 10,
            InternshipDurationMonths: [4],
            FreshnessDays: 2,
            CompanyName: new string('c', 251),
            CategoryName: new string('c', 251),
            WorkModes: [(WorkplaceType)999],
            Locations: Enumerable.Range(0, 26).Select(index => $"Location {index}").ToArray());

        var result = validator.TestValidate(query);

        Assert.False(result.IsValid);
        result.ShouldHaveValidationErrorFor(x => x.EffectivePageNumber);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
        result.ShouldHaveValidationErrorFor(x => x.MaxExperienceYears);
        result.ShouldHaveValidationErrorFor(x => x.InternshipDurationMonths);
        result.ShouldHaveValidationErrorFor(x => x.FreshnessDays);
        result.ShouldHaveValidationErrorFor(x => x.WorkModes);
        result.ShouldHaveValidationErrorFor(x => x.Locations);
        result.ShouldHaveValidationErrorFor(x => x.CompanyName);
        result.ShouldHaveValidationErrorFor(x => x.CategoryName);
    }

    private sealed class SearchFixture : IAsyncDisposable
    {
        private SearchFixture(JobPortalDbContext context)
        {
            Context = context;
            Repository = new(context, new FixedTimeProvider(Now));
        }

        public JobPortalDbContext Context { get; }
        public PublicJobRepository Repository { get; }
        public Company Alpha { get; private set; } = null!;
        public Company Beta { get; private set; } = null!;
        public Category Engineering { get; private set; } = null!;
        public Job Featured { get; private set; } = null!;
        public Job Latest { get; private set; } = null!;
        public Job Flexible { get; private set; } = null!;

        public static async Task<SearchFixture> CreateAsync()
        {
            var context = new JobPortalDbContext(
                new DbContextOptionsBuilder<JobPortalDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options);
            var fixture = new SearchFixture(context);
            fixture.Seed();
            await context.SaveChangesAsync();
            fixture.Featured.CreatedAtUtc = Now.AddDays(-2);
            fixture.Latest.CreatedAtUtc = Now.AddHours(-1);
            fixture.Flexible.CreatedAtUtc = Now.AddDays(-3);
            await context.SaveChangesAsync();
            return fixture;
        }

        public Job Copy(
            string slug,
            JobStatus status,
            bool isHidden = false,
            bool isDeleted = false,
            DateTime? publishedAtUtc = default,
            DateTime? expiresAtUtc = default) =>
            new()
            {
                ReferenceNumber = $"REF-{slug}",
                Title = slug,
                Slug = slug,
                Description = slug,
                ApplicationUrl = "https://jobs.example.test/apply",
                CompanyId = Alpha.Id,
                CategoryId = Engineering.Id,
                EmploymentType = EmploymentType.FullTime,
                WorkplaceType = WorkplaceType.OnSite,
                ExperienceLevel = ExperienceLevel.Entry,
                Status = status,
                IsHidden = isHidden,
                IsDeleted = isDeleted,
                PublishedAtUtc = publishedAtUtc == default ? Now.AddDays(-1) : publishedAtUtc,
                ExpiresAtUtc = expiresAtUtc == default ? Now.AddDays(10) : expiresAtUtc,
                CreatedAtUtc = Now.AddDays(-1)
            };

        public ValueTask DisposeAsync() => Context.DisposeAsync();

        private void Seed()
        {
            Alpha = new()
            {
                Name = "Alpha Labs",
                Slug = "alpha-labs",
                Industry = "Technology",
                CompanyType = CompanyType.Startup,
                OwnerUserId = Guid.NewGuid(),
                CreatedAtUtc = Now.AddYears(-1)
            };
            Beta = new()
            {
                Name = "Beta Staffing",
                Slug = "beta-staffing",
                Industry = "Consulting",
                CompanyType = CompanyType.Consultant,
                OwnerUserId = Guid.NewGuid(),
                CreatedAtUtc = Now.AddYears(-1)
            };
            Engineering = new()
            {
                Name = "Engineering",
                Slug = "engineering",
                CreatedAtUtc = Now.AddYears(-1)
            };
            var sales = new Category
            {
                Name = "Sales",
                Slug = "sales",
                CreatedAtUtc = Now.AddYears(-1)
            };
            Featured = Job(
                "platform-intern",
                "Platform Intern",
                Alpha,
                Engineering,
                Now.AddDays(-2),
                Now.AddDays(10),
                EmploymentType.Internship,
                WorkplaceType.Hybrid,
                "Pune",
                "Engineering",
                "Software Development",
                "B.Tech/B.E.",
                PostedByType.Company,
                0,
                2,
                300,
                500,
                true,
                internshipDurationMonths: 3,
                description: "Build distributed systems.");
            Latest = Job(
                "account-manager",
                "Account Manager",
                Beta,
                sales,
                Now.AddHours(-1),
                Now.AddDays(2),
                EmploymentType.FullTime,
                WorkplaceType.Remote,
                "Mumbai",
                "Sales",
                "Account Management",
                "Any Graduate",
                PostedByType.Consultant,
                3,
                5,
                800,
                1200);
            Flexible = Job(
                "operations-intern",
                "Operations Intern",
                Alpha,
                sales,
                Now.AddDays(-10),
                null,
                EmploymentType.Internship,
                WorkplaceType.OnSite,
                "Delhi",
                "Operations",
                "Operations",
                "Any Postgraduate",
                PostedByType.Company,
                1,
                4,
                100,
                200,
                isFlexible: true);
            var skill = new Skill
            {
                Name = "C#",
                NormalizedName = "C#",
                CreatedAtUtc = Now.AddYears(-1)
            };
            var jobSkill = new JobSkill
            {
                Job = Featured,
                JobId = Featured.Id,
                Skill = skill,
                SkillId = skill.Id,
                IsRequired = true,
                ProficiencyLevel = 3,
                CreatedAtUtc = Now.AddDays(-2)
            };
            Context.AddRange(Alpha, Beta, Engineering, sales, Featured, Latest, Flexible, skill, jobSkill);
        }

        private static Job Job(
            string slug,
            string title,
            Company company,
            Category category,
            DateTime publishedAtUtc,
            DateTime? expiresAtUtc,
            EmploymentType employmentType,
            WorkplaceType workplaceType,
            string location,
            string department,
            string roleCategory,
            string education,
            PostedByType postedBy,
            int minimumExperience,
            int maximumExperience,
            decimal minimumSalary,
            decimal maximumSalary,
            bool isFeatured = false,
            int? internshipDurationMonths = null,
            bool isFlexible = false,
            string description = "Persisted description") =>
            new()
            {
                ReferenceNumber = $"REF-{slug}",
                Title = title,
                Slug = slug,
                Description = description,
                ApplicationUrl = "https://jobs.example.test/apply",
                Company = company,
                CompanyId = company.Id,
                Category = category,
                CategoryId = category.Id,
                EmploymentType = employmentType,
                WorkplaceType = workplaceType,
                ExperienceLevel = ExperienceLevel.Entry,
                Location = location,
                Department = department,
                RoleCategory = roleCategory,
                EducationRequirement = education,
                PostedByType = postedBy,
                MinimumExperienceYears = minimumExperience,
                MaximumExperienceYears = maximumExperience,
                MinimumSalary = minimumSalary,
                MaximumSalary = maximumSalary,
                CurrencyCode = "INR",
                InternshipDurationMonths = internshipDurationMonths,
                IsFlexibleDuration = isFlexible,
                IsFeatured = isFeatured,
                Status = JobStatus.Published,
                PublishedAtUtc = publishedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
                CreatedAtUtc = publishedAtUtc
            };
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
