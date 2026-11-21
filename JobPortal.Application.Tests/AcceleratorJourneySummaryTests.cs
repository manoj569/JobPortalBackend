using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorJourneySummaryTests
{
    [Theory]
    [InlineData("New",AcceleratorNextAction.StartAssessment)]
    [InlineData("Assessment",AcceleratorNextAction.ResumeAssessment)]
    [InlineData("Assessed",AcceleratorNextAction.GenerateRoadmap)]
    [InlineData("Roadmap",AcceleratorNextAction.ContinueLearning)]
    [InlineData("Project",AcceleratorNextAction.ContinueProject)]
    [InlineData("Interview",AcceleratorNextAction.ContinueInterview)]
    [InlineData("JobReady",AcceleratorNextAction.ViewJobs)]
    [InlineData("Applied",AcceleratorNextAction.ViewApplications)]
    [InlineData("Report",AcceleratorNextAction.ViewInterviewReport)]
    [InlineData("MissingAssessment",AcceleratorNextAction.Unavailable)]
    [InlineData("MissingQuestions",AcceleratorNextAction.Unavailable)]
    public void DeterministicPolicySelectsAuthoritativeNextAction(string state,AcceleratorNextAction expected)
    {
        var context=new AcceleratorJourneyContext { UserId=Guid.NewGuid(),ScopeType=AcceleratorJourneyScopeType.CareerGoal,CareerGoalId=Guid.NewGuid() };
        var scope=new AcceleratorJourneyWorkScope(context,Guid.NewGuid(),[new(Guid.NewGuid(),3,1)],[],null);
        var data=Data();
        if (state=="Assessment") data=data with { ActiveAssessmentId=Guid.NewGuid() };
        if (state!="New" && state!="Assessment" && state!="MissingAssessment" && state!="MissingQuestions") data=data with { CompletedAssessments=1 };
        if (state is "Roadmap" or "Project" or "Interview" or "JobReady" or "Applied" or "Report") scope=scope with { Roadmap=new AcceleratorRoadmap() };
        if (state=="Project") data=data with { ActiveProjectId=Guid.NewGuid() };
        if (state=="Interview") data=data with { ActiveInterviewId=Guid.NewGuid() };
        if (state is "JobReady" or "Applied" or "Report") data=data with { CompletedActivities=1,PracticeEvaluations=1,Readiness=state=="Report" ? 50 : 70,LatestReportId=Guid.NewGuid() };
        if (state=="Applied") data=data with { ApplicationCount=1 };
        if (state=="MissingAssessment") data=data with { AssessmentConfigured=false };
        if (state=="MissingQuestions") data=data with { QuestionsAvailable=false };
        var result=AcceleratorJourneyPhasePolicy.Calculate(scope,data,new("Inactive",null,null,[]),70);
        Assert.Equal(expected,result.NextAction);
        Assert.Equal(data.EstimatedMinutesRemaining,result.EstimatedMinutesRemaining);
        Assert.Equal("Inactive",result.Entitlement.Status);
        if (state=="New") Assert.All(result.Phases.Skip(1),x=>Assert.False(x.Unlocked));
        if (state=="JobReady") Assert.True(result.Phases.Single(x=>x.Code=="JobApplicationAndTracking").Unlocked);
        if (state=="MissingAssessment") Assert.Equal("AssessmentNotConfigured",result.State);
        if (state=="MissingQuestions") Assert.Equal("QuestionsUnavailable",result.State);
    }
    [Fact]
    public void ProgressIsDisclosedCheckpointRatioAndOptionalFeaturesDoNotBlockJobReadiness()
    {
        var scope=new AcceleratorJourneyWorkScope(new AcceleratorJourneyContext { CareerGoalId=Guid.NewGuid() },Guid.NewGuid(),[new(Guid.NewGuid(),3,1)],[],null);
        var result=AcceleratorJourneyPhasePolicy.Calculate(scope,Data() with { CompletedAssessments=1,CompletedActivities=1,QuestionsAvailable=false,Readiness=70,RoadmapId=Guid.NewGuid(),RoadmapStatus=AcceleratorRoadmapStatus.Completed },new("Active",null,null,[]),70);
        Assert.Equal(4,result.Checklist.Count); Assert.Equal(75,result.OverallProgressPercent); Assert.Equal(AcceleratorNextAction.ViewJobs,result.NextAction);
        Assert.Null(result.EstimatedMinutesRemaining);
        Assert.Equal(AcceleratorRoadmapStatus.Completed,result.RoadmapStatus);
    }
    [Fact]
    public async Task MarketMappingResolvesCanonicalRoleAndOwnedGoalWithoutCoercingIds()
    {
        using var f=new AcceleratorFixture(); var setup=await AcceleratorPhase3JourneyTests.Setup(f,0);
        var role=await f.Db.AcceleratorCareerRoles.SingleAsync(x=>x.Code=="backend");
        var market=new MarketRole { Name="Backend path",NormalizedName="BACKEND PATH",Slug="backend-path",AcceleratorRoleId=role.Id,Status=MarketConceptStatus.Active };
        f.Db.Add(market); await f.Db.SaveChangesAsync();
        var service=Service(f); var summary=await service.GetAsync(f.Candidate.Id,market.Id,null,default);
        Assert.Equal(setup.GoalId,summary.GoalId); Assert.NotEqual(market.Id,summary.GoalId); Assert.Equal("AssessmentNotConfigured",summary.State);
        Assert.True(summary.Summary!.HasResume);
        Assert.Equal("LearningPathNotConfigured",(await service.GetAsync(f.Other.Id,market.Id,null,default)).State);
        Assert.Equal("MarketRoleNotMapped",(await service.GetAsync(f.Candidate.Id,Guid.NewGuid(),null,default)).State);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.GetAsync(f.Other.Id,null,summary.JourneyContextId,default));
        await Assert.ThrowsAsync<BadRequestException>(()=>service.GetAsync(f.Candidate.Id,market.Id,summary.JourneyContextId,default));
        Assert.Empty(await f.Db.AcceleratorUsageReservations.ToArrayAsync());
    }
    internal static AcceleratorJourneySummaryService Service(AcceleratorFixture f) => new(new AcceleratorJourneySummaryStore(f.Db),f.Work,f.Service,f.Clock,Options.Create(new AcceleratorJourneySummaryOptions()));
    internal static JourneySummaryData Data() => new(Title:"Path",LearningPathId:null,EnrollmentId:null,AssessmentConfigured:true,QuestionsAvailable:true,
        ActiveAssessmentId:null,CompletedAssessments:0,CompletedLessons:0,TotalLessons:0,CompletedActivities:0,TotalActivities:0,PracticeSessions:0,PracticeEvaluations:0,
        Projects:0,CompletedProjects:0,ActiveProjectId:null,EligibleProjects:0,Interviews:0,CompletedInterviews:0,ActiveInterviewId:null,LatestReportId:null,EligibleInterviews:0,
        Readiness:0,EstimatedMinutesRemaining:null,RelevantJobCount:null,ApplicationCount:0,SavedJobCount:0,HasResume:false);
}
