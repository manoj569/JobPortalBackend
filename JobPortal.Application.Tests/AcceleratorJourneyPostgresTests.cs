using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

// Run only against the dedicated Docker database prepared through the real migration chain.
public sealed class AcceleratorJourneyPostgresTests
{
    private static readonly Guid Owner = Guid.Parse("2a000000-0000-0000-0000-000000000001");
    private static readonly Guid Skill = Guid.Parse("2a000000-0000-0000-0000-000000000002");
    private static readonly Guid Taxonomy = Guid.Parse("2a000000-0000-0000-0000-000000000003");
    private static readonly Guid Role = Guid.Parse("2a000000-0000-0000-0000-000000000004");
    private static readonly Guid Competency = Guid.Parse("2a000000-0000-0000-0000-000000000005");
    private static readonly Guid LegacyGoal = Guid.Parse("2a000000-0000-0000-0000-000000000006");

    private static JobPortalDbContext Open()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ACCELERATOR_JOURNEY_TEST_POSTGRES"));
        Assert.Equal("127.0.0.1", connection.Host);
        Assert.Equal(55439, connection.Port);
        Assert.Equal("accelerator_2a_verify", connection.Database);
        return new(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(connection.ConnectionString,
            pg => { pg.MigrationsAssembly(typeof(PostgresMigrationMarker).Assembly.FullName); pg.EnableRetryOnFailure(3); }).Options);
    }

    private static AcceleratorJourneyContextService Contexts(JobPortalDbContext db)
    {
        var writes = new AcceleratorStore(db);
        var access = new AcceleratorService(writes, Options.Create(new AcceleratorOptions { Enabled = true }), TimeProvider.System,
            new AuditWriterTestDouble(), new AcceleratorTestPricing());
        return new(writes, new AcceleratorJourneyStore(db), new AcceleratorLearningPathStore(db), new AcceleratorPhase2Store(db), access);
    }

    [JourneyPostgresFact]
    public async Task HistoricalBackfillPreservesOwnershipAndMigrationHistory()
    {
        await using var db = Open();
        var context = await db.AcceleratorJourneyContexts.SingleAsync(x => x.CareerGoalId == LegacyGoal && !x.IsDeleted);
        Assert.Equal(Owner, context.UserId); Assert.Equal(Taxonomy, context.TaxonomyId);
        Assert.Equal(AcceleratorJourneyScopeType.CareerGoal, context.ScopeType); Assert.Null(context.LearningPathEnrollmentId);
        var attempt = await db.AcceleratorAssessmentAttempts.SingleAsync(x => x.Id == Guid.Parse("2a000000-0000-0000-0000-000000000008"));
        var evidence = await db.AcceleratorSkillEvidence.SingleAsync(x => x.Id == Guid.Parse("2a000000-0000-0000-0000-000000000009"));
        var roadmap = await db.AcceleratorRoadmaps.SingleAsync(x => x.Id == Guid.Parse("2a000000-0000-0000-0000-000000000010"));
        var recommendation = await db.AcceleratorRecommendations.SingleAsync(x => x.Id == Guid.Parse("2a000000-0000-0000-0000-000000000011"));
        Assert.All(new[] { attempt.JourneyContextId, evidence.JourneyContextId, roadmap.JourneyContextId, recommendation.JourneyContextId }, id => Assert.Equal(context.Id, id));
        Assert.All(new[] { attempt.GoalId, evidence.GoalId, roadmap.GoalId, recommendation.GoalId }, id => Assert.Equal(LegacyGoal, id));
        Assert.Equal("Historical verification roadmap", roadmap.Explanation);
        Assert.Equal("Historical verification recommendation", recommendation.Reason);
        Assert.Single(await db.Database.GetAppliedMigrationsAsync(), x => x == "20261117154417_AddCareerAcceleratorJourneyContexts");
    }

    [JourneyPostgresFact]
    public async Task ConcurrentCareerGoalCreationUsesOneDatabaseContext()
    {
        var goal = new AcceleratorCareerGoal { UserId = Owner, TaxonomyId = Taxonomy, RoleId = Role, RequestKey = Guid.NewGuid(),
            RequestHash = new string('A', 64), WeeklyStudyMinutes = 300, TargetAtUtc = DateTime.UtcNow.AddMonths(3) };
        await using (var setup = Open()) { setup.Add(goal); await setup.SaveChangesAsync(); }
        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = Open();
            return (await Contexts(db).GetOrCreateForCareerGoalAsync(Owner, goal.Id, default)).Id;
        }));
        Assert.Single(ids.Distinct());
        await using var verify = Open();
        Assert.Equal(1, await verify.AcceleratorJourneyContexts.CountAsync(x => x.CareerGoalId == goal.Id && !x.IsDeleted));
    }

    [JourneyPostgresFact]
    public async Task ConcurrentEnrollmentAndSkillAssessmentUseGenericOwnershipAndDatabaseConstraints()
    {
        var path = new AcceleratorLearningPath { Type = AcceleratorLearningPathType.Skill, CanonicalSkillId = Skill,
            Slug = "postgres-" + Guid.NewGuid().ToString("N"), Title = "Disposable skill path", Difficulty = 1, Status = AcceleratorLearningCatalogStatus.Published };
        var enrollment = new AcceleratorPathEnrollment { UserId = Owner, LearningPathId = path.Id, LearningPath = path,
            RequestKey = Guid.NewGuid(), RequestHash = new string('A',64), TargetLevel = 5, WeeklyCommitmentMinutes = 300,
            TargetCompletionAtUtc = DateTime.UtcNow.AddMonths(3), EnrolledAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow, Status = AcceleratorLearningEnrollmentStatus.Enrolled };
        var template = new AcceleratorAssessmentTemplate { TaxonomyId = Taxonomy, Version = Guid.NewGuid().ToString("N"),
            IsActive = true, TimeLimitMinutes = 30, MaximumQuestions = 1 };
        int goals; int roles;
        await using (var setup = Open())
        {
            goals = await setup.AcceleratorCareerGoals.CountAsync(); roles = await setup.AcceleratorCareerRoles.CountAsync();
            setup.AddRange(path, enrollment, template,
                new AcceleratorLearningPathCompetencyRequirement { LearningPathId = path.Id, CompetencyId = Competency, TargetProficiency = 5, Weight = 1, IsRequired = true },
                new AcceleratorLearningPathAssessment { LearningPathId = path.Id, AssessmentTemplateId = template.Id, Purpose = AcceleratorLearningPathAssessmentPurpose.Baseline },
                new AcceleratorQuestion { TaxonomyId = Taxonomy, CompetencyId = Competency, Version = Guid.NewGuid().ToString("N"), Type = AcceleratorQuestionType.MultipleChoice,
                    Difficulty = 1, Weight = 1, Prompt = "Choose A", OptionsJson = "[\"A\"]", AnswerKeyJson = "{\"answer\":\"A\"}", IsActive = true });
            await setup.SaveChangesAsync();
        }
        var ids = await Task.WhenAll(Enumerable.Range(0,8).Select(async _ =>
        {
            await using var db = Open();
            return (await Contexts(db).GetOrCreateForEnrollmentAsync(Owner, enrollment.Id, default)).Id;
        }));
        Assert.Single(ids.Distinct());
        await using (var db = Open())
        {
            var writes = new AcceleratorStore(db);
            var access = new AcceleratorService(writes, Options.Create(new AcceleratorOptions { Enabled = true }), TimeProvider.System, new AuditWriterTestDouble(), new AcceleratorTestPricing());
            var engine = new AcceleratorPhase2Service(writes, new AcceleratorPhase2Store(db), access, new AuditWriterTestDouble(), TimeProvider.System,
                Contexts(db), new AcceleratorJourneyStore(db), new AcceleratorLearningPathStore(db));
            var attempt = await engine.StartEnrollmentAssessmentAsync(Owner, enrollment.Id, AcceleratorLearningPathAssessmentPurpose.Baseline, Guid.NewGuid(), default);
            var answer = await engine.SubmitAnswerAsync(Owner, attempt.Id, new(attempt.Questions[0].Id,"{\"answer\":\"A\"}"), default);
            Assert.Equal(100m,(await engine.FinalizeAttemptAsync(Owner, attempt.Id,new(answer.Revision), default)).Score);
            await engine.GenerateEnrollmentRoadmapAsync(Owner, enrollment.Id, Guid.NewGuid(), default);
            Assert.Contains(await engine.EnrollmentRecommendationsAsync(Owner, enrollment.Id, default), x => x.Type == "SkillGap");
            Assert.Equal(goals, await db.AcceleratorCareerGoals.CountAsync()); Assert.Equal(roles, await db.AcceleratorCareerRoles.CountAsync());
            Assert.Equal(1, await db.AcceleratorJourneyContexts.CountAsync(x => x.LearningPathEnrollmentId == enrollment.Id && !x.IsDeleted));
            Assert.Null((await db.AcceleratorAssessmentAttempts.SingleAsync(x => x.Id == attempt.Id)).GoalId);
            Assert.All(await db.AcceleratorSkillEvidence.Where(x => x.JourneyContextId == ids[0]).ToArrayAsync(), x => Assert.Null(x.GoalId));
            Assert.All(await db.AcceleratorRoadmaps.Where(x => x.JourneyContextId == ids[0]).ToArrayAsync(), x => Assert.Null(x.GoalId));
            Assert.All(await db.AcceleratorRecommendations.Where(x => x.JourneyContextId == ids[0]).ToArrayAsync(), x => Assert.Null(x.GoalId));
        }
        foreach (var (scope, goal, source) in new[] {
            (AcceleratorJourneyScopeType.CareerGoal,(Guid?)null,(Guid?)null),
            (AcceleratorJourneyScopeType.CareerGoal,(Guid?)LegacyGoal,(Guid?)enrollment.Id),
            (AcceleratorJourneyScopeType.LearningPathEnrollment,(Guid?)null,(Guid?)null),
            (AcceleratorJourneyScopeType.LearningPathEnrollment,(Guid?)LegacyGoal,(Guid?)enrollment.Id) })
            await Rejected(scope,goal,source,"23514");
        await Rejected(AcceleratorJourneyScopeType.CareerGoal,LegacyGoal,null,"23505");
        await Rejected(AcceleratorJourneyScopeType.LearningPathEnrollment,null,enrollment.Id,"23505");
    }

    private static async Task Rejected(AcceleratorJourneyScopeType scope, Guid? goal, Guid? enrollment, string sqlState)
    {
        await using var db = Open();
        db.Add(new AcceleratorJourneyContext { UserId = Owner, TaxonomyId = Taxonomy, ScopeType = scope, CareerGoalId = goal, LearningPathEnrollmentId = enrollment });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(sqlState, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    private sealed class JourneyPostgresFactAttribute : FactAttribute
    {
        public JourneyPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCELERATOR_JOURNEY_TEST_POSTGRES")))
                Skip = "Requires an explicitly prepared disposable Docker PostgreSQL database for Migration 2A.";
        }
    }
}
