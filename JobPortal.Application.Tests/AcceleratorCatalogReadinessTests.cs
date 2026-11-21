using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorCatalogReadinessTests
{
    internal static AcceleratorCatalogReadinessService Service(AcceleratorFixture f) => new(f.Store,new AcceleratorCatalogReadinessStore(f.Db),new AcceleratorJourneyProvisioningStore(f.Db),f.Service,f.Audit,f.Clock);
    internal static readonly string[] CoreSkills=["C#","ASP.NET Core","SQL","JavaScript","React","REST APIs","Git","Testing","Cloud","System Design"];
    internal static async Task<(Guid Taxonomy,Guid Role,Guid Backend,MarketRole Market)> FullStack(AcceleratorFixture f)
    {
        var skills=new List<Skill> { f.CodeSkill };
        foreach(var name in CoreSkills.Skip(1)) { var skill=new Skill { Name=name,NormalizedName=name.ToUpperInvariant() }; f.Db.Add(skill); skills.Add(skill); }
        await f.Db.SaveChangesAsync();
        var taxonomy=await f.Service.PublishTaxonomyAsync(f.Admin.Id,new("readiness-v1",skills.Select(s=>new CompetencyDefinition(s.Id,"Canonical baseline competency","Engineering",1,null,[],["mcq"],[])).ToArray(),
            [new("full-stack-developer","Full Stack Developer","Core web application baseline",skills.Select(s=>new RoleRequirementDefinition(s.Id,3)).ToArray()),
             new("backend-developer","Backend Developer","Shares existing canonical competencies",skills.Where(s=>s.Name is "C#" or "SQL" or "REST APIs").Select(s=>new RoleRequirementDefinition(s.Id,3)).ToArray())]),default);
        var market=new MarketRole { Id=Guid.Parse("523641a2-0532-40ad-96dd-a8f8f64870e2"),Name="Full Stack Developer",NormalizedName="FULL STACK DEVELOPER",Slug="full-stack-developer" };
        f.Db.Add(market); await f.Db.SaveChangesAsync();
        return (taxonomy.Id,taxonomy.Roles.Single(x=>x.Code=="full-stack-developer").Id,taxonomy.Roles.Single(x=>x.Code=="backend-developer").Id,market);
    }

    [Fact]
    public async Task FullStackBootstrapRepairsCoverageAndReusesCanonicalPoolsAcrossRoles()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await FullStack(f); var service=Service(f);
        var before=await service.RoleAsync(f.Admin.Id,setup.Role,default);
        Assert.Equal(10,before.RequiredCompetencyCount); Assert.Equal(0,before.CoveredCompetencyCount); Assert.Equal(0,before.ObjectiveQuestionCoveragePercent);
        Assert.Equal(0,before.EligibleObjectiveQuestionCount); Assert.Equal(10,before.MissingCompetencyQuestionCoverage.Count);
        Assert.False(before.AssessmentReady);
        var blocked=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,setup.Market.Id,null,default);
        Assert.Equal("InsufficientObjectiveQuestionCoverage",blocked.DiagnosticReason);
        var blockedPost=await AcceleratorJourneyProvisioningTests.Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        Assert.NotNull(blockedPost.InternalCoverage);
        Assert.DoesNotContain("InternalCoverage",JsonSerializer.Serialize(blockedPost),StringComparison.OrdinalIgnoreCase);
        var preview=await service.BootstrapAsync(f.Admin.Id,new(setup.Taxonomy),default);
        Assert.Equal(20,preview.PlannedQuestions); Assert.Equal(0,preview.AddedQuestions); Assert.Empty(await f.Db.AcceleratorQuestions.ToArrayAsync());
        var applied=await service.BootstrapAsync(f.Admin.Id,new(setup.Taxonomy,true),default); Assert.Equal(20,applied.AddedQuestions);
        var after=await service.RoleAsync(f.Admin.Id,setup.Role,default);
        Assert.Equal(10,after.CoveredCompetencyCount); Assert.Equal(100,after.ObjectiveQuestionCoveragePercent); Assert.Equal(20,after.EligibleObjectiveQuestionCount);
        Assert.True(after.AssessmentReady); Assert.True(after.AcceleratorReady); Assert.False(after.LearningReady); Assert.False(after.AcceleratorComplete);
        var backend=await service.RoleAsync(f.Admin.Id,setup.Backend,default);
        Assert.Equal(3,backend.CoveredCompetencyCount); Assert.Equal(6,backend.EligibleObjectiveQuestionCount);
        Assert.Equal(20,await f.Db.AcceleratorQuestions.CountAsync()); Assert.Equal(10,await f.Db.AcceleratorCompetencies.CountAsync());
        var eligible=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,setup.Market.Id,null,default);
        Assert.Equal("ProvisioningRequired",eligible.State); Assert.Equal(AcceleratorNextAction.ProvisionJourney,eligible.NextAction);
        var provision=await AcceleratorJourneyProvisioningTests.Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        Assert.Equal("Ready",provision.State); Assert.NotNull(provision.EnrollmentId); Assert.NotNull(provision.LearningPathId); Assert.NotNull(provision.JourneyContextId);
        Assert.Empty(await f.Db.AcceleratorAssessmentAttempts.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorSkillEvidence.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorRoadmaps.ToArrayAsync());
        var summary=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,null,provision.JourneyContextId,default);
        Assert.Equal("Active",summary.Entitlement.Status); Assert.True(summary.Assessment!.CanStart);
        var attempt=await f.Phase2.StartJourneyAssessmentAsync(f.Candidate.Id,provision.JourneyContextId!.Value,Guid.NewGuid(),default);
        Assert.Equal(10,attempt.Questions.Count);
        var wire=JsonSerializer.Serialize(attempt); Assert.DoesNotContain("AnswerKey",wire,StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Rubric",wire,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0,(await service.BootstrapAsync(f.Admin.Id,new(setup.Taxonomy,true),default)).AddedQuestions);
        Assert.Equal(20,await f.Db.AcceleratorQuestions.CountAsync()); Assert.Equal(13,await f.Db.AcceleratorRoleRequirements.CountAsync());
        Assert.Empty(await f.Db.AcceleratorProgressEvents.ToArrayAsync());
    }

    [Theory]
    [InlineData(0,false)]
    [InlineData(49,false)]
    [InlineData(50,true)]
    [InlineData(100,true)]
    public void SharedCoveragePolicyHasExactBoundaryWithoutOptionalSkillInflation(int covered,bool expected)
    {
        var taxonomy=Guid.NewGuid(); var required=Enumerable.Range(0,100).Select(_=>Guid.NewGuid()).ToArray();
        var definition=AcceleratorBaselineQuestionCatalog.Questions[0];
        var questions=required.Take(covered).Select(id=>definition.Create(taxonomy,id)).Append(definition.Create(taxonomy,Guid.NewGuid())).ToArray();
        var result=AcceleratorAssessmentReadinessPolicy.Evaluate(taxonomy,required,questions);
        Assert.Equal(covered,result.CoveredCompetencyCount); Assert.Equal(covered,result.ObjectiveQuestionCoveragePercent); Assert.Equal(expected,result.AssessmentReady);
        Assert.Equal(covered,result.EligibleObjectiveQuestionCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void InvalidInactiveDeletedUnsupportedAndWrongTaxonomyQuestionsDoNotCount(int invalid)
    {
        var taxonomy=Guid.NewGuid(); var competency=Guid.NewGuid(); var q=AcceleratorBaselineQuestionCatalog.Questions[0].Create(taxonomy,competency);
        if(invalid==0) q.IsActive=false; if(invalid==1) q.IsDeleted=true; if(invalid==2) q.Type=AcceleratorQuestionType.ShortAnswer;
        if(invalid==3) q.TaxonomyId=Guid.NewGuid(); if(invalid==4) q.AnswerKeyJson="not-json"; if(invalid==5) q.Prompt="\t\n";
        var result=AcceleratorAssessmentReadinessPolicy.Evaluate(taxonomy,[competency],[q]);
        Assert.False(result.AssessmentReady); Assert.Equal(0,result.EligibleObjectiveQuestionCount);
    }

    [Fact]
    public async Task CuratedQuestionsAndNaturalKeysArePreserved()
    {
        using var f=new AcceleratorFixture(); var setup=await FullStack(f); var service=Service(f);
        var competency=await f.Db.AcceleratorCompetencies.SingleAsync(x=>x.SkillId==f.CodeSkill.Id);
        var curated=AcceleratorBaselineQuestionCatalog.Questions[0].Create(setup.Taxonomy,competency.Id); curated.Version="curated-v7"; curated.Prompt="Curated content must remain unchanged";
        f.Db.Add(curated); await f.Db.SaveChangesAsync();
        Assert.Equal(18,(await service.BootstrapAsync(f.Admin.Id,new(setup.Taxonomy,true),default)).AddedQuestions);
        Assert.Equal("Curated content must remain unchanged",(await f.Db.AcceleratorQuestions.SingleAsync(x=>x.Id==curated.Id)).Prompt);
        Assert.Single(await f.Db.AcceleratorQuestions.Where(x=>x.CompetencyId==competency.Id).ToArrayAsync());
        Assert.Equal(0,(await service.BootstrapAsync(f.Admin.Id,new(setup.Taxonomy,true),default)).AddedQuestions);
    }

    [Fact]
    public async Task LearningProjectsAndInterviewsAreSeparateReadinessLevels()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await FullStack(f); var service=Service(f);
        await service.BootstrapAsync(f.Admin.Id,new(setup.Taxonomy,true),default);
        var provision=await AcceleratorJourneyProvisioningTests.Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        var module=new AcceleratorLearningModule { LearningPathId=provision.LearningPathId!.Value,Title="Configured learning",Sequence=1,Difficulty=1,Status=AcceleratorLearningCatalogStatus.Published };
        f.Db.AddRange(module,new AcceleratorLesson { ModuleId=module.Id,Title="Configured lesson",Content="Existing approved content",Sequence=1,Type=AcceleratorLessonType.Article,Status=AcceleratorLearningCatalogStatus.Published }); await f.Db.SaveChangesAsync();
        var learning=await service.RoleAsync(f.Admin.Id,setup.Role,default);
        Assert.True(learning.LearningReady); Assert.False(learning.AcceleratorComplete); Assert.Equal(1,learning.PublishedModuleCount); Assert.Equal(1,learning.PublishedLessonCount);
        var comp=await f.Db.AcceleratorCompetencies.FirstAsync();
        await AcceleratorPhase45JourneyTests.SeedProjectCatalog(f.Db,setup.Role,comp.Id);
        await AcceleratorPhase45JourneyTests.SeedInterviewCatalog(f.Db,setup.Role,comp.Id);
        var complete=await service.RoleAsync(f.Admin.Id,setup.Role,default);
        Assert.True(complete.AcceleratorComplete); Assert.True(complete.ProjectsAvailable); Assert.True(complete.InterviewsAvailable);
        Assert.Equal(AcceleratorCatalogReadinessLevel.AcceleratorComplete,complete.Level);
    }

    [Fact]
    public async Task BulkLaunchMarketReportsAreDerivedFilteredAndAdminOnly()
    {
        using var f=new AcceleratorFixture(); var setup=await FullStack(f); var service=Service(f);
        await Assert.ThrowsAsync<AppException>(()=>service.RolesAsync(f.Candidate.Id,new(),default));
        await Assert.ThrowsAsync<AppException>(()=>service.BootstrapAsync(f.Candidate.Id,new(setup.Taxonomy,true),default));
        var before=await service.MarketAsync(f.Admin.Id,setup.Market.Id,default); Assert.Equal("AssessmentCoverageInsufficient",before.State);
        await service.BootstrapAsync(f.Admin.Id,new(setup.Taxonomy,true),default);
        var page=await service.RolesAsync(f.Admin.Id,new(Ready:true,TaxonomyId:setup.Taxonomy),default); Assert.Equal(2,page.Items.Count);
        Assert.Empty((await service.RolesAsync(f.Admin.Id,new(Ready:false,TaxonomyVersion:"readiness-v1"),default)).Items);
        Assert.Single((await service.RolesAsync(f.Admin.Id,new(Code:"full-stack-developer"),default)).Items);
        var market=await service.MarketAsync(f.Admin.Id,setup.Market.Id,default); Assert.True(market.CanProvisionConfiguration); Assert.Equal("LearningContentMissing",market.State);
        var launch=await service.LaunchAsync(f.Admin.Id,setup.Taxonomy,default); Assert.Equal(15,launch.Count); Assert.Equal(2,launch.Count(x=>x.RoleExists));
        var wire=JsonSerializer.Serialize(page); foreach(var secret in new[]{"AnswerKey","Rubric","OptionsJson","Prompt"}) Assert.DoesNotContain(secret,wire,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthoredCatalogIsEntirelyValidatedAndUsesStableRoleIndependentKeys()
    {
        var taxonomy=Guid.NewGuid(); var competency=Guid.NewGuid();
        Assert.Equal(AcceleratorBaselineQuestionCatalog.Questions.Count,AcceleratorBaselineQuestionCatalog.Questions.Select(x=>x.Key).Distinct().Count());
        Assert.All(AcceleratorBaselineQuestionCatalog.Questions,q=>Assert.True(AcceleratorAssessmentReadinessPolicy.IsEligible(q.Create(taxonomy,competency))));
        Assert.DoesNotContain(AcceleratorBaselineQuestionCatalog.Questions,q=>q.QuestionVersion.Contains("full-stack",StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownCanonicalTopicsAreReportedWithoutInventingContentOrLaunchRoles()
    {
        using var f=new AcceleratorFixture(); var skill=new Skill { Name="Unreviewed Widget Framework",NormalizedName="UNREVIEWED WIDGET FRAMEWORK" };
        f.Db.Add(skill); await f.Db.SaveChangesAsync();
        var tax=await f.Service.PublishTaxonomyAsync(f.Admin.Id,new("unknown-topic-v1",[new(skill.Id,"Existing canonical topic","Engineering",1,null,[],["mcq"],[])],
            [new("widget-engineer","Widget Engineer","Existing role",[new(skill.Id,3)])]),default);
        var result=await Service(f).BootstrapAsync(f.Admin.Id,new(tax.Id,true),default);
        Assert.Equal(0,result.AddedQuestions); Assert.Equal(skill.Name,Assert.Single(result.UnsupportedCompetencies).SkillName);
        Assert.Empty(await f.Db.AcceleratorQuestions.ToArrayAsync()); Assert.Single(await f.Db.AcceleratorCareerRoles.ToArrayAsync());
        Assert.All(await Service(f).LaunchAsync(f.Admin.Id,tax.Id,default),x=>Assert.False(x.RoleExists));
    }

    [Fact]
    public void QuestionBankAvailabilityIsDistinctFromTheHundredQuestionBaselineLimit()
    {
        var taxonomy=Guid.NewGuid(); var required=Enumerable.Range(0,200).Select(_=>Guid.NewGuid()).ToArray();
        var definition=AcceleratorBaselineQuestionCatalog.Questions[0];
        var coverage=AcceleratorAssessmentReadinessPolicy.Evaluate(taxonomy,required,required.Select(id=>definition.Create(taxonomy,id)));
        Assert.Equal(200,coverage.CoveredCompetencyCount); Assert.Equal(100,coverage.ObjectiveQuestionCoveragePercent);
        Assert.Equal(100,coverage.SelectedQuestionCount); Assert.Equal(100,coverage.BaselineCoveredCompetencyCount);
        Assert.True(coverage.AssessmentReady); Assert.Empty(coverage.MissingCompetencyIds);
    }

    [Fact]
    public async Task InactiveAndDeletedSeedNaturalKeysAreNeverReactivatedOrReplaced()
    {
        using var f=new AcceleratorFixture(); var setup=await FullStack(f);
        var competency=await f.Db.AcceleratorCompetencies.SingleAsync(x=>x.SkillId==f.CodeSkill.Id);
        var first=AcceleratorBaselineQuestionCatalog.Questions[0].Create(setup.Taxonomy,competency.Id); first.IsActive=false;
        var second=AcceleratorBaselineQuestionCatalog.Questions[1].Create(setup.Taxonomy,competency.Id); second.IsDeleted=true;
        f.Db.AddRange(first,second); await f.Db.SaveChangesAsync();
        var result=await Service(f).BootstrapAsync(f.Admin.Id,new(setup.Taxonomy,true),default);
        Assert.Equal(18,result.AddedQuestions); Assert.Equal(2,result.ExistingNaturalKeys);
        Assert.False((await f.Db.AcceleratorQuestions.SingleAsync(x=>x.Id==first.Id)).IsActive);
        Assert.True((await f.Db.AcceleratorQuestions.IgnoreQueryFilters().SingleAsync(x=>x.Id==second.Id)).IsDeleted);
        Assert.Equal(0,(await Service(f).BootstrapAsync(f.Admin.Id,new(setup.Taxonomy,true),default)).AddedQuestions);
        Assert.Equal(2,await f.Db.AcceleratorQuestions.IgnoreQueryFilters().CountAsync(x=>x.CompetencyId==competency.Id));
    }

    [Fact]
    public async Task InvalidCuratedBaselineIsReportedAsConfigurationGapNotAnUnresolvedRole()
    {
        using var f=new AcceleratorFixture(); var setup=await FullStack(f); var service=Service(f);
        await service.BootstrapAsync(f.Admin.Id,new(setup.Taxonomy,true),default);
        var template=await f.Phase2.UpsertTemplateAsync(f.Admin.Id,new(setup.Role,"retired-baseline",30,100,false),default);
        var path=new AcceleratorLearningPath { Type=AcceleratorLearningPathType.Career,CanonicalCareerRoleId=setup.Role,Title="Reviewed path",Slug="reviewed-invalid-baseline",Difficulty=1,Status=AcceleratorLearningCatalogStatus.Published };
        f.Db.AddRange(path,new AcceleratorLearningPathAssessment { LearningPathId=path.Id,AssessmentTemplateId=template,Purpose=AcceleratorLearningPathAssessmentPurpose.Baseline }); await f.Db.SaveChangesAsync();
        var report=await service.MarketAsync(f.Admin.Id,setup.Market.Id,default);
        Assert.Equal("LearningContentMissing",report.State); Assert.False(report.CanProvisionConfiguration);
        Assert.NotNull(report.Role); Assert.Equal(setup.Role,report.Role.RoleId); Assert.True(report.Role.AssessmentReady);
        Assert.True(report.Role.BaselineAssessmentMapped); Assert.False(report.Role.BaselineAssessmentUsable);
        Assert.Equal("CuratedBaselineAssessmentInvalid",report.DiagnosticReason);
    }

    [Theory]
    [InlineData("Data Structures")]
    [InlineData("DATA STRUCTURES")]
    public async Task DataStructuresClosesEighteenRequirementCoverageGapWithoutReplacingExistingQuestions(string canonicalName)
    {
        using var f=new AcceleratorFixture();
        var skills=new List<Skill> { f.CodeSkill };
        foreach(var name in CoreSkills.Skip(1).Take(7).Append(canonicalName)
            .Concat(Enumerable.Range(1,9).Select(i=>$"Unreviewed specialist competency {i}")))
        {
            var skill=new Skill { Name=name,NormalizedName=name.ToUpperInvariant() };
            f.Db.Add(skill); skills.Add(skill);
        }
        var sharedSkill=new Skill { Name="Python",NormalizedName="PYTHON" };
        f.Db.Add(sharedSkill); await f.Db.SaveChangesAsync();
        var tax=await f.Service.PublishTaxonomyAsync(f.Admin.Id,new("eighteen-requirements-v1",
            skills.Append(sharedSkill).Select(s=>new CompetencyDefinition(s.Id,"Existing canonical competency","Engineering",1,null,[],["mcq"],[])).ToArray(),
            [new("full-stack-developer","Full Stack Developer","Existing eighteen-requirement profile",skills.Select(s=>new RoleRequirementDefinition(s.Id,3)).ToArray())]),default);
        var competencies=await f.Db.AcceleratorCompetencies.Where(x=>x.TaxonomyId==tax.Id).ToArrayAsync();
        // Thirteen eligible questions cover eight required competencies. Eleven other catalog
        // questions belong to an unrequired competency and must not inflate role coverage.
        foreach(var (skill,index) in skills.Take(8).Select((s,i)=>(s,i)))
        {
            var competency=competencies.Single(x=>x.SkillId==skill.Id);
            foreach(var definition in AcceleratorBaselineQuestionCatalog.Questions
                .Where(x=>x.CanonicalSkillNames.Contains(skill.Name.ToUpperInvariant())).Take(index<5 ? 2 : 1))
                f.Db.Add(definition.Create(tax.Id,competency.Id));
        }
        var shared=competencies.Single(x=>x.SkillId==sharedSkill.Id);
        for(var i=0;i<11;i++)
        {
            var curated=AcceleratorBaselineQuestionCatalog.Questions.Single(x=>x.Key=="python-dict").Create(tax.Id,shared.Id);
            curated.Version=$"curated-python-{i}"; f.Db.Add(curated);
        }
        await f.Db.SaveChangesAsync();
        var original=await f.Db.AcceleratorQuestions.AsNoTracking().ToArrayAsync();
        Assert.Equal(24,original.Length);
        var service=Service(f); var role=Assert.Single(tax.Roles);
        var before=await service.RoleAsync(f.Admin.Id,role.Id,default);
        Assert.Equal(18,before.RequiredCompetencyCount); Assert.Equal(8,before.CoveredCompetencyCount);
        Assert.Equal(44.44m,before.ObjectiveQuestionCoveragePercent); Assert.Equal(13,before.EligibleObjectiveQuestionCount);
        Assert.False(before.AssessmentReady);
        var preview=await service.BootstrapAsync(f.Admin.Id,new(tax.Id),default);
        var repeatPreview=await service.BootstrapAsync(f.Admin.Id,new(tax.Id),default);
        Assert.Equal(2,preview.PlannedQuestions); Assert.Equal(preview.PlannedQuestions,repeatPreview.PlannedQuestions);
        Assert.Equal(0,preview.AddedQuestions); Assert.Equal(24,await f.Db.AcceleratorQuestions.CountAsync());
        Assert.Equal(2,(await service.BootstrapAsync(f.Admin.Id,new(tax.Id,true),default)).AddedQuestions);
        var after=await service.RoleAsync(f.Admin.Id,role.Id,default);
        Assert.Equal(18,after.RequiredCompetencyCount); Assert.Equal(9,after.CoveredCompetencyCount);
        Assert.Equal(50m,after.ObjectiveQuestionCoveragePercent); Assert.Equal(15,after.EligibleObjectiveQuestionCount);
        Assert.True(after.AssessmentReady);
        var dataStructures=competencies.Single(x=>x.SkillId==skills[8].Id);
        var added=await f.Db.AcceleratorQuestions.Where(x=>x.CompetencyId==dataStructures.Id).ToArrayAsync();
        Assert.Equal(2,added.Length);
        Assert.All(added,q=> { Assert.Equal(tax.Id,q.TaxonomyId); Assert.True(AcceleratorAssessmentReadinessPolicy.IsEligible(q)); });
        Assert.Equal(0,(await service.BootstrapAsync(f.Admin.Id,new(tax.Id,true),default)).AddedQuestions);
        Assert.Equal(0,(await service.BootstrapAsync(f.Admin.Id,new(tax.Id),default)).PlannedQuestions);
        Assert.Equal(26,await f.Db.AcceleratorQuestions.CountAsync());
        foreach(var question in original)
            Assert.Equal(JsonSerializer.Serialize(question),JsonSerializer.Serialize(await f.Db.AcceleratorQuestions.AsNoTracking().SingleAsync(x=>x.Id==question.Id)));
    }
}
