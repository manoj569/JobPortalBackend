using System.Text.Json;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorJourneyEligibilityTests
{
    [Fact]
    public async Task ActiveFullStackSelectionAdvertisesProvisioningWithoutMutatingConfiguration()
    {
        using var f=new AcceleratorFixture(); await f.Activate();
        var marketId=Guid.Parse("523641a2-0532-40ad-96dd-a8f8f64870e2");
        var setup=await AcceleratorJourneyProvisioningTests.Setup(f,marketId:marketId);
        var result=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,marketId,null,default);
        Assert.Equal("Active",result.Entitlement.Status); Assert.Equal("ProvisioningRequired",result.State);
        Assert.Equal(AcceleratorNextAction.ProvisionJourney,result.NextAction); Assert.Equal("MarketRoleNotMapped",result.DiagnosticReason);
        Assert.Equal("Full Stack Developer",result.Title); Assert.Null(result.JourneyContextId);
        Assert.Equal("14",JsonSerializer.Serialize(result.NextAction)); // Existing numeric enum representation remains unchanged.
        await f.Db.SaveChangesAsync(); // Even a later save must not persist objects merely planned by GET.
        Assert.Null((await f.Db.MarketRoles.SingleAsync()).AcceleratorRoleId);
        Assert.Empty(await f.Db.AcceleratorLearningPaths.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorAssessmentTemplates.ToArrayAsync());
        Assert.Empty(await f.Db.AcceleratorPathEnrollments.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorJourneyContexts.ToArrayAsync());
        Assert.Empty(await f.Db.AcceleratorJourneyProvisioningReceipts.ToArrayAsync());
        var provision=await AcceleratorJourneyProvisioningTests.Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        Assert.Equal("Ready",provision.State);
        var configured=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,marketId,null,default);
        Assert.Equal("Ready",configured.State); Assert.Equal(provision.JourneyContextId,configured.JourneyContextId);
        Assert.Equal(AcceleratorNextAction.StartAssessment,configured.NextAction); Assert.Null(configured.DiagnosticReason);
    }

    [Theory]
    [InlineData(0,"NotSubscribed")]
    [InlineData(1,"Expired")]
    [InlineData(2,"ProvisioningRequired")]
    public async Task InactiveOrUnverifiedEntitlementNeverAdvertisesProvisioning(int kind,string status)
    {
        using var f=new AcceleratorFixture(); var setup=await AcceleratorJourneyProvisioningTests.Setup(f);
        if(kind==1) { await f.Activate(); f.Clock.Utc=f.Clock.Utc.AddMonths(2); }
        if(kind==2)
        {
            f.Db.Add(new Membership { UserId=f.Candidate.Id,PlanCode=AcceleratorOptions.PlanCode,PlanName="CareerHarbor AI Career Accelerator",Status=MembershipStatus.Active,StartsAtUtc=f.Clock.Utc,EndsAtUtc=f.Clock.Utc.AddMonths(1) });
            await f.Db.SaveChangesAsync();
        }
        var result=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,setup.Market.Id,null,default);
        Assert.Equal(status,result.Entitlement.Status); Assert.NotEqual("ProvisioningRequired",result.State);
        Assert.NotEqual(AcceleratorNextAction.ProvisionJourney,result.NextAction);
        Assert.Empty(await f.Db.AcceleratorLearningPaths.ToArrayAsync());
    }

    [Theory]
    [InlineData(false,true,"InsufficientCanonicalRoleDemand")]
    [InlineData(true,false,"InsufficientObjectiveQuestionCoverage")]
    public async Task InsufficientCanonicalOrQuestionEvidenceIsUnavailable(bool canonical,bool questions,string reason)
    {
        using var f=new AcceleratorFixture(); await f.Activate();
        var setup=await AcceleratorJourneyProvisioningTests.Setup(f,fullStack:canonical,questions:questions);
        var result=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,setup.Market.Id,null,default);
        Assert.Equal("ProvisioningUnavailable",result.State); Assert.Equal(reason,result.DiagnosticReason);
        Assert.Equal(AcceleratorNextAction.Unavailable,result.NextAction); Assert.Null(result.NextResourceId);
        Assert.Null((await f.Db.MarketRoles.SingleAsync()).AcceleratorRoleId); Assert.Empty(await f.Db.AcceleratorLearningPaths.ToArrayAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidDeletedAndDeprecatedMarketPathsAreNotProvisionable(int kind)
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await AcceleratorJourneyProvisioningTests.Setup(f);
        if(kind==1) setup.Market.IsDeleted=true;
        if(kind==2) setup.Market.Status=MarketConceptStatus.Deprecated;
        await f.Db.SaveChangesAsync();
        var result=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,kind==0 ? Guid.NewGuid() : setup.Market.Id,null,default);
        Assert.Equal("ProvisioningUnavailable",result.State); Assert.Equal("MarketPathNotFound",result.DiagnosticReason);
        Assert.Equal(AcceleratorNextAction.Unavailable,result.NextAction);
    }

    [Fact]
    public async Task SkillSelectionAdvertisesProvisioningAndConfiguredSkillScopeResumesNormally()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); await AcceleratorJourneyProvisioningTests.Setup(f);
        var market=new MarketSkill { SkillId=f.CodeSkill.Id,Slug="eligible-skill" }; f.Db.Add(market); await f.Db.SaveChangesAsync();
        var service=AcceleratorJourneySummaryTests.Service(f);
        var result=await service.GetAsync(f.Candidate.Id,null,null,default,market.Id);
        Assert.Equal("ProvisioningRequired",result.State); Assert.Equal(AcceleratorNextAction.ProvisionJourney,result.NextAction);
        Assert.Null((await f.Db.MarketSkills.SingleAsync()).CompetencyId); Assert.Empty(await f.Db.AcceleratorLearningPaths.ToArrayAsync());
        var provision=await AcceleratorJourneyProvisioningTests.Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),MarketSkillPathId:market.Id),default);
        result=await service.GetAsync(f.Candidate.Id,null,null,default,market.Id);
        Assert.Equal("Ready",result.State); Assert.Equal(provision.JourneyContextId,result.JourneyContextId);
        Assert.Equal(provision.EnrollmentId,result.EnrollmentId); Assert.Equal(AcceleratorNextAction.StartAssessment,result.NextAction);
        Assert.Empty(await f.Db.AcceleratorCareerGoals.ToArrayAsync());
    }

    [Fact]
    public async Task CuratedMappingAndExistingEnrollmentRemainAuthoritative()
    {
        using var f=new AcceleratorFixture(); await f.Activate(); var setup=await AcceleratorJourneyProvisioningTests.Setup(f,fullStack:false);
        setup.Market.AcceleratorRoleId=setup.Role; await f.Db.SaveChangesAsync();
        var provision=await AcceleratorJourneyProvisioningTests.Service(f).ProvisionAsync(f.Candidate.Id,new(Guid.NewGuid(),setup.Market.Id),default);
        var result=await AcceleratorJourneySummaryTests.Service(f).GetAsync(f.Candidate.Id,setup.Market.Id,null,default);
        Assert.Equal("Ready",result.State); Assert.Equal(provision.EnrollmentId,result.EnrollmentId);
        Assert.Equal(provision.JourneyContextId,result.JourneyContextId); Assert.Equal(setup.Role,(await f.Db.MarketRoles.SingleAsync()).AcceleratorRoleId);
        Assert.Single(await f.Db.AcceleratorPathEnrollments.ToArrayAsync());
    }
}
