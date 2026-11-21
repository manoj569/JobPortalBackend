using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorRecoveryTests
{
    [Fact]
    public async Task FourQuestionAssessmentRecoversAcrossDevicesWithoutBrowserKeys()
    {
        using var f=new AcceleratorFixture();
        var extra=Enumerable.Range(1,3).Select(i=>new Skill { Name=$"Recovery skill {i}",NormalizedName=$"RECOVERY SKILL {i}" }).ToArray();
        f.Db.AddRange(extra); await f.Db.SaveChangesAsync();
        var definition=f.Taxonomy();
        var taxonomy=await f.Service.PublishTaxonomyAsync(f.Admin.Id,definition with {
            Skills=[..definition.Skills,..extra.Select(x=>new CompetencyDefinition(x.Id,"Recovery competency","Engineering",1,null,[],["coding"],[]))],
            Roles=[definition.Roles[0] with { Requirements=[..definition.Roles[0].Requirements,..extra.Select(x=>new RoleRequirementDefinition(x.Id,3))] }]
        },default);
        var role=Assert.Single(taxonomy.Roles);
        var goal=await f.Service.CreateGoalAsync(f.Candidate.Id,f.Goal(role.Id),default);
        var template=new AcceleratorAssessmentTemplate { RoleId=role.Id,TaxonomyId=taxonomy.Id,Version="recovery-v1",IsActive=true,TimeLimitMinutes=30,MaximumQuestions=4 };
        f.Db.Add(template);
        foreach(var competency in taxonomy.Skills.Where(x=>x.SkillId!=f.ProductSkill.Id))
            f.Db.Add(new AcceleratorQuestion { TaxonomyId=taxonomy.Id,CompetencyId=competency.Id,Version="v1",IsActive=true,Type=AcceleratorQuestionType.MultipleChoice,
                Difficulty=1,Weight=1,Prompt="Choose A or B",OptionsJson="[\"A\",\"B\"]",AnswerKeyJson="{\"answer\":\"A\"}" });
        await f.Db.SaveChangesAsync();
        var attempt=await f.Phase2.StartGoalAssessmentAsync(f.Candidate.Id,goal.Id,Guid.NewGuid(),default);
        Assert.Equal(4,attempt.Questions.Count);
        foreach(var question in attempt.Questions.Take(3))
            attempt=await f.Phase2.SubmitAnswerAsync(f.Candidate.Id,attempt.Id,new(question.Id,"{\"answer\":\"A\"}"),default);
        using var device=new JobPortalDbContext(f.DbOptions);
        var recoveredService=Phase2(f,device);
        var summary=await Summary(f,device).GetAsync(f.Candidate.Id,null,attempt.JourneyContextId,default);
        Assert.Equal(attempt.Id,summary.Assessment!.AttemptId);
        Assert.Equal(3,summary.Assessment.AnsweredQuestionCount);
        Assert.Equal(4,summary.Assessment.QuestionCount);
        Assert.True(summary.Assessment.CanResume);
        Assert.Equal(75,summary.Phases.Single(x=>x.Code=="Foundation").ProgressPercent);
        var recovered=await recoveredService.CurrentAttemptAsync(f.Candidate.Id,summary.JourneyContextId!.Value,default);
        Assert.NotNull(recovered); Assert.Equal(attempt.Revision,recovered.Revision); Assert.Null(recovered.Score);
        Assert.Equal(3,recovered.Questions.Count(x=>x.IsAnswered));
        Assert.All(recovered.Questions.Where(x=>x.IsAnswered),x=>Assert.Equal("{\"answer\":\"A\"}",x.SubmittedAnswerJson));
        Assert.All(recovered.Questions.Where(x=>!x.IsAnswered),x=>Assert.Null(x.SubmittedAnswerJson));
        var json=JsonSerializer.Serialize(recovered);
        foreach(var secret in new[]{"AnswerKey","Rubric","IsCorrect","EvaluationEvidence"}) Assert.DoesNotContain(secret,json,StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<NotFoundException>(()=>recoveredService.AttemptAsync(f.Other.Id,attempt.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>recoveredService.CurrentAttemptAsync(f.Other.Id,summary.JourneyContextId.Value,default));
        recovered=await recoveredService.SubmitAnswerAsync(f.Candidate.Id,recovered.Id,new(recovered.Questions.Single(x=>!x.IsAnswered).Id,"{\"answer\":\"A\"}"),default);
        var completed=await recoveredService.FinalizeAttemptAsync(f.Candidate.Id,recovered.Id,new(recovered.Revision),default);
        Assert.Equal(100m,completed.Score); Assert.False(completed.CanResume);
        Assert.Null(await recoveredService.CurrentAttemptAsync(f.Candidate.Id,summary.JourneyContextId.Value,default));
        Assert.Equal(completed.Revision,(await recoveredService.FinalizeAttemptAsync(f.Candidate.Id,completed.Id,new(Guid.NewGuid()),default)).Revision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CurrentAttemptExpiryAndServerStartRespectJourneyAndIdempotency(int kind)
    {
        using var f=new AcceleratorFixture(); var scope=await AcceleratorPhase3JourneyTests.Setup(f,kind);
        var competency=await f.Db.AcceleratorCompetencies.SingleAsync(x=>x.Id==scope.CompetencyId);
        var role=await f.Db.AcceleratorCareerRoles.SingleAsync(x=>x.Code=="backend");
        var template=new AcceleratorAssessmentTemplate { RoleId=kind==2 ? null : role.Id,TaxonomyId=competency.TaxonomyId,Version="recovery-v1",IsActive=true,TimeLimitMinutes=30,MaximumQuestions=1 };
        f.Db.Add(template);
        if(scope.EnrollmentId.HasValue)
        {
            var enrollment=await f.Db.AcceleratorPathEnrollments.SingleAsync(x=>x.Id==scope.EnrollmentId);
            f.Db.Add(new AcceleratorLearningPathAssessment { LearningPathId=enrollment.LearningPathId,AssessmentTemplateId=template.Id,Purpose=AcceleratorLearningPathAssessmentPurpose.Baseline });
        }
        await f.Db.SaveChangesAsync();
        var context=await f.Work.ResolveAsync(f.Candidate.Id,scope.GoalId,scope.EnrollmentId,default);
        var key=Guid.NewGuid();
        var attempt=await f.Phase2.StartJourneyAssessmentAsync(f.Candidate.Id,context.Context.Id,key,default);
        Assert.Equal(attempt.Id,(await f.Phase2.StartJourneyAssessmentAsync(f.Candidate.Id,context.Context.Id,key,default)).Id);
        using var device=new JobPortalDbContext(f.DbOptions); var service=Phase2(f,device);
        Assert.Equal(attempt.Id,(await service.CurrentAttemptAsync(f.Candidate.Id,context.Context.Id,default))!.Id);
        f.Clock.Utc=attempt.ExpiresAtUtc;
        Assert.Null(await service.CurrentAttemptAsync(f.Candidate.Id,context.Context.Id,default));
        Assert.False((await service.AttemptAsync(f.Candidate.Id,attempt.Id,default)).CanResume);
        var summary=await Summary(f,device).GetAsync(f.Candidate.Id,null,context.Context.Id,default);
        Assert.False(summary.Assessment!.CanResume); Assert.True(summary.Assessment.CanStart);
        Assert.Null(summary.Summary!.ActiveAssessmentId);
        var next=await service.StartJourneyAssessmentAsync(f.Candidate.Id,context.Context.Id,Guid.NewGuid(),default);
        Assert.NotEqual(attempt.Id,next.Id);
        Assert.Equal(attempt.Id,(await service.StartJourneyAssessmentAsync(f.Candidate.Id,context.Context.Id,key,default)).Id);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.StartJourneyAssessmentAsync(f.Other.Id,context.Context.Id,Guid.NewGuid(),default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InterviewAnswerCanBeEvaluatedAfterRefreshWithoutRememberingAnswerId(int kind)
    {
        using var f=new AcceleratorFixture(); var scope=await AcceleratorPhase3JourneyTests.Setup(f,kind);
        var version=await AcceleratorPhase45JourneyTests.SeedInterview(f,scope.CompetencyId);
        var service=AcceleratorPhase45JourneyTests.Interviews(f);
        var session=await service.StartAsync(f.Candidate.Id,new(Guid.NewGuid(),scope.GoalId,version.Id,false,scope.EnrollmentId),default);
        var question=await service.NextQuestionAsync(f.Candidate.Id,session.Id,default); Assert.NotNull(question);
        var answer=await service.SubmitAnswerAsync(f.Candidate.Id,session.Id,new(Guid.NewGuid(),question.Id,"A",session.Revision),default);
        f.Db.ChangeTracker.Clear();
        var refreshed=await service.SessionAsync(f.Candidate.Id,session.Id,default);
        var submitted=Assert.Single(refreshed.Questions);
        Assert.Equal(answer.Id,submitted.AnswerId); Assert.Equal(answer.SubmittedAtUtc,submitted.SubmittedAtUtc);
        Assert.Equal(InterviewAnswerRecoveryStatus.Submitted,submitted.AnswerStatus);
        Assert.Equal(question.Id,refreshed.CurrentQuestionId); Assert.Equal(question.SectionId,refreshed.CurrentSectionId);
        var evaluation=await service.EvaluateDeterministicAsync(f.Candidate.Id,session.Id,submitted.AnswerId!.Value,default);
        Assert.Equal(evaluation.Id,(await service.EvaluateDeterministicAsync(f.Candidate.Id,session.Id,submitted.AnswerId.Value,default)).Id);
        refreshed=await service.SessionAsync(f.Candidate.Id,session.Id,default);
        Assert.Equal(evaluation.Id,Assert.Single(refreshed.Questions).EvaluationId);
        Assert.Equal(InterviewAnswerRecoveryStatus.Evaluated,Assert.Single(refreshed.Questions).AnswerStatus);
        Assert.Null(refreshed.CurrentQuestionId);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.SessionAsync(f.Other.Id,session.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.EvaluationAsync(f.Other.Id,session.Id,answer.Id,default));
        var json=JsonSerializer.Serialize(refreshed);
        Assert.DoesNotContain("Rules",json,StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Rubric",json,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MarketSkillResolutionUsesSkillPathAndNeverAnotherCandidatesEnrollment()
    {
        using var f=new AcceleratorFixture(); var scope=await AcceleratorPhase3JourneyTests.Setup(f,2);
        var market=new MarketSkill { SkillId=f.CodeSkill.Id,CompetencyId=scope.CompetencyId,Slug="recovery-skill" };
        f.Db.Add(market); await f.Db.SaveChangesAsync(); var service=AcceleratorJourneySummaryTests.Service(f);
        var summary=await service.GetAsync(f.Candidate.Id,null,null,default,market.Id);
        Assert.Equal(scope.EnrollmentId,summary.EnrollmentId); Assert.Null(summary.GoalId); Assert.NotNull(summary.JourneyContextId);
        var other=await service.GetAsync(f.Other.Id,null,null,default,market.Id);
        Assert.Equal("EnrollmentRequired",other.State); Assert.Null(other.JourneyContextId); Assert.Null(other.EnrollmentId);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.GetAsync(f.Other.Id,null,summary.JourneyContextId,default));
        Assert.Equal("MarketSkillNotMapped",(await service.GetAsync(f.Candidate.Id,null,null,default,Guid.NewGuid())).State);
        await Assert.ThrowsAsync<BadRequestException>(()=>service.GetAsync(f.Candidate.Id,market.Id,null,default,market.Id));
        market.CompetencyId=null; await f.Db.SaveChangesAsync();
        Assert.Equal("MarketSkillNotMapped",(await service.GetAsync(f.Candidate.Id,null,null,default,market.Id)).State);
    }

    [Fact]
    public async Task ServerSelectedGoalTemplatePreservesLegacyReplayAndRejectsDifferentAssessments()
    {
        using var f=new AcceleratorFixture(); var setup=await AcceleratorPhase3JourneyTests.Setup(f,0);
        await Assert.ThrowsAsync<ConflictException>(()=>f.Phase2.StartGoalAssessmentAsync(f.Candidate.Id,setup.GoalId,Guid.NewGuid(),default));
        var role=await f.Db.AcceleratorCareerRoles.SingleAsync(x=>x.Code=="backend");
        var template=new AcceleratorAssessmentTemplate { RoleId=role.Id,TaxonomyId=role.TaxonomyId,IsActive=true,Version="first",TimeLimitMinutes=30,MaximumQuestions=1 };
        f.Db.Add(template); await f.Db.SaveChangesAsync();
        var key=Guid.NewGuid(); var attempt=await f.Phase2.StartGoalAssessmentAsync(f.Candidate.Id,setup.GoalId,key,default);
        var hash=(await f.Db.AcceleratorAssessmentAttempts.SingleAsync()).RequestHash;
        Assert.Equal(attempt.Id,(await f.Phase2.StartAttemptAsync(f.Candidate.Id,new(key,setup.GoalId,template.Id),default)).Id);
        var next=new AcceleratorAssessmentTemplate { RoleId=role.Id,TaxonomyId=role.TaxonomyId,IsActive=true,Version="second",TimeLimitMinutes=30,MaximumQuestions=1 };
        template.IsActive=false; f.Db.Add(next); await f.Db.SaveChangesAsync();
        Assert.Equal(attempt.Id,(await f.Phase2.StartGoalAssessmentAsync(f.Candidate.Id,setup.GoalId,key,default)).Id);
        Assert.Equal(hash,(await f.Db.AcceleratorAssessmentAttempts.SingleAsync()).RequestHash);
        await Assert.ThrowsAsync<ConflictException>(()=>f.Phase2.StartAttemptAsync(f.Candidate.Id,new(key,setup.GoalId,next.Id),default));
        var secondGoal=await f.Service.CreateGoalAsync(f.Candidate.Id,f.Goal(role.Id),default);
        await Assert.ThrowsAsync<ConflictException>(()=>f.Phase2.StartGoalAssessmentAsync(f.Candidate.Id,secondGoal.Id,key,default));
        var question=Assert.Single(attempt.Questions);
        var saved=await f.Phase2.SubmitAnswerAsync(f.Candidate.Id,attempt.Id,new(question.Id,"{\"answer\":\"A\"}"),default);
        Assert.True(Assert.Single(saved.Questions).AnswerIsImmutable);
        Assert.Equal(saved.Revision,(await f.Phase2.SubmitAnswerAsync(f.Candidate.Id,attempt.Id,new(question.Id,"{\"answer\":\"A\"}"),default)).Revision);
        await Assert.ThrowsAsync<ConflictException>(()=>f.Phase2.SubmitAnswerAsync(f.Candidate.Id,attempt.Id,new(question.Id,"{\"answer\":\"B\"}"),default));
    }

    [Fact]
    public async Task InterviewRecoveryHistoryRemainsEnrollmentScoped()
    {
        using var f=new AcceleratorFixture(); var setup=await AcceleratorPhase3JourneyTests.Setup(f,2);
        var firstEnrollment=await f.Db.AcceleratorPathEnrollments.SingleAsync(x=>x.Id==setup.EnrollmentId);
        var second=new AcceleratorPathEnrollment { UserId=f.Candidate.Id,LearningPathId=firstEnrollment.LearningPathId,Status=AcceleratorLearningEnrollmentStatus.InProgress,WeeklyCommitmentMinutes=300,TargetLevel=3 };
        f.Db.Add(second); await f.Db.SaveChangesAsync();
        var version=await AcceleratorPhase45JourneyTests.SeedInterview(f,setup.CompetencyId);
        var service=AcceleratorPhase45JourneyTests.Interviews(f);
        var first=await service.StartAsync(f.Candidate.Id,new(Guid.NewGuid(),Guid.Empty,version.Id,false,setup.EnrollmentId),default);
        var other=await service.StartAsync(f.Candidate.Id,new(Guid.NewGuid(),Guid.Empty,version.Id,false,second.Id),default);
        Assert.NotEqual(first.JourneyContextId,other.JourneyContextId);
        Assert.Equal(first.Id,Assert.Single(await service.EnrollmentHistoryAsync(f.Candidate.Id,setup.EnrollmentId!.Value,1,20,default)).Id);
        Assert.Equal(other.Id,Assert.Single(await service.EnrollmentHistoryAsync(f.Candidate.Id,second.Id,1,20,default)).Id);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.EnrollmentHistoryAsync(f.Other.Id,second.Id,1,20,default));
    }

    [Theory]
    [InlineData(0,4,0)]
    [InlineData(1,3,33)]
    [InlineData(3,4,75)]
    [InlineData(4,4,100)]
    [InlineData(8,4,100)]
    public void PhaseProgressUsesPersistedWorkRatiosWithoutChangingOverallPolicy(int done,int total,int expected)
    {
        var data=AcceleratorJourneySummaryTests.Data() with {
            Assessment=new(Guid.NewGuid(),AcceleratorAssessmentStatus.InProgress,Guid.NewGuid(),null,DateTime.UtcNow,total,done,true,true),
            TotalActivities=total,CompletedActivities=done,InterviewQuestionCount=total,InterviewEvaluatedQuestionCount=done,
            ActiveProjectProgressPercent=expected
        };
        foreach(var code in new[]{"Foundation","SkillBuild","InterviewReadiness","BuildAndShowcase"})
            Assert.Equal(expected,AcceleratorPhaseProgressPolicy.Apply(new(code,true,false),data).ProgressPercent);
        Assert.Equal(0,AcceleratorPhaseProgressPolicy.Apply(new("JobApplicationAndTracking",true,false),data).ProgressPercent);
        Assert.Equal(100,AcceleratorPhaseProgressPolicy.Apply(new("JobApplicationAndTracking",true,true),data).ProgressPercent);
        var scope=new AcceleratorJourneyWorkScope(new AcceleratorJourneyContext { CareerGoalId=Guid.NewGuid() },Guid.NewGuid(),[new(Guid.NewGuid(),3,1)],[],null);
        var result=AcceleratorJourneyPhasePolicy.Calculate(scope,data,new("Inactive",null,null,[]),70);
        Assert.Equal("journey-checklist-v1",result.PolicyVersion);
        Assert.Equal("journey-phase-progress-v1",result.PhaseProgressPolicyVersion);
        Assert.Equal(done>0 ? 25 : 0,result.OverallProgressPercent);
        Assert.Equal(0,AcceleratorPhaseProgressPolicy.Apply(new("Foundation",true,false),AcceleratorJourneySummaryTests.Data()).ProgressPercent);
    }

    [Theory]
    [InlineData(false,"SkillLearningPathNotConfigured")]
    [InlineData(true,"CompetencyMappingIncomplete")]
    public async Task MarketSkillMissingConfigurationIsControlled(bool wrongCompetency,string state)
    {
        using var f=new AcceleratorFixture(); var scope=await AcceleratorPhase3JourneyTests.Setup(f,0);
        var other=await f.Db.AcceleratorCompetencies.SingleAsync(x=>x.SkillId==f.ProductSkill.Id);
        var market=new MarketSkill { SkillId=f.CodeSkill.Id,CompetencyId=wrongCompetency ? other.Id : scope.CompetencyId,Slug="missing-config" };
        f.Db.Add(market); await f.Db.SaveChangesAsync();
        var service=AcceleratorJourneySummaryTests.Service(f);
        Assert.Equal(state,(await service.GetAsync(f.Candidate.Id,null,null,default,market.Id)).State);
        var role=await f.Db.AcceleratorCareerRoles.SingleAsync(x=>x.Code=="backend");
        Assert.Equal("MarketSkillNotMapped",(await service.GetAsync(f.Candidate.Id,null,null,default,role.Id)).State);
        Assert.Empty(await f.Db.AcceleratorPathEnrollments.ToArrayAsync());
    }

    internal static AcceleratorPhase2Service Phase2(AcceleratorFixture f,JobPortalDbContext db)
    {
        var store=new AcceleratorStore(db); var phase2=new AcceleratorPhase2Store(db); var journey=new AcceleratorJourneyStore(db); var learning=new AcceleratorLearningPathStore(db);
        var access=new AcceleratorService(store,Options.Create(f.Settings),f.Clock,f.Audit,f.Pricing);
        return new(store,phase2,access,f.Audit,f.Clock,new(store,journey,learning,phase2,access),journey,learning);
    }
    private static AcceleratorJourneySummaryService Summary(AcceleratorFixture f,JobPortalDbContext db)
    {
        var store=new AcceleratorStore(db); var phase2=new AcceleratorPhase2Store(db); var journey=new AcceleratorJourneyStore(db); var learning=new AcceleratorLearningPathStore(db);
        var access=new AcceleratorService(store,Options.Create(f.Settings),f.Clock,f.Audit,f.Pricing);
        var work=new AcceleratorJourneyWorkService(new(store,journey,learning,phase2,access),journey,store,learning);
        return new(new AcceleratorJourneySummaryStore(db),work,access,f.Clock,Options.Create(new AcceleratorJourneySummaryOptions()));
    }
}
