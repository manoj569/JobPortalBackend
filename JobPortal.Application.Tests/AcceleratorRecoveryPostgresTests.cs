using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorRecoveryPostgresTests
{
    private static readonly Guid Owner=Guid.Parse("2a000000-0000-0000-0000-000000000001");
    private static readonly Guid Competency=Guid.Parse("2a000000-0000-0000-0000-000000000005");
    private static AcceleratorPhase2Service Phase2(JobPortalDbContext db)
    {
        var writes=new AcceleratorStore(db); var journeys=new AcceleratorJourneyStore(db); var learning=new AcceleratorLearningPathStore(db); var store=new AcceleratorPhase2Store(db);
        var audit=new AuditWriterTestDouble();
        var access=new AcceleratorService(writes,Options.Create(new AcceleratorOptions { Enabled=true }),TimeProvider.System,audit,new AcceleratorTestPricing());
        return new(writes,store,access,audit,TimeProvider.System,new(writes,journeys,learning,store,access),journeys,learning);
    }

    [RecoveryPostgresFact]
    public async Task ConcurrentStartsReadsSubmitsAndFinalizeRecoverCommittedAssessmentState()
    {
        Guid enrollmentId;
        await using(var db=AcceleratorPhase45PostgresTests.Open())
        {
            var competency=await db.AcceleratorCompetencies.SingleAsync(x=>x.Id==Competency);
            var path=new AcceleratorLearningPath { Type=AcceleratorLearningPathType.Skill,Slug="recovery-"+Guid.NewGuid().ToString("N"),Title="Disposable recovery test",CanonicalSkillId=competency.SkillId,Difficulty=1,Status=AcceleratorLearningCatalogStatus.Published };
            var enrollment=new AcceleratorPathEnrollment { UserId=Owner,LearningPath=path,LearningPathId=path.Id,RequestKey=Guid.NewGuid(),RequestHash=new string('A',64),TargetLevel=3,WeeklyCommitmentMinutes=300,Status=AcceleratorLearningEnrollmentStatus.InProgress,EnrolledAtUtc=DateTime.UtcNow,LastActivityAtUtc=DateTime.UtcNow };
            var template=new AcceleratorAssessmentTemplate { TaxonomyId=competency.TaxonomyId,Version=Guid.NewGuid().ToString("N"),IsActive=true,TimeLimitMinutes=30,MaximumQuestions=1 };
            db.AddRange(path,enrollment,template,new AcceleratorLearningPathCompetencyRequirement { LearningPathId=path.Id,CompetencyId=Competency,TargetProficiency=3,Weight=1,IsRequired=true },
                new AcceleratorLearningPathAssessment { LearningPathId=path.Id,AssessmentTemplateId=template.Id,Purpose=AcceleratorLearningPathAssessmentPurpose.Baseline },
                new AcceleratorQuestion { TaxonomyId=competency.TaxonomyId,CompetencyId=Competency,Version=Guid.NewGuid().ToString("N"),Type=AcceleratorQuestionType.MultipleChoice,Difficulty=1,Weight=1,Prompt="Disposable question",OptionsJson="[\"A\",\"B\"]",AnswerKeyJson="{\"answer\":\"A\"}",IsActive=true });
            await db.SaveChangesAsync(); enrollmentId=enrollment.Id;
        }
        var key=Guid.NewGuid();
        var starts=await Task.WhenAll(Enumerable.Range(0,3).Select(async _=>{
            await using var db=AcceleratorPhase45PostgresTests.Open(); return await Phase2(db).StartEnrollmentAssessmentAsync(Owner,enrollmentId,AcceleratorLearningPathAssessmentPurpose.Baseline,key,default);
        }));
        Assert.Single(starts.Select(x=>x.Id).Distinct()); var attempt=starts[0];
        var read=Read(attempt.Id);
        AssessmentAttemptResponse submitted;
        await using(var db=AcceleratorPhase45PostgresTests.Open()) submitted=await Phase2(db).SubmitAnswerAsync(Owner,attempt.Id,new(attempt.Questions[0].Id,"{\"answer\":\"A\"}"),default);
        var snapshot=await read;
        Assert.Equal(snapshot.Questions[0].IsAnswered,snapshot.Questions[0].SubmittedAnswerJson is not null);
        var recovered=await Read(attempt.Id); Assert.Equal(submitted.Revision,recovered.Revision); Assert.True(recovered.Questions[0].IsAnswered);
        var finalize=Finalize(attempt.Id,recovered.Revision);
        var duplicate=SubmitDuplicate(attempt.Id,attempt.Questions[0].Id);
        var completed=await finalize; await duplicate;
        Assert.Equal(AcceleratorAssessmentStatus.Completed,completed.Status);
        Assert.False((await Read(attempt.Id)).CanResume);
        await using var verify=AcceleratorPhase45PostgresTests.Open();
        Assert.Single(await verify.AcceleratorSkillEvidence.Where(x=>x.AttemptId==attempt.Id).ToArrayAsync());
        Assert.Null(await Phase2(verify).CurrentAttemptAsync(Owner,attempt.JourneyContextId!.Value,default));
    }
    private static async Task<AssessmentAttemptResponse> Read(Guid id)
    { await using var db=AcceleratorPhase45PostgresTests.Open(); return await Phase2(db).AttemptAsync(Owner,id,default); }
    private static async Task<AssessmentAttemptResponse> Finalize(Guid id,Guid revision)
    { await using var db=AcceleratorPhase45PostgresTests.Open(); return await Phase2(db).FinalizeAttemptAsync(Owner,id,new(revision),default); }
    private static async Task SubmitDuplicate(Guid id,Guid questionId)
    {
        await using var db=AcceleratorPhase45PostgresTests.Open();
        try { await Phase2(db).SubmitAnswerAsync(Owner,id,new(questionId,"{\"answer\":\"A\"}"),default); }
        catch(ConflictException) { Assert.Equal(AcceleratorAssessmentStatus.Completed,(await Read(id)).Status); }
    }

    [RecoveryPostgresFact]
    public async Task ConcurrentInterviewDetailAndDuplicateEvaluationExposeConsistentRecoveryIds()
    {
        Guid version;
        await using(var db=AcceleratorPhase45PostgresTests.Open()) version=(await AcceleratorPhase45JourneyTests.SeedInterviewCatalog(db,Guid.Parse("2a000000-0000-0000-0000-000000000004"),Competency)).Id;
        InterviewSessionResponse session; InterviewAnswerResponse answer;
        await using(var db=AcceleratorPhase45PostgresTests.Open())
        {
            var service=AcceleratorPhase45PostgresTests.Services(db).Interviews;
            session=await service.StartAsync(Owner,new(Guid.NewGuid(),Guid.Parse("2a000000-0000-0000-0000-000000000006"),version,false),default);
            answer=await service.SubmitAnswerAsync(Owner,session.Id,new(Guid.NewGuid(),session.Questions[0].Id,"A",session.Revision),default);
        }
        var detail=Detail(session.Id);
        var evaluations=await Task.WhenAll(Enumerable.Range(0,3).Select(async _=>{
            await using var db=AcceleratorPhase45PostgresTests.Open(); return await AcceleratorPhase45PostgresTests.Services(db).Interviews.EvaluateDeterministicAsync(Owner,session.Id,answer.Id,default);
        }));
        Assert.Single(evaluations.Select(x=>x.Id).Distinct());
        var snapshot=Assert.Single((await detail).Questions);
        Assert.Equal(answer.Id,snapshot.AnswerId); Assert.Equal(snapshot.Evaluated,snapshot.EvaluationId.HasValue);
        Assert.Equal(evaluations[0].Id,Assert.Single((await Detail(session.Id)).Questions).EvaluationId);
    }
    private static async Task<InterviewSessionResponse> Detail(Guid id)
    { await using var db=AcceleratorPhase45PostgresTests.Open(); return await AcceleratorPhase45PostgresTests.Services(db).Interviews.SessionAsync(Owner,id,default); }
    private sealed class RecoveryPostgresFactAttribute : FactAttribute
    {
        public RecoveryPostgresFactAttribute()
        { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCELERATOR_PHASE45_TEST_POSTGRES"))) Skip="Requires the explicitly disposable Phase 4/5 PostgreSQL database."; }
    }
}
