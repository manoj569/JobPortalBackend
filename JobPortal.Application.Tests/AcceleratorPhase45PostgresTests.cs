using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorPhase45PostgresTests
{
    private static readonly Guid Owner=Guid.Parse("2a000000-0000-0000-0000-000000000001");
    private static readonly Guid Role=Guid.Parse("2a000000-0000-0000-0000-000000000004");
    private static readonly Guid Competency=Guid.Parse("2a000000-0000-0000-0000-000000000005");
    internal static JobPortalDbContext Open()
    {
        var cs=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ACCELERATOR_PHASE45_TEST_POSTGRES"));
        Assert.Equal("127.0.0.1",cs.Host); Assert.Equal(55439,cs.Port); Assert.Equal("accelerator_phase45_verify",cs.Database);
        return new(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(cs.ConnectionString,p=>{p.MigrationsAssembly(typeof(PostgresMigrationMarker).Assembly.FullName);p.EnableRetryOnFailure(3);}).Options);
    }
    internal static (AcceleratorPhase4Service Projects,AcceleratorPhase5Service Interviews,AcceleratorJourneySummaryService Summary) Services(JobPortalDbContext db)
    {
        var writes=new AcceleratorStore(db); var journeys=new AcceleratorJourneyStore(db); var learning=new AcceleratorLearningPathStore(db); var phase2store=new AcceleratorPhase2Store(db);
        var access=new AcceleratorService(writes,Options.Create(new AcceleratorOptions { Enabled=true }),TimeProvider.System,new AuditWriterTestDouble(),new AcceleratorTestPricing());
        var contexts=new AcceleratorJourneyContextService(writes,journeys,learning,phase2store,access);
        var work=new AcceleratorJourneyWorkService(contexts,journeys,writes,learning);
        var phase2=new AcceleratorPhase2Service(writes,phase2store,access,new AuditWriterTestDouble(),TimeProvider.System,contexts,journeys,learning);
        return (new(writes,phase2,new AcceleratorPhase4Store(db),access,new ProjectDeterministicEvaluator(),new AuditWriterTestDouble(),TimeProvider.System,work),
            new(writes,phase2store,new AcceleratorPhase5Store(db),new AcceleratorPhase3Store(db),new AcceleratorPhase4Store(db),access,new AuditWriterTestDouble(),TimeProvider.System,work,journeys),
            new(new AcceleratorJourneySummaryStore(db),work,access,TimeProvider.System,Options.Create(new AcceleratorJourneySummaryOptions())));
    }
    [Phase45PostgresFact]
    public async Task LegacyRootsAndReadinessBackfillPreserveGoalAndHistoricalValues()
    {
        await using var db=Open(); var goal=Guid.Parse("2a000000-0000-0000-0000-000000000006");
        var project=await db.AcceleratorCandidateProjects.SingleAsync(x=>x.Id==Guid.Parse("45000000-0000-0000-0000-000000000003"));
        var session=await db.AcceleratorInterviewSessions.SingleAsync(x=>x.Id==Guid.Parse("45000000-0000-0000-0000-000000000006"));
        var readiness=await db.AcceleratorReadinessSnapshots.SingleAsync(x=>x.Id==Guid.Parse("45000000-0000-0000-0000-000000000007"));
        Assert.All(new[]{project.GoalId,session.GoalId,readiness.GoalId},x=>Assert.Equal(goal,x));
        Assert.All(new[]{project.JourneyContextId,session.JourneyContextId,readiness.JourneyContextId},x=>Assert.Equal(goal,x));
        Assert.Equal(56m,readiness.Total); Assert.Equal("Historical readiness preserved",readiness.Explanation); Assert.Equal(80m,session.OverallScore);
    }
    [Phase45PostgresFact]
    public async Task ConcurrentEnrollmentLifecyclesPersistOneStartAnswerEvaluationAndReadiness()
    {
        Guid enrollmentId; Guid projectVersionId; Guid assignmentId; Guid interviewVersionId;
        await using (var setup=Open())
        {
            var path=new AcceleratorLearningPath { Type=AcceleratorLearningPathType.Skill,Slug="phase45-"+Guid.NewGuid().ToString("N"),Title="Disposable skill path",CanonicalSkillId=Guid.Parse("2a000000-0000-0000-0000-000000000002"),Difficulty=1,Status=AcceleratorLearningCatalogStatus.Published };
            var enrollment=new AcceleratorPathEnrollment { UserId=Owner,LearningPath=path,LearningPathId=path.Id,RequestKey=Guid.NewGuid(),RequestHash=new string('A',64),TargetLevel=3,WeeklyCommitmentMinutes=300,Status=AcceleratorLearningEnrollmentStatus.InProgress,EnrolledAtUtc=DateTime.UtcNow,LastActivityAtUtc=DateTime.UtcNow };
            setup.AddRange(path,enrollment,new AcceleratorLearningPathCompetencyRequirement { LearningPathId=path.Id,CompetencyId=Competency,TargetProficiency=3,Weight=1,IsRequired=true });
            await setup.SaveChangesAsync(); enrollmentId=enrollment.Id;
            var (project,assignment)=await AcceleratorPhase45JourneyTests.SeedProjectCatalog(setup,Role,Competency);
            projectVersionId=project.Id; assignmentId=assignment.Id;
            interviewVersionId=(await AcceleratorPhase45JourneyTests.SeedInterviewCatalog(setup,Role,Competency)).Id;
        }
        var start=new StartAcceleratorProjectRequest(Guid.NewGuid(),Guid.Empty,projectVersionId,enrollmentId);
        var projects=await Task.WhenAll(Enumerable.Range(0,4).Select(async _=>{await using var db=Open();return await Services(db).Projects.StartProjectAsync(Owner,start,default);}));
        Assert.Single(projects.Select(x=>x.Id).Distinct()); var projectId=projects[0].Id;
        await using (var db=Open())
        {
            var service=Services(db).Projects;
            var submission=await service.SubmitAsync(Owner,projectId,new(Guid.NewGuid(),assignmentId,"A",projects[0].Revision),default);
            Assert.Equal(100m,(await service.EvaluateDeterministicAsync(Owner,projectId,submission.Id,default)).Score);
        }
        var interviewStart=new StartInterviewRequest(Guid.NewGuid(),Guid.Empty,interviewVersionId,false,enrollmentId);
        var sessions=await Task.WhenAll(Enumerable.Range(0,4).Select(async _=>{await using var db=Open();return await Services(db).Interviews.StartAsync(Owner,interviewStart,default);}));
        Assert.Single(sessions.Select(x=>x.Id).Distinct()); var session=sessions[0];
        var submit=new SubmitInterviewAnswerRequest(Guid.NewGuid(),session.Questions[0].Id,"A",session.Revision);
        var answers=await Task.WhenAll(Enumerable.Range(0,4).Select(async _=>{await using var db=Open();return await Services(db).Interviews.SubmitAnswerAsync(Owner,session.Id,submit,default);}));
        Assert.Single(answers.Select(x=>x.Id).Distinct());
        Guid revision;
        await using (var db=Open())
        {
            var service=Services(db).Interviews;
            Assert.Equal(100m,(await service.EvaluateDeterministicAsync(Owner,session.Id,answers[0].Id,default)).Score);
            revision=(await service.SessionAsync(Owner,session.Id,default)).Revision;
        }
        await Task.WhenAll(Enumerable.Range(0,4).Select(async _=>{await using var db=Open();return await Services(db).Interviews.CompleteAsync(Owner,session.Id,new(revision),default);}));
        await using var verify=Open();
        var savedProject=await verify.AcceleratorCandidateProjects.SingleAsync(x=>x.Id==projectId);
        var savedSession=await verify.AcceleratorInterviewSessions.SingleAsync(x=>x.Id==session.Id);
        Assert.Null(savedProject.GoalId); Assert.Null(savedSession.GoalId); Assert.NotNull(savedProject.JourneyContextId); Assert.Equal(savedProject.JourneyContextId,savedSession.JourneyContextId);
        Assert.Single(await verify.AcceleratorReadinessSnapshots.Where(x=>x.TriggerInterviewSessionId==session.Id).ToArrayAsync());
        var summary=await Services(verify).Summary.GetAsync(Owner,null,savedProject.JourneyContextId,default);
        Assert.Equal(1,summary.Summary!.CompletedProjects); Assert.Equal(1,summary.Summary.CompletedInterviews);
        Assert.Null(await new AcceleratorPhase4Store(verify).CandidateProjectAsync(Guid.NewGuid(),projectId,default));
        Assert.Null(await new AcceleratorPhase5Store(verify).SessionAsync(Guid.NewGuid(),session.Id,default));
        var context=await verify.AcceleratorJourneyContexts.SingleAsync(x=>x.Id==savedProject.JourneyContextId);
        var signals=await new AcceleratorRecognitionStore(verify).SignalsAsync(context,default,.5m);
        Assert.Equal(100m,signals.ProjectScore); Assert.Equal(100m,signals.InterviewScore);
    }
    private sealed class Phase45PostgresFactAttribute : FactAttribute
    {
        public Phase45PostgresFactAttribute()
        { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCELERATOR_PHASE45_TEST_POSTGRES"))) Skip="Requires the explicitly disposable Phase 4/5 PostgreSQL database."; }
    }
}
