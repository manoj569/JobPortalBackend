using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorJourneyProvisioningTests
{
    internal static AcceleratorJourneyProvisioningService Service(AcceleratorFixture f) => new(f.Store,new AcceleratorJourneyProvisioningStore(f.Db),f.Service,f.Audit,f.Clock,AcceleratorJourneySummaryTests.Service(f));
    internal static async Task<(MarketRole Market,Guid Role,Guid Competency)> Setup(AcceleratorFixture f,bool fullStack=true,int requirements=1,bool questions=true,Guid? marketId=null)
    {
        var definition=f.Taxonomy();
        var taxonomy=await f.Service.PublishTaxonomyAsync(f.Admin.Id,definition with { Roles=[new(fullStack ? "full-stack-developer" : "backend-developer",fullStack ? "Full Stack Developer" : "Backend Developer","Reviewed role",
            requirements==1 ? [new(f.CodeSkill.Id,3)] : [new(f.CodeSkill.Id,3),new(f.ProductSkill.Id,3)])] },default);
        var market=new MarketRole { Id=marketId ?? Guid.NewGuid(),Name="Full Stack Developer",NormalizedName="FULL STACK DEVELOPER",Slug="full-stack-market",Status=MarketConceptStatus.Active };
        f.Db.Add(market); await f.Db.SaveChangesAsync();
        var competency=taxonomy.Skills.Single(x=>x.SkillId==f.CodeSkill.Id);
        if(questions) await Question(f,competency.Id,taxonomy.Id);
        return (market,taxonomy.Roles[0].Id,competency.Id);
    }
    internal static async Task Question(AcceleratorFixture f,Guid competency,Guid taxonomy) =>
        await f.Phase2.UpsertQuestionAsync(f.Admin.Id,new(taxonomy,competency,"provision-test-v1",AcceleratorQuestionType.MultipleChoice,1,1,"Choose the valid option","[\"A\",\"B\"]","{\"answer\":\"A\"}","{}","Reviewed objective question",true),default);

    [Fact]
    public async Task PaidFullStackResolvesRoleCreatesSharedConfigurationAndCompletesRealBaseline()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await Setup(f,requirements:2);
        var count=await f.Db.AcceleratorCareerRoles.CountAsync(); var service=Service(f);
        var request=new ProvisionAcceleratorJourneyRequest(Guid.NewGuid(),setup.Market.Id);
        var before=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,setup.Market.Id,null,default);
        Assert.Equal("ProvisioningRequired",before.State); Assert.Equal(AcceleratorNextAction.ProvisionJourney,before.NextAction);
        Assert.Empty(await f.Db.AcceleratorLearningPaths.ToArrayAsync());
        var provision=await service.ProvisionAsync(f.Candidate.Id,request,default);
        Assert.Equal("Ready",provision.State); Assert.Null(provision.DiagnosticReason);
        Assert.Equal(2,provision.RequiredCompetencies); Assert.Equal(1,provision.CoveredCompetencies); Assert.Single(provision.UncoveredCompetencyIds);
        Assert.True(provision.IsPartialBaseline); Assert.Equal(50m,provision.CompetencyCoveragePercent);
        Assert.Equal(setup.Role,(await f.Db.MarketRoles.SingleAsync()).AcceleratorRoleId);
        Assert.Equal(count,await f.Db.AcceleratorCareerRoles.CountAsync());
        Assert.Equal(AcceleratorConfigurationSource.AutoProvisioned,(await f.Db.AcceleratorLearningPaths.SingleAsync()).ConfigurationSource);
        var replay=await service.ProvisionAsync(f.Candidate.Id,request,default);
        Assert.Equal(provision.JourneyContextId,replay.JourneyContextId);
        Assert.Equal(provision.EnrollmentId,(await service.ProvisionAsync(f.Candidate.Id,request with { RequestKey=Guid.NewGuid() },default)).EnrollmentId);
        Assert.Single(await f.Db.AcceleratorLearningPaths.ToArrayAsync()); Assert.Single(await f.Db.AcceleratorAssessmentTemplates.ToArrayAsync());
        Assert.Single(await f.Db.AcceleratorPathEnrollments.ToArrayAsync());
        Assert.Empty(await f.Db.AcceleratorSkillEvidence.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorProgressEvents.ToArrayAsync());
        var summary=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,setup.Market.Id,null,default);
        Assert.Equal("Ready",summary.State); Assert.Equal(AcceleratorNextAction.StartAssessment,summary.NextAction); Assert.Equal(0,summary.OverallProgressPercent);
        var attempt=await f.Phase2.StartJourneyAssessmentAsync(f.Candidate.Id,provision.JourneyContextId!.Value,Guid.NewGuid(),default);
        Assert.Single(attempt.Questions); Assert.DoesNotContain("AnswerKey",JsonSerializer.Serialize(attempt),StringComparison.OrdinalIgnoreCase);
        attempt=await f.Phase2.SubmitAnswerAsync(f.Candidate.Id,attempt.Id,new(attempt.Questions[0].Id,"{\"answer\":\"B\"}"),default);
        Assert.Equal(0m,(await f.Phase2.FinalizeAttemptAsync(f.Candidate.Id,attempt.Id,new(attempt.Revision),default)).Score);
        var gaps=await f.Phase2.EnrollmentGapsAsync(f.Candidate.Id,provision.EnrollmentId!.Value,default);
        Assert.Contains(gaps,x=>x.Current==AcceleratorProficiency.NotAssessed && x.Confidence==0);
        Assert.NotEmpty((await f.Phase2.GenerateEnrollmentRoadmapAsync(f.Candidate.Id,provision.EnrollmentId.Value,Guid.NewGuid(),default)).Nodes);
        Assert.Equal(AcceleratorNextAction.ContinueLearning,(await service.ProvisionAsync(f.Candidate.Id,request,default)).NextAction);
        Assert.Empty(await f.Db.AcceleratorCareerGoals.ToArrayAsync());
        Assert.Empty(await f.Db.AcceleratorUsageReservations.ToArrayAsync());
    }

    [Fact]
    public async Task CuratedMappingPathAndBaselineTakePriorityAndProgressIsNeverReset()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await Setup(f,fullStack:false);
        setup.Market.AcceleratorRoleId=setup.Role; await f.Db.SaveChangesAsync();
        var template=await f.Phase2.UpsertTemplateAsync(f.Admin.Id,new(setup.Role,"curated",20,10,true),default);
        var path=new AcceleratorLearningPath { Type=AcceleratorLearningPathType.Career,CanonicalCareerRoleId=setup.Role,Slug="curated-full-stack",Title="Reviewed curriculum",Difficulty=2,Status=AcceleratorLearningCatalogStatus.Published,Description="Do not overwrite this content" };
        f.Db.AddRange(path,new AcceleratorLearningPathAssessment { LearningPathId=path.Id,AssessmentTemplateId=template,Purpose=AcceleratorLearningPathAssessmentPurpose.Baseline }); await f.Db.SaveChangesAsync();
        var service=Service(f); var result=await service.ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        Assert.Equal(path.Id,result.LearningPathId); Assert.Equal(setup.Role,(await f.Db.MarketRoles.SingleAsync()).AcceleratorRoleId);
        Assert.Equal("Do not overwrite this content",(await f.Db.AcceleratorLearningPaths.SingleAsync()).Description);
        Assert.Equal(AcceleratorConfigurationSource.Curated,(await f.Db.AcceleratorLearningPaths.SingleAsync()).ConfigurationSource);
        var enrollment=await f.Db.AcceleratorPathEnrollments.SingleAsync(); enrollment.OverallProgressPercent=40; await f.Db.SaveChangesAsync();
        await service.ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        Assert.Equal(40,(await f.Db.AcceleratorPathEnrollments.SingleAsync()).OverallProgressPercent);
        Assert.Single(await f.Db.AcceleratorAssessmentTemplates.ToArrayAsync());
        await new MarketIntelligenceStore(f.Db).BootstrapTaxonomyAsync(f.Clock.Utc,default);
        Assert.Equal(setup.Role,(await f.Db.MarketRoles.SingleAsync(x=>x.Id==setup.Market.Id)).AcceleratorRoleId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnpaidAndExpiredCandidatesCannotCreateConfiguration(bool expired)
    {
        using var f=new AcceleratorFixture(); var setup=await Setup(f);
        if(expired) { await f.Activate(); f.Clock.Utc=f.Clock.Utc.AddMonths(2); }
        var error=await Assert.ThrowsAsync<AppException>(()=>Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default));
        Assert.Equal(403,error.StatusCode); Assert.Null((await f.Db.MarketRoles.SingleAsync()).AcceleratorRoleId);
        Assert.Empty(await f.Db.AcceleratorLearningPaths.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorJourneyProvisioningReceipts.ToArrayAsync());
    }

    [Fact]
    public async Task SkillPathUsesCanonicalSkillAndCandidateOwnedContextWithoutFakeRole()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await Setup(f);
        var market=new MarketSkill { SkillId=f.CodeSkill.Id,Slug="csharp-market" }; f.Db.Add(market); await f.Db.SaveChangesAsync();
        var request=new ProvisionAcceleratorJourneyRequest(Guid.NewGuid(),MarketSkillPathId:market.Id);
        var result=await Service(f).ProvisionAsync(f.Candidate.Id,request,default);
        Assert.Equal("Ready",result.State); Assert.Equal(setup.Competency,(await f.Db.MarketSkills.SingleAsync()).CompetencyId);
        var path=await f.Db.AcceleratorLearningPaths.SingleAsync(); Assert.Equal(AcceleratorLearningPathType.Skill,path.Type); Assert.Null(path.CanonicalCareerRoleId);
        Assert.Null((await f.Db.AcceleratorAssessmentTemplates.SingleAsync()).RoleId);
        Assert.NotEmpty((await f.Phase2.StartJourneyAssessmentAsync(f.Candidate.Id,result.JourneyContextId!.Value,Guid.NewGuid(),default)).Questions);
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Work.OwnedAsync(f.Other.Id,result.JourneyContextId.Value,default));
        Assert.Empty(await f.Db.AcceleratorCareerGoals.ToArrayAsync());
    }

    [Fact]
    public async Task MissingQuestionsAndUntrustedMarketRecordsNeverPublishFakeConfiguration()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await Setup(f,questions:false);
        var result=await Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        Assert.Equal("ProvisioningUnavailable",result.State); Assert.Equal("InsufficientObjectiveQuestionCoverage",result.DiagnosticReason);
        Assert.Empty(await f.Db.AcceleratorLearningPaths.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorAssessmentTemplates.ToArrayAsync());
        Assert.Null((await f.Db.MarketRoles.SingleAsync()).AcceleratorRoleId);
        setup.Market.Status=MarketConceptStatus.Deprecated; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(()=>Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default));
        await Assert.ThrowsAsync<NotFoundException>(()=>Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),Guid.NewGuid()),default));
        await Assert.ThrowsAsync<BadRequestException>(()=>Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id,Guid.NewGuid()),default));
        Assert.DoesNotContain(typeof(ProvisionAcceleratorJourneyRequest).GetProperties(),x=>x.Name is "RoleId" or "TaxonomyId" or "RoleName");
    }

    [Fact]
    public async Task RequestKeysAreBoundToMarketIdentityAndSuccessfulReceiptsAreAppendOnly()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await Setup(f);
        var service=Service(f); var key=Guid.NewGuid(); await service.ProvisionAsync(f.Candidate.Id,new(key,setup.Market.Id),default);
        var skill=new MarketSkill { SkillId=f.CodeSkill.Id,CompetencyId=setup.Competency,Slug="second" }; f.Db.Add(skill); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(()=>service.ProvisionAsync(f.Candidate.Id,new(key,MarketSkillPathId:skill.Id),default));
        var receipt=await f.Db.AcceleratorJourneyProvisioningReceipts.SingleAsync(); receipt.PolicyVersion="changed";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task NewRoleRequiresIndependentActiveJobDemandAndReusesCanonicalCompetencies(int excluded)
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await Setup(f,fullStack:false);
        var competency=await f.Db.AcceleratorCompetencies.SingleAsync(x=>x.Id==setup.Competency);
        var skill=new MarketSkill { SkillId=f.CodeSkill.Id,CompetencyId=excluded==0 ? null : competency.Id,Slug="canonical-skill" }; f.Db.Add(skill);
        var companies=new[]{new Company { Name="A",NormalizedName="A" },new Company { Name="B",NormalizedName="B" }}; f.Db.AddRange(companies);
        for(var i=0;i<3;i++)
        {
            var job=new Job { Title="Full Stack Developer",CompanyId=companies[i%2].Id,Status=JobStatus.Published,PublishedAtUtc=f.Clock.Utc.AddHours(-1),ExpiresAtUtc=f.Clock.Utc.AddDays(1) };
            if(i==0)
            {
                if(excluded==1) job.ExpiresAtUtc=f.Clock.Utc;
                if(excluded==2) job.IsDeleted=true;
                if(excluded==3) job.Status=JobStatus.Draft;
                if(excluded==4) job.PublishedAtUtc=f.Clock.Utc.AddDays(1);
                if(excluded==5) job.IsHidden=true;
            }
            f.Db.AddRange(job,new MarketJobRole { JobId=job.Id,MarketRoleId=setup.Market.Id,Confidence=i==0 && excluded==6 ? .1m : 1,Source=MarketMappingSource.Administrator },
                new MarketJobSkill { JobId=job.Id,MarketSkillId=skill.Id,Confidence=1,Source=MarketMappingSource.Administrator });
        }
        await f.Db.SaveChangesAsync();
        var hash=(await f.Db.AcceleratorTaxonomies.SingleAsync()).ContentHash;
        var first=await Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        if(excluded!=0)
        {
            Assert.Equal("ProvisioningUnavailable",first.State); Assert.Equal("InsufficientCanonicalRoleDemand",first.DiagnosticReason);
            Assert.Single(await f.Db.AcceleratorCareerRoles.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorLearningPaths.ToArrayAsync()); return;
        }
        Assert.Equal("Ready",first.State);
        var role=await f.Db.AcceleratorCareerRoles.SingleAsync(x=>x.Code=="full-stack-developer");
        Assert.Equal(AcceleratorConfigurationSource.AutoProvisioned,role.ConfigurationSource);
        Assert.Equal(competency.Id,Assert.Single(await f.Db.AcceleratorRoleRequirements.Where(x=>x.RoleId==role.Id).ToArrayAsync()).CompetencyId);
        Assert.Equal(hash,(await f.Db.AcceleratorTaxonomies.SingleAsync()).ContentHash);
        await Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        Assert.Single(await f.Db.AcceleratorCareerRoles.Where(x=>x.Code=="full-stack-developer").ToArrayAsync());
    }

    [Fact]
    public async Task MissingDemandRemainsControlledAndDoesNotInventRoleSkills()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await Setup(f,fullStack:false);
        var result=await Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        Assert.Equal("InsufficientCanonicalRoleDemand",result.DiagnosticReason);
        Assert.Single(await f.Db.AcceleratorCareerRoles.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorPathEnrollments.ToArrayAsync());
    }

    [Fact]
    public async Task ExactApprovedSlugResolvesCanonicalRoleWithoutFuzzyTitleMatching()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await Setup(f);
        setup.Market.Name="Web Delivery"; setup.Market.NormalizedName="WEB DELIVERY"; setup.Market.Slug="full-stack-developer";
        await f.Db.SaveChangesAsync();
        Assert.Equal("Ready",(await Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default)).State);
        Assert.Equal(setup.Role,(await f.Db.MarketRoles.SingleAsync()).AcceleratorRoleId);
        Assert.Single(await f.Db.AcceleratorCareerRoles.ToArrayAsync());
    }

    [Theory]
    [InlineData(1,1,true)]
    [InlineData(2,1,true)]
    [InlineData(3,1,false)]
    [InlineData(3,2,true)]
    [InlineData(1,0,false)]
    public void CoverageThresholdIsExplicitAndDoesNotTreatUnassessedSkillsAsEvidence(int total,int covered,bool valid) =>
        Assert.Equal(valid,AcceleratorJourneyProvisioningPolicy.SufficientCoverage(total,covered));
}
