using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorMarketIntelligenceTests
{
    [Fact]
    public void AcceleratorMarketIntelligenceDetectsPhrasesAndAliasesWithoutSubstringFalsePositives()
    {
        var skills = new[]
        {
            Term("ASP.NET Core", "ASP Net Core", ".NET Core", "Dotnet Core"),
            Term("Azure Data Factory", "ADF"), Term("Machine Learning", "ML"),
            Term("Go"), Term("Snowflake")
        };
        var job = Snapshot("Backend Engineer", "Experience with asp.net core and ADF; " +
            "machine learning tools are useful. Strong Google Cloud experience.",
            ["ML"]);
        var result = MarketExtractionPolicy.Extract(job, skills, []);
        Assert.Contains(skills[0].Id, result.SkillIds);
        Assert.Contains(skills[1].Id, result.SkillIds);
        Assert.Contains(skills[2].Id, result.SkillIds);
        Assert.DoesNotContain(skills[3].Id, result.SkillIds);
    }

    [Fact]
    public void AcceleratorMarketIntelligenceDoesNotMatchShortAmbiguousAliasInProse()
    {
        var ml = Term("Machine Learning", "ML");
        var job = Snapshot("Software Engineer", "Our XML pipeline uses Azure.", []);
        Assert.DoesNotContain(ml.Id, MarketExtractionPolicy.Extract(job, [ml], []).SkillIds);
        Assert.Contains(ml.Id, MarketExtractionPolicy.Extract(job with { StructuredSkills = ["ML"] },
            [ml], []).SkillIds);
    }

    [Fact]
    public void AcceleratorMarketIntelligenceDetectsOnlyPlausibleUnknownSkills()
    {
        var job = Snapshot("Prompt Engineer", "No taxonomy match.", ["LangGraph", "The", "C"]);
        var result = MarketExtractionPolicy.Extract(job, [], []);
        Assert.Equal(["LANGGRAPH"], result.UnknownSkills);
        Assert.Equal("PROMPT ENGINEER", result.UnknownRole);
    }

    [Fact]
    public void AcceleratorMarketIntelligenceFindsUnknownTermInExplicitRequirementSkillLine()
    {
        var job = Snapshot("Data Engineer", "General duties", []) with
        { Requirements = "- Skills: LangGraph, SQL, The Team" };
        var result = MarketExtractionPolicy.Extract(job, [Term("SQL")], []);
        Assert.Equal(["LANGGRAPH"], result.UnknownSkills);
    }

    [Fact]
    public void AcceleratorMarketIntelligenceFindsDistinctiveUnknownTechnologyInContextualProse()
    {
        var job = Snapshot("AI Engineer", "Experience with LangGraph in production.", []);
        Assert.Equal(["LANGGRAPH"], MarketExtractionPolicy.Extract(job, [], []).UnknownSkills);
        Assert.Empty(MarketExtractionPolicy.Extract(job with
        { Description = "Experience with teams and people." }, [], []).UnknownSkills);
    }

    [Fact]
    public void AcceleratorMarketIntelligenceResolvesCanonicalRoleAlias()
    {
        var role = Term("Machine Learning Engineer", "AI ML Engineer");
        var result = MarketExtractionPolicy.Extract(Snapshot("AI ML Engineer", "", []), [], [role]);
        Assert.Equal(role.Id, result.PrimaryRoleId);
        Assert.Null(result.UnknownRole);
    }

    [Fact]
    public void AcceleratorMarketIntelligenceDoesNotForceAmbiguousRole()
    {
        var roles = new[] { Term("AI Engineer", "AI ML Engineer"),
            Term("Machine Learning Engineer", "AI ML Engineer") };
        var result = MarketExtractionPolicy.Extract(Snapshot("AI ML Engineer", "", []), [], roles);
        Assert.Null(result.PrimaryRoleId);
        Assert.Equal("AI ML ENGINEER", result.UnknownRole);
    }

    [Fact]
    public void AcceleratorMarketIntelligenceRequiresRepeatedJobsAndIndependentCompaniesForReview()
    {
        var settings = new MarketIntelligenceOptions { DiscoveryMinimumJobs = 3,
            DiscoveryMinimumCompanies = 2 };
        Assert.False(MarketDiscoveryPolicy.ReadyForReview(1, 1, settings));
        Assert.False(MarketDiscoveryPolicy.ReadyForReview(3, 1, settings));
        Assert.True(MarketDiscoveryPolicy.ReadyForReview(3, 2, settings));
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceBootstrapsCanonicalPathsFromActiveJobsWhenTaxonomyIsMissing()
    {
        await using var db = Database();
        var company = Company(db, "CareerHarbor");
        var sql = new Skill { Name = "SQL", NormalizedName = "SQL" };
        var python = new Skill { Name = "Python", NormalizedName = "PYTHON" };
        var job = new Job
        {
            Title = "Senior Data Engineer", Company = company, CompanyId = company.Id,
            Status = JobStatus.Published, PublishedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        job.JobSkills.Add(new JobSkill { Job = job, JobId = job.Id, Skill = sql, SkillId = sql.Id });
        job.JobSkills.Add(new JobSkill { Job = job, JobId = job.Id, Skill = python, SkillId = python.Id });
        db.AddRange(sql, python, job);
        await db.SaveChangesAsync();

        var store = new MarketIntelligenceStore(db);
        await store.BootstrapTaxonomyAsync(DateTime.UtcNow, default);

        var taxonomy = Assert.Single(db.AcceleratorTaxonomies);
        Assert.Equal(MarketFoundationTaxonomyPolicy.Version, taxonomy.Version);
        Assert.Equal(2, db.AcceleratorCompetencies.Count());
        var role = Assert.Single(db.MarketRoles);
        Assert.Equal("Data Engineer", role.Name);
        Assert.Contains(await store.SkillsAsync(default), x => x.Name == "SQL");
        Assert.Contains(await store.RolesAsync(default), x => x.Id == role.Id);
        Assert.Equal(1, Assert.Single(db.MarketProcessingCheckpoints).TaxonomyRevision);

        await store.BootstrapTaxonomyAsync(DateTime.UtcNow.AddMinutes(5), default);
        Assert.Equal(1, Assert.Single(db.MarketProcessingCheckpoints).TaxonomyRevision);

        var extracted = MarketExtractionPolicy.Extract(Snapshot("Senior Data Engineer", "", ["SQL", "Python"]),
            await store.SkillsAsync(default), await store.RolesAsync(default));
        Assert.Equal(role.Id, extracted.PrimaryRoleId);
        Assert.Equal(2, extracted.SkillIds.Length);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceFoundationBootstrapIsIdempotentAndRetainsUnknownTitlesForReview()
    {
        await using var db = Database();
        var company = Company(db, "CareerHarbor");
        var skill = new Skill { Name = "Rust", NormalizedName = "RUST" };
        var job = new Job
        {
            Title = "Compiler Specialist", Company = company, CompanyId = company.Id,
            Status = JobStatus.Published, PublishedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        job.JobSkills.Add(new JobSkill { Job = job, JobId = job.Id, Skill = skill, SkillId = skill.Id });
        db.AddRange(skill, job);
        await db.SaveChangesAsync();

        var store = new MarketIntelligenceStore(db);
        await store.BootstrapTaxonomyAsync(DateTime.UtcNow, default);
        Assert.Empty(db.AcceleratorTaxonomies);
        Assert.Empty(db.MarketSkills);
        Assert.Empty(db.MarketRoles);

        job.Title = "Data Engineer";
        await db.SaveChangesAsync();
        await store.BootstrapTaxonomyAsync(DateTime.UtcNow, default);
        var firstCounts = (db.AcceleratorTaxonomies.Count(), db.MarketSkills.Count(),
            db.MarketRoles.Count(), db.MarketProcessingCheckpoints.Single().TaxonomyRevision);
        await store.BootstrapTaxonomyAsync(DateTime.UtcNow, default);
        Assert.Equal(firstCounts, (db.AcceleratorTaxonomies.Count(), db.MarketSkills.Count(),
            db.MarketRoles.Count(), db.MarketProcessingCheckpoints.Single().TaxonomyRevision));
    }

    [Fact]
    public void AcceleratorMarketIntelligenceFoundationRolesPreferPrecisionOverBroadTechnologyTitles()
    {
        Assert.True(MarketFoundationTaxonomyPolicy.TryResolveRole("Senior Data Engineer", out var data));
        Assert.Equal("data-engineer", data.Code);
        Assert.True(MarketFoundationTaxonomyPolicy.TryResolveRole("Backend Engineer", out var backend));
        Assert.Equal("backend-developer", backend.Code);
        Assert.False(MarketFoundationTaxonomyPolicy.TryResolveRole("Software Engineer", out _));
        Assert.False(MarketFoundationTaxonomyPolicy.TryResolveRole(".NET Developer", out _));
        Assert.False(MarketFoundationTaxonomyPolicy.TryResolveRole("Java Developer", out _));

        var result = MarketExtractionPolicy.Extract(Snapshot("Software Engineer", "", []), [],
            [Term("Data Engineer")]);
        Assert.Null(result.PrimaryRoleId);
        Assert.Equal("SOFTWARE ENGINEER", result.UnknownRole);
    }

    [Fact]
    public void AcceleratorMarketIntelligenceFoundationAliasPlanDeduplicatesNormalizationVariants()
    {
        var skill = Guid.NewGuid();
        var plan = MarketFoundationTaxonomyPolicy.PlanSkillAliases(
            [new(skill, "ASP.NET Core", ["asp net core", " ASP.NET   Core ", "ASP.NET Core"])], []);

        var alias = Assert.Single(plan.Selections);
        Assert.Equal(skill, alias.SkillId);
        Assert.Equal("ASP NET CORE", alias.NormalizedText);
    }

    [Fact]
    public void AcceleratorMarketIntelligenceFoundationAliasPlanSkipsAmbiguousAndPersistedOwnership()
    {
        var csharp = Guid.NewGuid();
        var c = Guid.NewGuid();
        var collision = MarketFoundationTaxonomyPolicy.PlanSkillAliases(
            [new(csharp, "C#", []), new(c, "C", [])], []);
        Assert.Empty(collision.Selections);
        Assert.Equal(2, collision.AmbiguousAliasesSkipped);

        var existing = MarketFoundationTaxonomyPolicy.PlanSkillAliases(
            [new(csharp, "C#", [])], ["C"]);
        Assert.Empty(existing.Selections);
        Assert.Equal(1, existing.DuplicateAliasesSkipped);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceBootstrapSkipsPunctuationCollisionInsteadOfViolatingAliasIndex()
    {
        await using var db = Database();
        var company = Company(db, "CareerHarbor");
        var csharp = new Skill { Name = "C#", NormalizedName = "C#" };
        var c = new Skill { Name = "C", NormalizedName = "C" };
        var job = new Job { Title = "Data Engineer", Company = company, CompanyId = company.Id,
            Status = JobStatus.Published, PublishedAtUtc = DateTime.UtcNow.AddMinutes(-1) };
        job.JobSkills.Add(new JobSkill { Job = job, JobId = job.Id, Skill = csharp, SkillId = csharp.Id });
        job.JobSkills.Add(new JobSkill { Job = job, JobId = job.Id, Skill = c, SkillId = c.Id });
        db.AddRange(csharp, c, job);
        await db.SaveChangesAsync();

        await new MarketIntelligenceStore(db).BootstrapTaxonomyAsync(DateTime.UtcNow, default);

        Assert.Equal(2, db.MarketSkills.Count());
        Assert.Empty(db.MarketSkillAliases);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceBootstrapDoesNotTakeAnExistingAliasFromAnotherSkill()
    {
        await using var db = Database();
        var company = Company(db, "CareerHarbor");
        var existingSkill = new Skill { Name = "Existing C", NormalizedName = "EXISTING C" };
        var existingMarket = new MarketSkill { Skill = existingSkill, SkillId = existingSkill.Id,
            Slug = "existing-c", Category = "Existing", Description = "Existing" };
        db.MarketSkillAliases.Add(new MarketSkillAlias { MarketSkill = existingMarket,
            Text = "C", NormalizedText = "C" });
        var csharp = new Skill { Name = "C#", NormalizedName = "C#" };
        var job = FoundationJob(company, csharp, "Data Engineer", JobStatus.Published,
            DateTime.UtcNow.AddMinutes(-1), null);
        db.AddRange(existingSkill, csharp, job);
        await db.SaveChangesAsync();

        await new MarketIntelligenceStore(db).BootstrapTaxonomyAsync(DateTime.UtcNow, default);

        Assert.Equal(existingMarket.Id, Assert.Single(db.MarketSkillAliases).MarketSkillId);
        Assert.Contains(db.MarketSkills, x => x.SkillId == csharp.Id);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceBootstrapRequiresActivePublishedDemand()
    {
        await using var db = Database();
        var company = Company(db, "CareerHarbor");
        var expired = new Skill { Name = "Expired", NormalizedName = "EXPIRED" };
        var deleted = new Skill { Name = "Deleted", NormalizedName = "DELETED" };
        var draft = new Skill { Name = "Draft", NormalizedName = "DRAFT" };
        db.AddRange(expired, deleted, draft);
        db.Jobs.AddRange(
            FoundationJob(company, expired, "Data Engineer", JobStatus.Published,
                DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddMinutes(-1)),
            FoundationJob(company, deleted, "Data Engineer", JobStatus.Published,
                DateTime.UtcNow.AddMinutes(-1), null, true),
            FoundationJob(company, draft, "Data Engineer", JobStatus.Draft,
                DateTime.UtcNow.AddMinutes(-1), null));
        await db.SaveChangesAsync();

        await new MarketIntelligenceStore(db).BootstrapTaxonomyAsync(DateTime.UtcNow, default);

        Assert.Empty(db.AcceleratorTaxonomies);
        Assert.Empty(db.MarketSkills);
        Assert.Empty(db.MarketRoles);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceBootstrapLeavesNoisySkillsForDiscoveryWhenBroaderDemandExists()
    {
        await using var db = Database();
        var first = Company(db, "A");
        var second = Company(db, "B");
        var sql = new Skill { Name = "SQL", NormalizedName = "SQL" };
        var noise = new Skill { Name = "One Off Framework", NormalizedName = "ONE OFF FRAMEWORK" };
        db.AddRange(sql, noise);
        db.Jobs.AddRange(
            FoundationJob(first, sql, "Data Engineer", JobStatus.Published, DateTime.UtcNow.AddMinutes(-3), null),
            FoundationJob(second, sql, "Data Engineer", JobStatus.Published, DateTime.UtcNow.AddMinutes(-2), null),
            FoundationJob(first, sql, "Backend Engineer", JobStatus.Published, DateTime.UtcNow.AddMinutes(-1), null),
            FoundationJob(first, noise, "Data Engineer", JobStatus.Published, DateTime.UtcNow.AddMinutes(-1), null));
        await db.SaveChangesAsync();

        await new MarketIntelligenceStore(db).BootstrapTaxonomyAsync(DateTime.UtcNow, default);

        Assert.Contains(db.AcceleratorCompetencies, x => x.SkillId == sql.Id);
        Assert.DoesNotContain(db.AcceleratorCompetencies, x => x.SkillId == noise.Id);
        Assert.DoesNotContain(db.MarketSkills, x => x.SkillId == noise.Id);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceBootstrapPreservesAnExistingPublishedTaxonomy()
    {
        await using var db = Database();
        var skill = new Skill { Name = "Existing SQL", NormalizedName = "EXISTING SQL" };
        var taxonomy = new AcceleratorTaxonomy { Version = "existing-v1", ContentHash = "existing",
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1) };
        var competency = new AcceleratorCompetency { Taxonomy = taxonomy, TaxonomyId = taxonomy.Id,
            Skill = skill, SkillId = skill.Id, SkillNameSnapshot = skill.Name, Description = "Existing",
            Category = "Existing", Difficulty = 1, AssessmentMethodsJson = "[\"mcq\"]" };
        var role = new AcceleratorCareerRole { Taxonomy = taxonomy, TaxonomyId = taxonomy.Id,
            Code = "existing-role", Name = "Existing Role", Description = "Existing" };
        role.Requirements.Add(new AcceleratorRoleRequirement { TaxonomyId = taxonomy.Id, Role = role,
            RoleId = role.Id, Competency = competency, CompetencyId = competency.Id, ExpectedProficiency = 1 });
        taxonomy.Competencies.Add(competency);
        taxonomy.Roles.Add(role);
        db.Add(taxonomy);
        await db.SaveChangesAsync();

        await new MarketIntelligenceStore(db).BootstrapTaxonomyAsync(DateTime.UtcNow, default);

        Assert.Equal("existing-v1", Assert.Single(db.AcceleratorTaxonomies).Version);
        Assert.Equal(competency.Id, Assert.Single(db.AcceleratorCompetencies).Id);
        Assert.DoesNotContain(db.AcceleratorTaxonomies, x => x.Version == MarketFoundationTaxonomyPolicy.Version);
        Assert.Equal(role.Id, Assert.Single(db.MarketRoles).AcceleratorRoleId);
        var revision = Assert.Single(db.MarketProcessingCheckpoints).TaxonomyRevision;
        await new MarketIntelligenceStore(db).BootstrapTaxonomyAsync(DateTime.UtcNow.AddMinutes(5), default);
        Assert.Equal(revision, Assert.Single(db.MarketProcessingCheckpoints).TaxonomyRevision);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceSearchesApprovedAliasesAndKeepsUnpublishedPathsVisible()
    {
        await using var db = Database();
        var snowflake = Skill(db, "Snowflake", null);
        var etl = Skill(db, "ETL", null);
        var dotnet = Skill(db, "ASP.NET Core", null, "Dotnet Core");
        var role = Role(db, "Machine Learning Engineer", "AI ML Engineer");
        await db.SaveChangesAsync();
        var store = new MarketIntelligenceStore(db);
        var snowflakeResult = await store.PathsAsync(false, "snowflake", null, null, null, null,
            "Alphabetical", 0, 10, DateTime.UtcNow, default);
        Assert.Single(snowflakeResult);
        Assert.Equal(snowflake.Id, snowflakeResult[0].Id);
        Assert.Equal("ComingSoon", snowflakeResult[0].LearningPathStatus);
        Assert.Equal($"/api/jobs?marketSkillId={snowflake.Id:D}", snowflakeResult[0].JobFilter);
        Assert.Equal(etl.Id, Assert.Single(await store.PathsAsync(false, "etl", null, null, null,
            null, "Alphabetical", 0, 10, DateTime.UtcNow, default)).Id);
        Assert.Equal(dotnet.Id, Assert.Single(await store.PathsAsync(false, "dotnet core", null, null,
            null, null, "Alphabetical", 0, 10, DateTime.UtcNow, default)).Id);
        Assert.Equal(role.Id, Assert.Single(await store.PathsAsync(true, "AI ML Engineer", null, null,
            null, null, "Alphabetical", 0, 10, DateTime.UtcNow, default)).Id);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceCountsOnlyActiveJobsAndDistinctCompanies()
    {
        await using var db = Database();
        var skill = Skill(db, "Snowflake", null);
        var role = Role(db, "Data Engineer");
        var first = Company(db, "A"); var second = Company(db, "B");
        var now = DateTime.UtcNow;
        AddJob(db, skill, role, first, JobStatus.Published, now.AddDays(-2), null);
        AddJob(db, skill, role, first, JobStatus.Published, now.AddDays(-4), null);
        AddJob(db, skill, role, second, JobStatus.Published, now.AddDays(-5), null);
        AddJob(db, skill, role, second, JobStatus.Draft, now.AddDays(-1), null);
        AddJob(db, skill, role, second, JobStatus.Published, now.AddDays(-1), now.AddHours(-1));
        await db.SaveChangesAsync();
        var store = new MarketIntelligenceStore(db);
        var card = Assert.Single(await store.PathsAsync(false, null, null, null, null, null,
            "MostJobs", 0, 10, now, default));
        Assert.Equal(3, card.Demand.ActiveJobCount);
        Assert.Equal(2, card.Demand.DistinctCompanyCount);
        Assert.Equal(3, card.Demand.NewJobs7Days);
        Assert.Null(card.Demand.TrendPercent);
        Assert.Equal(role.Id, Assert.Single(card.TopAssociations).Id);
        var detail = await store.DetailAsync(true, role.Id, now, default);
        Assert.NotNull(detail);
        Assert.Equal(3, detail.Path.Demand.ActiveJobCount);
        Assert.Equal(100m, Assert.Single(detail.Associations).Percentage);
        var skillDetail = await store.DetailAsync(false, skill.Id, now, default);
        Assert.NotNull(skillDetail);
        Assert.Equal(3, Assert.Single(skillDetail.Associations).JobCount);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceTrendUsesPreviousWindowAndHandlesZero()
    {
        await using var db = Database();
        var skill = Skill(db, "Snowflake", null);
        var company = Company(db, "A");
        var now = DateTime.UtcNow;
        AddJob(db, skill, null, company, JobStatus.Published, now.AddDays(-35), null);
        AddJob(db, skill, null, company, JobStatus.Published, now.AddDays(-5), null);
        AddJob(db, skill, null, company, JobStatus.Published, now.AddDays(-3), null);
        await db.SaveChangesAsync();
        var card = Assert.Single(await new MarketIntelligenceStore(db).PathsAsync(false, null,
            null, null, null, null, "MostJobs", 0, 10, now, default));
        Assert.Equal(100m, card.Demand.TrendPercent);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligencePaginationAndPendingDiscoveryIsolation()
    {
        await using var db = Database();
        Skill(db, "Snowflake", null); Skill(db, "ETL", null);
        db.MarketSkillDiscoveries.Add(new MarketSkillDiscovery { RawTerm = "SecretCandidate",
            NormalizedTerm = "SECRETCANDIDATE", Status = MarketDiscoveryStatus.PendingReview });
        await db.SaveChangesAsync();
        var store = new MarketIntelligenceStore(db);
        var first = await store.PathsAsync(false, null, null, null, null, null,
            "Alphabetical", 0, 1, DateTime.UtcNow, default);
        var second = await store.PathsAsync(false, null, null, null, null, null,
            "Alphabetical", 1, 1, DateTime.UtcNow, default);
        Assert.Single(first); Assert.Single(second); Assert.NotEqual(first[0].Id, second[0].Id);
        Assert.Empty(await store.PathsAsync(false, "SecretCandidate", null, null, null,
            null, "Alphabetical", 0, 10, DateTime.UtcNow, default));
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceRelatedSkillsUseActiveJobCooccurrence()
    {
        await using var db = Database();
        var snowflake = Skill(db, "Snowflake", null);
        var etl = Skill(db, "ETL", null);
        var company = Company(db, "A");
        var now = DateTime.UtcNow;
        var job = new Job { Title = "Data Engineer", Company = company, CompanyId = company.Id,
            Status = JobStatus.Published, PublishedAtUtc = now.AddDays(-1) };
        db.Jobs.Add(job);
        db.MarketJobSkills.AddRange(new MarketJobSkill { Job = job, MarketSkill = snowflake },
            new MarketJobSkill { Job = job, MarketSkill = etl });
        await db.SaveChangesAsync();
        var detail = await new MarketIntelligenceStore(db).DetailAsync(false, snowflake.Id, now, default);
        Assert.NotNull(detail);
        Assert.Equal(etl.Id, Assert.Single(detail.Related).Id);
        Assert.Equal(100m, detail.Related[0].Percentage);
        Assert.Equal("A", Assert.Single(detail.Companies).Name);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceHistoricalMappingDoesNotCountAsCurrentDemand()
    {
        await using var db = Database();
        var skill = Skill(db, "Snowflake", null);
        var company = Company(db, "A");
        var now = DateTime.UtcNow;
        AddJob(db, skill, null, company, JobStatus.Published, now.AddDays(-1), null);
        var stale = new Job { Title = "Old", Company = company, CompanyId = company.Id,
            Status = JobStatus.Published, PublishedAtUtc = now.AddDays(-1) };
        db.Jobs.Add(stale);
        db.MarketJobSkills.Add(new MarketJobSkill { Job = stale, MarketSkill = skill,
            IsDeleted = true, DeletedAtUtc = now });
        await db.SaveChangesAsync();
        var card = Assert.Single(await new MarketIntelligenceStore(db).PathsAsync(false, null, null,
            null, null, null, "MostJobs", 0, 10, now, default));
        Assert.Equal(1, card.Demand.ActiveJobCount);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceCategoryAndMostJobsSortingAreBounded()
    {
        await using var db = Database();
        var snowflake = Skill(db, "Snowflake", null);
        var etl = Skill(db, "ETL", null);
        etl.Category = "Data";
        var company = Company(db, "A");
        var now = DateTime.UtcNow;
        AddJob(db, etl, null, company, JobStatus.Published, now.AddDays(-1), null);
        AddJob(db, etl, null, company, JobStatus.Published, now.AddDays(-2), null);
        AddJob(db, snowflake, null, company, JobStatus.Published, now.AddDays(-1), null);
        await db.SaveChangesAsync();
        var store = new MarketIntelligenceStore(db);
        var ranked = await store.PathsAsync(false, null, null, null, null, null,
            "MostJobs", 0, 2, now, default);
        Assert.Equal(etl.Id, ranked[0].Id);
        Assert.Equal(snowflake.Id, ranked[1].Id);
        Assert.Equal(etl.Id, Assert.Single(await store.PathsAsync(false, null, "Data", null,
            null, null, "MostJobs", 0, 2, now, default)).Id);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceCardAssociationsStopAtThree()
    {
        await using var db = Database();
        var role = Role(db, "Data Engineer");
        var company = Company(db, "A");
        var now = DateTime.UtcNow;
        foreach (var name in new[] { "SQL", "ETL", "Snowflake", "Airflow" })
            AddJob(db, Skill(db, name, null), role, company, JobStatus.Published,
                now.AddDays(-1), null);
        await db.SaveChangesAsync();
        var card = Assert.Single(await new MarketIntelligenceStore(db).PathsAsync(true,
            null, null, null, null, null, "MostJobs", 0, 10, now, default));
        Assert.Equal(3, card.TopAssociations.Count);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceCandidateCannotMutateAliasAndCollisionIsRejected()
    {
        using var fixture = new AcceleratorFixture();
        var first = Skill(fixture.Db, "Snowflake", null);
        var second = Skill(fixture.Db, "ETL", null);
        fixture.Db.SaveChanges();
        var service = new MarketIntelligenceService(new MarketIntelligenceStore(fixture.Db),
            fixture.Service, fixture.Audit, new UnitOfWork(fixture.Db),
            Options.Create(new MarketIntelligenceOptions()), fixture.Clock,
            NullLogger<MarketIntelligenceService>.Instance);
        await Assert.ThrowsAsync<AppException>(() => service.AddAliasAsync(fixture.Candidate.Id,
            false, first.Id, new MarketAliasRequest("Snow", "Evidence reviewed"), default));
        await Assert.ThrowsAsync<ConflictException>(() => service.AddAliasAsync(fixture.Admin.Id,
            false, second.Id, new MarketAliasRequest("Snowflake", "Evidence reviewed"), default));
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceCandidateSignalsRemainCandidateScoped()
    {
        using var fixture = new AcceleratorFixture();
        var competency = Guid.NewGuid();
        var market = Skill(fixture.Db, "Snowflake", competency);
        fixture.Db.AcceleratorSkillEvidence.Add(new AcceleratorSkillEvidence
        { UserId = fixture.Other.Id, CompetencyId = competency,
            Source = AcceleratorEvidenceSource.AssessmentDemonstrated,
            Proficiency = AcceleratorProficiency.Advanced });
        fixture.Db.SaveChanges();
        var store = new MarketIntelligenceStore(fixture.Db);
        Assert.Empty((await store.CandidateSignalsAsync(fixture.Candidate.Id,
            default)).VerifiedProficiencyByMarketSkill);
        Assert.Equal((int)AcceleratorProficiency.Advanced,
            (await store.CandidateSignalsAsync(fixture.Other.Id, default))
                .VerifiedProficiencyByMarketSkill[market.Id]);
    }

    [Fact]
    public void AcceleratorMarketIntelligencePostgresCursorQueryTranslatesWithoutDatabaseAccess()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=market_query_translation_only;Username=unused;Password=unused")
            .Options);
        var checkpoint = new MarketProcessingCheckpoint { LastJobId = Guid.NewGuid(),
            LastJobCreatedAtUtc = DateTime.UtcNow };
        var sql = MarketIntelligenceStore.AfterCursorQuery(db, checkpoint).ToQueryString();
        Assert.Contains("CreatedAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains("Job", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceRecommendationUsesOnlyCandidatesOwnGoal()
    {
        using var fixture = new AcceleratorFixture();
        var desiredRoleId = Guid.NewGuid();
        var desired = Role(fixture.Db, "Backend Developer");
        desired.AcceleratorRoleId = desiredRoleId;
        var other = Role(fixture.Db, "Data Engineer");
        fixture.Db.AcceleratorCareerGoals.Add(new AcceleratorCareerGoal
        { UserId = fixture.Candidate.Id, RoleId = desiredRoleId,
            Status = AcceleratorGoalStatus.Active, CreatedAtUtc = DateTime.UtcNow });
        fixture.Db.SaveChanges();
        var service = new MarketIntelligenceService(new MarketIntelligenceStore(fixture.Db),
            fixture.Service, fixture.Audit, new UnitOfWork(fixture.Db),
            Options.Create(new MarketIntelligenceOptions()), fixture.Clock,
            NullLogger<MarketIntelligenceService>.Instance);
        var candidate = await service.RecommendAsync(fixture.Candidate.Id, true, default);
        var anotherCandidate = await service.RecommendAsync(fixture.Other.Id, true, default);
        Assert.Equal(desired.Id, candidate[0].Path.Id);
        Assert.Contains("career goal", candidate[0].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.All(anotherCandidate, x => Assert.NotEqual(100m, x.Score));
        Assert.Contains(anotherCandidate, x => x.Path.Id == other.Id);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceBatchRespectsLimitAndReplayIsIdempotent()
    {
        using var fixture = new AcceleratorFixture();
        var store = new FakeMarketStore();
        store.Jobs.AddRange(Enumerable.Range(0, 4).Select(_ =>
            Snapshot("Data Engineer", "Snowflake", [])));
        store.Terms.Add(Term("Snowflake"));
        var service = MarketService(fixture, store);
        var first = await service.ProcessBatchAsync(2, default);
        Assert.Equal(2, first.JobsProcessed);
        Assert.Equal(2, first.SkillsMapped);
        Assert.Equal(2, store.LastTake);
        Assert.True(store.Completed);
        store.ResetCursor();
        var replay = await service.ProcessBatchAsync(2, default);
        Assert.Equal(0, replay.JobsProcessed);
        Assert.Equal(2, store.Applied.Count);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceDoesNotClaimJobsBeforeCanonicalTermsAreAvailable()
    {
        using var fixture = new AcceleratorFixture();
        var store = new FakeMarketStore();
        store.Jobs.Add(Snapshot("Unclassified Role", "", ["UnknownSkill"]));

        var result = await MarketService(fixture, store).ProcessBatchAsync(10, default);

        Assert.Equal(new MarketBatchResult(0, 0, 0, 0, 0), result);
        Assert.Equal(0, store.LastTake);
        Assert.Empty(store.Applied);
        Assert.False(store.Completed);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceBatchIsolatesOneJobFailure()
    {
        using var fixture = new AcceleratorFixture();
        var store = new FakeMarketStore();
        store.Jobs.AddRange([Snapshot("Data Engineer", "", []),
            Snapshot("Backend Developer", "", [])]);
        store.Terms.Add(Term("SQL"));
        store.FailingJobId = store.Jobs[0].Id;
        var result = await MarketService(fixture, store).ProcessBatchAsync(2, default);
        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(store.Jobs[0].Id, Assert.Single(store.Failures));
        Assert.True(store.Completed);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceBackfillRequiresAdminAndBoundsBatch()
    {
        using var fixture = new AcceleratorFixture();
        var store = new FakeMarketStore();
        store.Terms.Add(Term("SQL"));
        var service = MarketService(fixture, store);
        await Assert.ThrowsAsync<AppException>(() => service.BackfillAsync(fixture.Candidate.Id,
            new MarketBackfillRequest(1), default));
        await Assert.ThrowsAsync<BadRequestException>(() => service.BackfillAsync(fixture.Admin.Id,
            new MarketBackfillRequest(201), default));
        var result = await service.BackfillAsync(fixture.Admin.Id,
            new MarketBackfillRequest(25), default);
        Assert.Equal(0, result.JobsProcessed);
        Assert.Equal(25, store.LastTake);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceCanonicalMergeKeepsHistoricalMappingsAndAudits()
    {
        using var fixture = new AcceleratorFixture();
        var source = Skill(fixture.Db, "Dotnet Core", null);
        var target = Skill(fixture.Db, "ASP.NET Core", null);
        var company = Company(fixture.Db, "A");
        AddJob(fixture.Db, source, null, company, JobStatus.Published,
            DateTime.UtcNow.AddDays(-1), null);
        fixture.Db.SaveChanges();
        var service = MarketService(fixture, new MarketIntelligenceStore(fixture.Db));
        await Assert.ThrowsAsync<AppException>(() => service.MergeCanonicalAsync(fixture.Candidate.Id,
            false, source.Id, new MarketMergeRequest(target.Id, "Reviewed taxonomy overlap"), default));
        await service.MergeCanonicalAsync(fixture.Admin.Id, false, source.Id,
            new MarketMergeRequest(target.Id, "Reviewed taxonomy overlap"), default);
        Assert.Equal(MarketConceptStatus.Merged, source.Status);
        Assert.Equal(target.Id, source.MergedIntoId);
        Assert.Contains(fixture.Db.MarketSkillAliases,
            x => x.MarketSkillId == target.Id && x.NormalizedText == "DOTNET CORE");
        Assert.Contains(fixture.Db.MarketJobSkills, x => x.MarketSkillId == source.Id && !x.IsDeleted);
        Assert.Contains(fixture.Audit.Events, x => x.EntityType == "MarketSkill" &&
            x.EntityId == source.Id.ToString());
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceAdminDiscoveryListIsPagedAndDetailBoundsEvidence()
    {
        await using var db = Database();
        var first = new MarketSkillDiscovery { RawTerm = "LangGraph",
            NormalizedTerm = "LANGGRAPH", Status = MarketDiscoveryStatus.PendingReview,
            LastSeenAtUtc = DateTime.UtcNow };
        var second = new MarketSkillDiscovery { RawTerm = "PyTorch",
            NormalizedTerm = "PYTORCH", Status = MarketDiscoveryStatus.PendingReview,
            LastSeenAtUtc = DateTime.UtcNow.AddMinutes(-1) };
        db.MarketSkillDiscoveries.AddRange(first, second);
        for (var index = 0; index < 8; index++)
            db.MarketDiscoveryObservations.Add(new MarketDiscoveryObservation
            { SkillDiscoveryId = first.Id, JobId = Guid.NewGuid(), CompanyId = index < 4
                ? Guid.Empty : Guid.Parse("11111111-1111-1111-1111-111111111111") });
        await db.SaveChangesAsync();
        var store = new MarketIntelligenceStore(db);
        var page = Assert.Single(await store.DiscoveriesAsync(false, 0, 1, default));
        Assert.Equal(first.Id, page.Id);
        Assert.Equal(8, page.DistinctJobCount);
        Assert.Equal(2, page.DistinctCompanyCount);
        Assert.Empty(page.ExampleJobIds);
        Assert.Equal(5, (await store.DiscoveryAsync(false, first.Id, default))!.ExampleJobIds.Count);
    }

    [Fact]
    public async Task AcceleratorMarketIntelligenceApprovalReusesStructuredCanonicalSkillIdentity()
    {
        await using var db = Database();
        var skill = new Skill { Name = "ASP.NET Core", NormalizedName = "ASP.NET CORE" };
        var discovery = new MarketSkillDiscovery { RawTerm = "ASP NET CORE",
            NormalizedTerm = "ASP NET CORE", SuggestedSkillId = skill.Id,
            Status = MarketDiscoveryStatus.PendingReview };
        db.Skills.Add(skill); db.MarketSkillDiscoveries.Add(discovery);
        await db.SaveChangesAsync();
        var store = new MarketIntelligenceStore(db);
        await store.ApproveSkillAsync(discovery.Id, new MarketApproveSkillRequest(
            "Engineering", "Web development", null, "Reviewed evidence"),
            DateTime.UtcNow, default);
        Assert.Equal(skill.Id, Assert.Single(db.MarketSkills).SkillId);
        Assert.Single(db.Skills);
        Assert.Equal(MarketDiscoveryStatus.Approved, discovery.Status);
    }

    private static MarketIntelligenceService MarketService(AcceleratorFixture fixture,
        IMarketIntelligenceStore store) => new(store, fixture.Service, fixture.Audit,
            new UnitOfWork(fixture.Db), Options.Create(new MarketIntelligenceOptions()), fixture.Clock,
            NullLogger<MarketIntelligenceService>.Instance);

    private sealed class FakeMarketStore : IMarketIntelligenceStore
    {
        public List<MarketJobSnapshot> Jobs { get; } = [];
        public List<MarketTerm> Terms { get; } = [];
        public Dictionary<Guid, MarketExtraction> Applied { get; } = [];
        public List<Guid> Failures { get; } = [];
        public Guid? FailingJobId { get; set; }
        public int LastTake { get; private set; }
        public bool Completed { get; private set; }
        private int cursor;
        public void ResetCursor() { cursor = 0; Completed = false; }
        public Task BootstrapTaxonomyAsync(DateTime nowUtc, CancellationToken ct) => Task.CompletedTask;
        public Task<MarketProcessingStatus> StatusAsync(CancellationToken ct) =>
            Task.FromResult(new MarketProcessingStatus(null, null, Applied.Count, Failures.Count));
        public Task<IReadOnlyList<MarketJobSnapshot>> NextJobsAsync(int take, CancellationToken ct)
        {
            LastTake = take;
            var result = Jobs.Skip(cursor).Take(take).ToArray();
            cursor += result.Length;
            return Task.FromResult<IReadOnlyList<MarketJobSnapshot>>(result);
        }
        public Task CompleteBatchAsync(CancellationToken ct) { Completed = true; return Task.CompletedTask; }
        public Task<IReadOnlyList<MarketTerm>> SkillsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MarketTerm>>(Terms);
        public Task<IReadOnlyList<MarketTerm>> RolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MarketTerm>>([]);
        public Task<bool> ApplyExtractionAsync(MarketJobSnapshot job, MarketExtraction extraction,
            DateTime nowUtc, CancellationToken ct)
        {
            if (job.Id == FailingJobId) throw new InvalidOperationException("Simulated per-job failure.");
            return Task.FromResult(Applied.TryAdd(job.Id, extraction));
        }
        public Task RecordFailureAsync(Guid jobId, DateTime createdAtUtc, CancellationToken ct)
        { Failures.Add(jobId); return Task.CompletedTask; }
        public Task<IReadOnlyList<MarketPathCard>> PathsAsync(bool roles, string? search,
            string? category, Guid? relatedRoleId, string? location, int? experience,
            string sort, int skip, int take, DateTime nowUtc, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<MarketPathDetail?> DetailAsync(bool role, Guid id, DateTime nowUtc,
            CancellationToken ct) => throw new NotImplementedException();
        public Task<MarketCandidateSignals> CandidateSignalsAsync(Guid candidateId,
            CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, int>> PublishedRoleRequirementsAsync(Guid marketRoleId,
            CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<MarketDiscoveryItem>> DiscoveriesAsync(bool roles, int skip,
            int take, CancellationToken ct) => throw new NotImplementedException();
        public Task<MarketDiscoveryItem?> DiscoveryAsync(bool role, Guid id, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task ApproveSkillAsync(Guid id, MarketApproveSkillRequest request, DateTime nowUtc,
            CancellationToken ct) => throw new NotImplementedException();
        public Task ApproveRoleAsync(Guid id, MarketApproveRoleRequest request, DateTime nowUtc,
            CancellationToken ct) => throw new NotImplementedException();
        public Task MergeSkillAsync(Guid id, MarketMergeRequest request, DateTime nowUtc,
            CancellationToken ct) => throw new NotImplementedException();
        public Task MergeRoleAsync(Guid id, MarketMergeRequest request, DateTime nowUtc,
            CancellationToken ct) => throw new NotImplementedException();
        public Task MergeCanonicalAsync(bool role, Guid sourceId, Guid targetId,
            DateTime nowUtc, CancellationToken ct) => throw new NotImplementedException();
        public Task ReviewAsync(bool role, Guid id, MarketDiscoveryStatus status, string reason,
            CancellationToken ct) => throw new NotImplementedException();
        public Task AddAliasAsync(bool role, Guid id, string alias, string normalized,
            CancellationToken ct) => throw new NotImplementedException();
    }

    private static MarketTerm Term(string name, params string[] aliases) =>
        new(Guid.NewGuid(), name, MarketExtractionPolicy.Normalize(name), aliases, true);
    private static MarketJobSnapshot Snapshot(string title, string description, string[] skills) =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, title, description, null, null, null,
            null, null, skills, "hash");
    private static JobPortalDbContext Database() => new(new DbContextOptionsBuilder<JobPortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static MarketSkill Skill(JobPortalDbContext db, string name, Guid? competency,
        params string[] aliases)
    {
        var skill = new Skill { Name = name, NormalizedName = MarketExtractionPolicy.Normalize(name) };
        var market = new MarketSkill { Skill = skill, SkillId = skill.Id, CompetencyId = competency,
            Slug = name.ToLowerInvariant().Replace(' ', '-'), Category = "Technology" };
        db.MarketSkills.Add(market);
        foreach (var alias in aliases.Append(name))
            db.MarketSkillAliases.Add(new MarketSkillAlias { MarketSkill = market, Text = alias,
                NormalizedText = MarketExtractionPolicy.Normalize(alias) });
        return market;
    }
    private static MarketRole Role(JobPortalDbContext db, string name, params string[] aliases)
    {
        var market = new MarketRole { Name = name,
            NormalizedName = MarketExtractionPolicy.Normalize(name), Family = "Data & AI", Slug = name };
        db.MarketRoles.Add(market);
        foreach (var alias in aliases.Append(name))
            db.MarketRoleAliases.Add(new MarketRoleAlias { MarketRole = market, Text = alias,
                NormalizedText = MarketExtractionPolicy.Normalize(alias) });
        return market;
    }
    private static Company Company(JobPortalDbContext db, string name)
    {
        var company = new Company { Name = name, NormalizedName = name.ToUpperInvariant() };
        db.Companies.Add(company); return company;
    }
    private static void AddJob(JobPortalDbContext db, MarketSkill skill, MarketRole? role,
        Company company, JobStatus status, DateTime published, DateTime? expires)
    {
        var job = new Job { Title = "Data Engineer", Company = company, CompanyId = company.Id,
            Status = status, PublishedAtUtc = published, ExpiresAtUtc = expires };
        db.Jobs.Add(job);
        db.MarketJobSkills.Add(new MarketJobSkill { Job = job, JobId = job.Id,
            MarketSkill = skill, MarketSkillId = skill.Id });
        if (role is not null) db.MarketJobRoles.Add(new MarketJobRole { Job = job,
            JobId = job.Id, MarketRole = role, MarketRoleId = role.Id, IsPrimary = true });
    }
    private static Job FoundationJob(Company company, Skill skill, string title, JobStatus status,
        DateTime? publishedAtUtc, DateTime? expiresAtUtc, bool isDeleted = false)
    {
        var job = new Job { Title = title, Company = company, CompanyId = company.Id, Status = status,
            PublishedAtUtc = publishedAtUtc, ExpiresAtUtc = expiresAtUtc, IsDeleted = isDeleted };
        job.JobSkills.Add(new JobSkill { Job = job, JobId = job.Id, Skill = skill, SkillId = skill.Id });
        return job;
    }
}

public sealed class AcceleratorMarketIntelligencePostgresTests(WorkdayCheckpointDatabase database)
    : IClassFixture<WorkdayCheckpointDatabase>
{
    [MarketLocalPostgresFact]
    public async Task ConcurrentBootstrapWithRetryStrategyCreatesOneFoundationAndOneRevision()
    {
        var now = DateTime.UtcNow;
        await using (var setup = database.Open())
        {
            var company = new Company { Name = "Market bootstrap " + Guid.NewGuid().ToString("N"),
                NormalizedName = "MARKET BOOTSTRAP " + Guid.NewGuid().ToString("N") };
            // The market normalizer deliberately collapses both names to "C". This
            // exercises the actual PostgreSQL unique alias index under concurrent,
            // retry-enabled bootstrap callers.
            var csharp = new Skill { Name = "C#", NormalizedName = "C#" };
            var c = new Skill { Name = "C", NormalizedName = "C" };
            var job = new Job
            {
                ReferenceNumber = "market-bootstrap-" + Guid.NewGuid().ToString("N"),
                Title = "Data Engineer", Slug = "market-bootstrap-" + Guid.NewGuid().ToString("N"),
                Description = "Disposable market bootstrap fixture", ApplicationUrl = "https://example.test/apply",
                Company = company, CompanyId = company.Id, Status = JobStatus.Published,
                PublishedAtUtc = now.AddMinutes(-1)
            };
            job.JobSkills.Add(new JobSkill { Job = job, JobId = job.Id, Skill = csharp, SkillId = csharp.Id });
            job.JobSkills.Add(new JobSkill { Job = job, JobId = job.Id, Skill = c, SkillId = c.Id });
            setup.AddRange(company, csharp, c, job);
            await setup.SaveChangesAsync();
        }

        Task BootstrapAsync() => Task.Run(async () =>
        {
            var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(database.Connection,
                postgres => postgres.EnableRetryOnFailure(3)).Options;
            await using var context = new JobPortalDbContext(options);
            Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);
            await new MarketIntelligenceStore(context).BootstrapTaxonomyAsync(now, default);
        });

        await Task.WhenAll(BootstrapAsync(), BootstrapAsync());

        await using var check = database.Open();
        Assert.Single(await check.AcceleratorTaxonomies.Where(x => x.Version ==
            MarketFoundationTaxonomyPolicy.Version).ToArrayAsync());
        Assert.Equal(2, await check.MarketSkills.CountAsync());
        Assert.Equal(0, await check.MarketSkillAliases.CountAsync());
        Assert.Equal(1, await check.MarketRoles.CountAsync());
        Assert.Equal(1, (await check.MarketProcessingCheckpoints.SingleAsync(x =>
            x.Name == "career-accelerator-market-v1")).TaxonomyRevision);
    }

    private sealed class MarketLocalPostgresFactAttribute : FactAttribute
    {
        public MarketLocalPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires explicitly disposable localhost careerharbor_workday_test PostgreSQL; never uses API/Neon configuration.";
        }
    }
}
