using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Repositories;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorPhase45JourneyTests
{
    internal static AcceleratorPhase4Service Projects(AcceleratorFixture f) => new(f.Store,f.Phase2,new AcceleratorPhase4Store(f.Db),f.Service,new ProjectDeterministicEvaluator(),f.Audit,f.Clock,f.Work);
    internal static AcceleratorPhase5Service Interviews(AcceleratorFixture f) => new(f.Store,f.Phase2Store,new AcceleratorPhase5Store(f.Db),new AcceleratorPhase3Store(f.Db),new AcceleratorPhase4Store(f.Db),f.Service,f.Audit,f.Clock,f.Work,new AcceleratorJourneyStore(f.Db));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProjectLifecycleUsesOwnedJourneyAndEvaluatedEvidence(int kind)
    {
        using var f=new AcceleratorFixture(); var scope=await AcceleratorPhase3JourneyTests.Setup(f,kind);
        var (version,assignment)=await SeedProject(f,scope.CompetencyId);
        var service=Projects(f);
        Assert.Equal(version.Id,Assert.Single(await service.RecommendAsync(f.Candidate.Id,scope.GoalId,default,scope.EnrollmentId)).ProjectVersionId);
        var request=new StartAcceleratorProjectRequest(Guid.NewGuid(),scope.GoalId,version.Id,scope.EnrollmentId);
        var project=await service.StartProjectAsync(f.Candidate.Id,request,default);
        Assert.NotNull(project.JourneyContextId); Assert.Equal(kind==0 ? scope.GoalId : (Guid?)null,project.GoalId);
        Assert.Equal(project.Id,(await service.StartProjectAsync(f.Candidate.Id,request,default)).Id);
        Assert.Equal(0,(await service.ProgressAsync(f.Candidate.Id,project.Id,default)).ProgressPercent);
        Assert.Empty(await f.Db.AcceleratorSkillEvidence.ToArrayAsync());
        await Assert.ThrowsAsync<NotFoundException>(()=>service.ProjectAsync(f.Other.Id,project.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.ProgressAsync(f.Other.Id,project.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.CandidateMilestoneAsync(f.Other.Id,project.Id,assignment.MilestoneId,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.CandidateAssignmentAsync(f.Other.Id,project.Id,assignment.Id,default));
        var submit=new SubmitAcceleratorAssignmentRequest(Guid.NewGuid(),assignment.Id,"A",project.Revision);
        var submission=await service.SubmitAsync(f.Candidate.Id,project.Id,submit,default);
        Assert.Equal(submission.Id,(await service.SubmitAsync(f.Candidate.Id,project.Id,submit,default)).Id);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.SubmissionAsync(f.Other.Id,project.Id,submission.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.EvaluationAsync(f.Other.Id,project.Id,submission.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.EvaluateDeterministicAsync(f.Other.Id,project.Id,submission.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.HistoryAsync(f.Other.Id,project.Id,1,20,default));
        var evaluation=await service.EvaluateDeterministicAsync(f.Candidate.Id,project.Id,submission.Id,default);
        Assert.Equal(100m,evaluation.Score);
        Assert.Equal(evaluation.Id,(await service.EvaluateDeterministicAsync(f.Candidate.Id,project.Id,submission.Id,default)).Id);
        var progress=await service.ProgressAsync(f.Candidate.Id,project.Id,default);
        Assert.Equal(AcceleratorCandidateProjectStatus.Completed,progress.Status); Assert.Equal(100,progress.ProgressPercent);
        var evidence=Assert.Single(await f.Db.AcceleratorSkillEvidence.ToArrayAsync());
        Assert.Equal(project.JourneyContextId,evidence.JourneyContextId); Assert.Equal(project.Id,evidence.CandidateProjectId);
        var context=await f.Db.AcceleratorJourneyContexts.SingleAsync(x=>x.Id==project.JourneyContextId);
        Assert.Equal(100m,(await new AcceleratorRecognitionStore(f.Db).SignalsAsync(context,default,.5m)).ProjectScore);
        Assert.Single(await service.HistoryAsync(f.Candidate.Id,project.Id,1,20,default));
        if (kind!=0) Assert.Empty(await f.Db.AcceleratorCareerGoals.ToArrayAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InterviewLifecycleUsesOwnedJourneyAndProducesReadiness(int kind)
    {
        using var f=new AcceleratorFixture(); var scope=await AcceleratorPhase3JourneyTests.Setup(f,kind);
        var version=await SeedInterview(f,scope.CompetencyId); var service=Interviews(f);
        Assert.Equal(version.Id,Assert.Single(await service.RecommendationsAsync(f.Candidate.Id,scope.GoalId,default,scope.EnrollmentId)).VersionId);
        var request=new StartInterviewRequest(Guid.NewGuid(),scope.GoalId,version.Id,false,scope.EnrollmentId);
        var session=await service.StartAsync(f.Candidate.Id,request,default);
        Assert.NotNull(session.JourneyContextId); Assert.Equal(kind==0 ? scope.GoalId : (Guid?)null,session.GoalId);
        Assert.Equal(session.Id,(await service.StartAsync(f.Candidate.Id,request,default)).Id);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.SessionAsync(f.Other.Id,session.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.NextQuestionAsync(f.Other.Id,session.Id,default));
        var next=await service.NextQuestionAsync(f.Candidate.Id,session.Id,default); Assert.NotNull(next);
        Assert.DoesNotContain("DeterministicRulesJson",JsonSerializer.Serialize(session),StringComparison.Ordinal);
        var submit=new SubmitInterviewAnswerRequest(Guid.NewGuid(),next.Id,"A",session.Revision);
        var answer=await service.SubmitAnswerAsync(f.Candidate.Id,session.Id,submit,default);
        Assert.Equal(answer.Id,(await service.SubmitAnswerAsync(f.Candidate.Id,session.Id,submit,default)).Id);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.EvaluationAsync(f.Other.Id,session.Id,answer.Id,default));
        var evaluation=await service.EvaluateDeterministicAsync(f.Candidate.Id,session.Id,answer.Id,default);
        Assert.Equal(100m,evaluation.Score);
        var current=await service.SessionAsync(f.Candidate.Id,session.Id,default);
        var report=await service.CompleteAsync(f.Candidate.Id,session.Id,new(current.Revision),default);
        Assert.Equal(100m,report.OverallScore);
        Assert.Equal(report.SessionId,(await service.CompleteAsync(f.Candidate.Id,session.Id,new(Guid.NewGuid()),default)).SessionId);
        Assert.Equal(report.OverallScore,(await service.ReportAsync(f.Candidate.Id,session.Id,default)).OverallScore);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.ReportAsync(f.Other.Id,session.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.CompleteAsync(f.Other.Id,session.Id,new(current.Revision),default));
        if (scope.EnrollmentId.HasValue) await Assert.ThrowsAsync<NotFoundException>(()=>service.EnrollmentHistoryAsync(f.Other.Id,scope.EnrollmentId.Value,1,20,default));
        var evidence=Assert.Single(await f.Db.AcceleratorSkillEvidence.ToArrayAsync()); Assert.Equal(session.JourneyContextId,evidence.JourneyContextId);
        Assert.Equal(session.JourneyContextId,(await f.Db.AcceleratorReadinessSnapshots.SingleAsync()).JourneyContextId);
        var context=await f.Db.AcceleratorJourneyContexts.SingleAsync(x=>x.Id==session.JourneyContextId);
        Assert.Equal(100m,(await new AcceleratorRecognitionStore(f.Db).SignalsAsync(context,default,.5m)).InterviewScore);
        if (kind!=0) Assert.Empty(await f.Db.AcceleratorCareerGoals.ToArrayAsync());
    }

    [Fact]
    public async Task ForeignEnrollmentAndCrossJourneyKeysAreRejected()
    {
        using var f=new AcceleratorFixture(); var scope=await AcceleratorPhase3JourneyTests.Setup(f,2);
        var (version,_)=await SeedProject(f,scope.CompetencyId); var interview=await SeedInterview(f,scope.CompetencyId);
        var projectRequest=new StartAcceleratorProjectRequest(Guid.NewGuid(),Guid.Empty,version.Id,scope.EnrollmentId);
        await Projects(f).StartProjectAsync(f.Candidate.Id,projectRequest,default);
        await Assert.ThrowsAsync<NotFoundException>(()=>Projects(f).StartProjectAsync(f.Other.Id,projectRequest,default));
        var role=await f.Db.AcceleratorCareerRoles.SingleAsync(x=>x.Code=="backend");
        var goal=await f.Service.CreateGoalAsync(f.Candidate.Id,f.Goal(role.Id),default);
        await Assert.ThrowsAsync<ConflictException>(()=>Projects(f).StartProjectAsync(f.Candidate.Id,projectRequest with { GoalId=goal.Id,EnrollmentId=null },default));
        var request=new StartInterviewRequest(Guid.NewGuid(),Guid.Empty,interview.Id,false,scope.EnrollmentId);
        await Interviews(f).StartAsync(f.Candidate.Id,request,default);
        await Assert.ThrowsAsync<NotFoundException>(()=>Interviews(f).StartAsync(f.Other.Id,request,default));
        await Assert.ThrowsAsync<ConflictException>(()=>Interviews(f).StartAsync(f.Candidate.Id,request with { GoalId=goal.Id,EnrollmentId=null },default));
        var unrelated=await f.Db.AcceleratorCompetencies.SingleAsync(x=>x.SkillId==f.ProductSkill.Id);
        var (wrongProject,_)=await SeedProject(f,unrelated.Id);
        var wrongInterview=await SeedInterview(f,unrelated.Id);
        await Assert.ThrowsAsync<ConflictException>(()=>Projects(f).StartProjectAsync(f.Candidate.Id,projectRequest with { RequestKey=Guid.NewGuid(),ProjectVersionId=wrongProject.Id },default));
        await Assert.ThrowsAsync<ConflictException>(()=>Interviews(f).StartAsync(f.Candidate.Id,request with { RequestKey=Guid.NewGuid(),VersionId=wrongInterview.Id },default));
        Assert.Empty(await f.Db.AcceleratorUsageReservations.ToArrayAsync());
    }

    [Fact]
    public async Task EnrollmentAiEvaluationFailsClosedWithoutQuotaOrFabricatedEvidence()
    {
        using var f=new AcceleratorFixture(); var scope=await AcceleratorPhase3JourneyTests.Setup(f,2);
        var (version,assignment)=await SeedProject(f,scope.CompetencyId,true); var projects=Projects(f);
        var project=await projects.StartProjectAsync(f.Candidate.Id,new(Guid.NewGuid(),Guid.Empty,version.Id,scope.EnrollmentId),default);
        var submission=await projects.SubmitAsync(f.Candidate.Id,project.Id,new(Guid.NewGuid(),assignment.Id,"Candidate answer",project.Revision),default);
        var projectStore=new AcceleratorPhase4Store(f.Db); var dispatches=new AcceleratorPhase3Store(f.Db);
        var ai=new AcceleratorProjectAiService(f.Store,projectStore,dispatches,projects,f.Quota,f.Service,Options.Create(f.Settings),[],[],f.Audit,f.Clock);
        await Assert.ThrowsAsync<AcceleratorProviderUnavailableException>(()=>ai.EvaluateAsync(f.Candidate.Id,project.Id,submission.Id,default));
        var interview=await SeedInterview(f,scope.CompetencyId,true); var interviews=Interviews(f);
        var session=await interviews.StartAsync(f.Candidate.Id,new(Guid.NewGuid(),Guid.Empty,interview.Id,false,scope.EnrollmentId),default);
        var answer=await interviews.SubmitAnswerAsync(f.Candidate.Id,session.Id,new(Guid.NewGuid(),session.Questions[0].Id,"Candidate answer",session.Revision),default);
        var interviewStore=new AcceleratorPhase5Store(f.Db);
        var interviewAi=new AcceleratorInterviewAiService(f.Store,dispatches,interviewStore,interviewStore,interviews,f.Quota,f.Service,Options.Create(f.Settings),[],[],f.Audit,f.Clock);
        await Assert.ThrowsAsync<AcceleratorProviderUnavailableException>(()=>interviewAi.EvaluateAsync(f.Candidate.Id,session.Id,answer.Id,default));
        Assert.Empty(await f.Db.AcceleratorUsageReservations.ToArrayAsync()); Assert.Empty(await f.Db.AcceleratorAiDispatchRecords.ToArrayAsync());
        Assert.Empty(await f.Db.AcceleratorSkillEvidence.ToArrayAsync());
    }

    [Fact]
    public async Task ConfiguredProjectInterviewWeightsUseRealScoresAndRequireCompletedEvidence()
    {
        using var f=new AcceleratorFixture(); var scope=await AcceleratorPhase3JourneyTests.Setup(f,2);
        var (version,assignment)=await SeedProject(f,scope.CompetencyId); var projects=Projects(f);
        var project=await projects.StartProjectAsync(f.Candidate.Id,new(Guid.NewGuid(),Guid.Empty,version.Id,scope.EnrollmentId),default);
        var submission=await projects.SubmitAsync(f.Candidate.Id,project.Id,new(Guid.NewGuid(),assignment.Id,"A",project.Revision),default);
        await projects.EvaluateDeterministicAsync(f.Candidate.Id,project.Id,submission.Id,default);
        var journeys=new AcceleratorJourneyStore(f.Db); var learning=new AcceleratorLearningPathStore(f.Db);
        var contexts=new AcceleratorJourneyContextService(f.Store,journeys,learning,f.Phase2Store,f.Service);
        var recognition=new AcceleratorRecognitionService(f.Store,new AcceleratorRecognitionStore(f.Db),contexts,journeys,learning,f.Service,f.Audit,f.Clock);
        await recognition.CreatePolicyAsync(f.Admin.Id,AcceleratorRecognitionTests.Policy() with { Components=[new(AcceleratorProficiencyComponent.Project,75,true),new(AcceleratorProficiencyComponent.Interview,25,true)] },default);
        Assert.Null((await recognition.ProficiencyAsync(f.Candidate.Id,project.JourneyContextId!.Value,default)).Score);
        var interview=await SeedInterview(f,scope.CompetencyId); var interviews=Interviews(f);
        var session=await interviews.StartAsync(f.Candidate.Id,new(Guid.NewGuid(),Guid.Empty,interview.Id,false,scope.EnrollmentId),default);
        var answer=await interviews.SubmitAnswerAsync(f.Candidate.Id,session.Id,new(Guid.NewGuid(),session.Questions[0].Id,"B",session.Revision),default);
        Assert.Equal(0m,(await interviews.EvaluateDeterministicAsync(f.Candidate.Id,session.Id,answer.Id,default)).Score);
        var current=await interviews.SessionAsync(f.Candidate.Id,session.Id,default);
        await interviews.CompleteAsync(f.Candidate.Id,session.Id,new(current.Revision),default);
        var proficiency=await recognition.ProficiencyAsync(f.Candidate.Id,project.JourneyContextId.Value,default);
        Assert.Equal(75m,proficiency.Score);
        Assert.Contains(proficiency.Components,x=>x.Component==AcceleratorProficiencyComponent.Project && x.Score==100m);
        Assert.Contains(proficiency.Components,x=>x.Component==AcceleratorProficiencyComponent.Interview && x.Score==0m);
    }

    internal static async Task<(AcceleratorProjectVersion,AcceleratorProjectAssignment)> SeedProject(AcceleratorFixture f,Guid competencyId,bool ai=false)
    {
        var role=await f.Db.AcceleratorCareerRoles.SingleAsync(x=>x.Code=="backend");
        return await SeedProjectCatalog(f.Db,role.Id,competencyId,ai);
    }
    internal static async Task<(AcceleratorProjectVersion,AcceleratorProjectAssignment)> SeedProjectCatalog(JobPortalDbContext db,Guid roleId,Guid competencyId,bool ai=false)
    {
        var definition=new AcceleratorProjectDefinition { StableKey="journey-project-"+Guid.NewGuid().ToString("N"),IsActive=true };
        var version=new AcceleratorProjectVersion { Definition=definition,DefinitionId=definition.Id,Version=1,TargetRoleId=roleId,Type=AcceleratorProjectType.BackendApi,Difficulty=AcceleratorPracticeDifficulty.Beginner,
            EstimatedMinutes=60,Title="Journey project",ShortDescription="Configured project",BusinessContext="Learning context",Instructions="Answer A" };
        var milestone=new AcceleratorProjectMilestone { ProjectVersion=version,ProjectVersionId=version.Id,Sequence=1,Title="Milestone",IsRequired=true,EstimatedMinutes=60 };
        var rubric=new AcceleratorProjectRubric { ProjectVersionId=version.Id,Version=1,Name="Configured rubric",IsActive=true };
        var criterion=new AcceleratorProjectRubricCriterion { RubricId=rubric.Id,CompetencyId=competencyId,Sequence=1,Name="Correctness",Weight=100,MaximumScore=10 };
        rubric.Criteria.Add(criterion); version.Rubrics.Add(rubric); version.Milestones.Add(milestone);
        version.Competencies.Add(new AcceleratorProjectCompetency { ProjectVersionId=version.Id,CompetencyId=competencyId,RequiredProficiency=0 });
        var assignment=new AcceleratorProjectAssignment { Milestone=milestone,MilestoneId=milestone.Id,Sequence=1,Type=AcceleratorAssignmentType.Structured,EvaluationMode=ai ? AcceleratorProjectEvaluationMode.Ai : AcceleratorProjectEvaluationMode.Deterministic,
            Prompt="Choose A",MaximumAttempts=3,RetryAllowed=true,DeterministicRulesJson=JsonSerializer.Serialize(new[]{new ProjectRule("ExactChoice",criterion.Id,"A")}) };
        milestone.Assignments.Add(assignment); db.Add(version); await db.SaveChangesAsync();
        version.CatalogStatus=AcceleratorProjectCatalogStatus.Published; version.IsActive=true; await db.SaveChangesAsync();
        return (version,assignment);
    }
    internal static async Task<AcceleratorInterviewVersion> SeedInterview(AcceleratorFixture f,Guid competencyId,bool ai=false)
    {
        var role=await f.Db.AcceleratorCareerRoles.SingleAsync(x=>x.Code=="backend");
        return await SeedInterviewCatalog(f.Db,role.Id,competencyId,ai);
    }
    internal static async Task<AcceleratorInterviewVersion> SeedInterviewCatalog(JobPortalDbContext db,Guid roleId,Guid competencyId,bool ai=false)
    {
        var definition=new AcceleratorInterviewDefinition { StableKey="journey-interview-"+Guid.NewGuid().ToString("N"),Name="Journey interview",TargetRoleId=roleId };
        var version=new AcceleratorInterviewVersion { Definition=definition,DefinitionId=definition.Id,Version=1,Type=AcceleratorInterviewType.Technical,Difficulty=AcceleratorPracticeDifficulty.Beginner,EstimatedMinutes=20,TargetExperienceLevel="Entry",Instructions="Answer A" };
        var section=new AcceleratorInterviewSection { Version=version,InterviewVersionId=version.Id,Sequence=1,Title="Technical",Type=AcceleratorInterviewType.Technical,EstimatedMinutes=20,IsRequired=true,QuestionCount=1 };
        var criterion=new AcceleratorInterviewRubricCriterion { InterviewVersionId=version.Id,CompetencyId=competencyId,Sequence=1,Name="Correctness",Weight=100,MaximumScore=10 };
        section.Questions.Add(new AcceleratorInterviewQuestion { Section=section,SectionId=section.Id,Sequence=1,CompetencyId=competencyId,Kind=AcceleratorInterviewQuestionKind.Static,Type=AcceleratorInterviewQuestionType.TechnicalConcept,
            EvaluationMode=ai ? AcceleratorInterviewEvaluationMode.Ai : AcceleratorInterviewEvaluationMode.Deterministic,PublicPrompt="Choose A",DeterministicRulesJson=JsonSerializer.Serialize(new[]{new ProjectRule("ExactChoice",criterion.Id,"A")}) });
        version.Sections.Add(section); version.Criteria.Add(criterion); version.Competencies.Add(new AcceleratorInterviewCompetency { InterviewVersionId=version.Id,CompetencyId=competencyId,ExpectedProficiency=2,Weight=100,IsRequired=true,QuestionCoverageTarget=1 });
        db.Add(version); await db.SaveChangesAsync(); version.Status=AcceleratorInterviewCatalogStatus.Published; await db.SaveChangesAsync(); return version;
    }
}
