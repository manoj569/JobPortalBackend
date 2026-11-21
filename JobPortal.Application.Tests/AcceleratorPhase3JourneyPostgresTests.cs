using JobPortal.Application.Common.Exceptions;
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

public sealed class AcceleratorPhase3JourneyPostgresTests
{
    private static readonly string[] AllowedDatabases = ["accelerator_phase3_verify", "accelerator_phase45_verify"];
    private static readonly Guid Owner = Guid.Parse("2a000000-0000-0000-0000-000000000001");
    private static readonly Guid Goal = Guid.Parse("2a000000-0000-0000-0000-000000000006");
    private static readonly Guid Competency = Guid.Parse("2a000000-0000-0000-0000-000000000005");
    private static JobPortalDbContext Open()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ACCELERATOR_PHASE3_JOURNEY_TEST_POSTGRES"));
        Assert.Equal("127.0.0.1", connection.Host); Assert.Equal(55439, connection.Port); Assert.Contains(connection.Database, AllowedDatabases);
        return new(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(connection.ConnectionString, pg =>
        { pg.MigrationsAssembly(typeof(PostgresMigrationMarker).Assembly.FullName); pg.EnableRetryOnFailure(3); }).Options);
    }
    private static AcceleratorPhase3Service Service(JobPortalDbContext db)
    {
        var writes = new AcceleratorStore(db); var journeys = new AcceleratorJourneyStore(db); var learning = new AcceleratorLearningPathStore(db);
        var phase2 = new AcceleratorPhase2Store(db); var phase3 = new AcceleratorPhase3Store(db);
        var settings = Options.Create(new AcceleratorOptions { Enabled = true });
        var access = new AcceleratorService(writes, settings, TimeProvider.System, new AuditWriterTestDouble(), new AcceleratorTestPricing());
        var contexts = new AcceleratorJourneyContextService(writes, journeys, learning, phase2, access);
        var quota = new AcceleratorQuotaService(writes, access, settings, TimeProvider.System, new AuditWriterTestDouble(), [new UnavailableAcceleratorAIProvider()]);
        return new(writes, phase3, phase2, access, new TutorContextBuilder(writes, phase3, contexts, journeys, learning), [new UnavailableAiTutorProvider()],
            quota, new AuditWriterTestDouble(), TimeProvider.System, settings, contexts, learning);
    }
    [Phase3PostgresFact]
    public async Task LegacyBackfillPreservesGoalsQuestionsAndEvaluations()
    {
        await using var db = Open();
        var conversation = await db.AcceleratorTutorConversations.SingleAsync(x => x.Id == Guid.Parse("3a000000-0000-0000-0000-000000000001"));
        var session = await db.AcceleratorPracticeSessions.SingleAsync(x => x.Id == Guid.Parse("3a000000-0000-0000-0000-000000000003"));
        Assert.Equal(Goal, conversation.GoalId); Assert.Equal(Goal, session.GoalId);
        Assert.Equal(Goal, conversation.JourneyContextId); Assert.Equal(Goal, session.JourneyContextId);
        Assert.Equal("Historical conversation", conversation.Title);
        var result = await Service(db).PracticeResultsAsync(Owner, session.Id, default);
        Assert.Equal(100m, result.ScorePercent); Assert.Equal("Historical evaluation", Assert.Single(result.Evaluations).Explanation);
        Assert.Equal(1, await db.AcceleratorPracticeQuestions.CountAsync(x => x.SessionId == session.Id));
    }
    [Phase3PostgresFact]
    public async Task GenericSessionsPersistAndConcurrentReplayCreatesOneEvaluation()
    {
        Guid enrollmentId; Guid moduleId; Guid lessonId;
        await using (var db = Open())
        {
            var path = new AcceleratorLearningPath { Type = AcceleratorLearningPathType.Skill, Slug = "phase3-pg-" + Guid.NewGuid().ToString("N"), Title = "Disposable skill path",
                CanonicalSkillId = Guid.Parse("2a000000-0000-0000-0000-000000000002"), Difficulty = 1, Status = AcceleratorLearningCatalogStatus.Published };
            var module = new AcceleratorLearningModule { LearningPath = path, LearningPathId = path.Id, Title = "Disposable module", Sequence = 1, Difficulty = 1, Status = AcceleratorLearningCatalogStatus.Published };
            var lesson = new AcceleratorLesson { Module = module, ModuleId = module.Id, Title = "Disposable lesson", Sequence = 1, Status = AcceleratorLearningCatalogStatus.Published };
            var enrollment = new AcceleratorPathEnrollment { UserId = Owner, LearningPath = path, LearningPathId = path.Id, Status = AcceleratorLearningEnrollmentStatus.InProgress,
                RequestKey = Guid.NewGuid(), RequestHash = new string('A', 64), TargetLevel = 3, WeeklyCommitmentMinutes = 300, EnrolledAtUtc = DateTime.UtcNow, LastActivityAtUtc = DateTime.UtcNow };
            db.AddRange(path, module, lesson, enrollment, new AcceleratorLearningPathCompetencyRequirement { LearningPathId = path.Id, CompetencyId = Competency, TargetProficiency = 3, Weight = 1, IsRequired = true },
                new AcceleratorQuestion { TaxonomyId = Guid.Parse("2a000000-0000-0000-0000-000000000003"), CompetencyId = Competency, Version = Guid.NewGuid().ToString("N"), Type = AcceleratorQuestionType.MultipleChoice,
                    Difficulty = 1, Weight = 1, Prompt = "Disposable question", OptionsJson = "[\"A\",\"B\"]", AnswerKeyJson = "{\"answer\":\"A\"}", RubricJson = "{}", Explanation = "Fixture", IsActive = true });
            await db.SaveChangesAsync(); enrollmentId = enrollment.Id; moduleId = module.Id; lessonId = lesson.Id;
        }
        var start = new StartPracticeSessionRequest(Guid.NewGuid(), Guid.Empty, Competency, null, AcceleratorPracticeDifficulty.Beginner, AcceleratorPracticeQuestionType.MultipleChoice, enrollmentId, moduleId, lessonId);
        var conversations = new StartTutorConversationRequest(Guid.NewGuid(), Guid.Empty, Competency, null, "Learning", "Disposable conversation", enrollmentId, moduleId, lessonId);
        var sessions = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = Open(); return await Service(db).StartPracticeAsync(Owner, start, default);
        }));
        Assert.Single(sessions.Select(x => x.Id).Distinct());
        var session = sessions[0]; Assert.Null(session.GoalId); Assert.NotNull(session.JourneyContextId);
        var ids = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = Open(); return (await Service(db).StartConversationAsync(Owner, conversations, default)).Id;
        }));
        Assert.Single(ids.Distinct());
        var answer = new SubmitPracticeAnswerRequest(Guid.NewGuid(), session.Questions[0].Id, "{\"answer\":\"A\"}");
        var evaluations = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = Open(); return await Service(db).SubmitPracticeAnswerAsync(Owner, session.Id, answer, default);
        }));
        Assert.Single(evaluations.Select(x => x.Id).Distinct());
        await using var verify = Open();
        Assert.Single(await verify.AcceleratorPracticeAttempts.Where(x => x.SessionId == session.Id).ToArrayAsync());
        var context = await verify.AcceleratorJourneyContexts.SingleAsync(x => x.Id == session.JourneyContextId);
        Assert.Equal(100m, (await new AcceleratorRecognitionStore(verify).SignalsAsync(context, default)).PracticeScore);
        var conversation = await Service(verify).ConversationAsync(Owner, ids[0], default);
        Assert.Null(conversation.GoalId); Assert.Equal(context.Id, conversation.JourneyContextId);
        var reservations = await verify.AcceleratorUsageReservations.CountAsync();
        await Assert.ThrowsAsync<TutorProviderUnavailableException>(() => Service(verify).SendMessageAsync(Owner, conversation.Id, new(Guid.NewGuid(), AcceleratorTutorMode.Explain, "Explain this lesson"), default));
        Assert.Equal(reservations, await verify.AcceleratorUsageReservations.CountAsync());
        var other = Guid.NewGuid();
        Assert.Null(await new AcceleratorPhase3Store(verify).PracticeSessionAsync(other, session.Id, default));
    }
    private sealed class Phase3PostgresFactAttribute : FactAttribute
    {
        public Phase3PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCELERATOR_PHASE3_JOURNEY_TEST_POSTGRES"))) Skip = "Requires the disposable Docker Phase 3 JourneyContext database.";
        }
    }
}
