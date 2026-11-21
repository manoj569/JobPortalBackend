using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorPhase3JourneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SharedEngineSupportsLegacyCareerAndSkillEnrollments(int scope)
    {
        using var f = new AcceleratorFixture();
        var setup = await Setup(f, scope);
        var service = Create(f);
        var request = Conversation(setup);
        var conversation = await service.StartConversationAsync(f.Candidate.Id, request, default);
        Assert.Equal(conversation.Id, (await service.StartConversationAsync(f.Candidate.Id, request, default)).Id);
        Assert.Equal(setup.GoalId == Guid.Empty ? null : setup.GoalId, conversation.GoalId);
        Assert.NotNull(conversation.JourneyContextId);
        var row = await f.Db.AcceleratorTutorConversations.SingleAsync();
        var context = await Builder(f).BuildAsync(f.Candidate.Id, row, new string('"', 8000), default);
        using var json = JsonDocument.Parse(context.Json);
        Assert.Equal(conversation.JourneyContextId, json.RootElement.GetProperty("journeyContextId").GetGuid());
        Assert.True(context.Json.Length <= 24000);
        if (scope != 0)
        {
            Assert.Equal(setup.ModuleId, json.RootElement.GetProperty("module").GetProperty("Id").GetGuid());
            Assert.Equal(setup.LessonId, json.RootElement.GetProperty("lesson").GetProperty("Id").GetGuid());
            Assert.Empty(await f.Db.AcceleratorCareerGoals.ToArrayAsync());
            if (scope == 2) Assert.Equal(f.CodeSkill.Id, json.RootElement.GetProperty("path").GetProperty("CanonicalSkillId").GetGuid());
        }
        var start = Practice(setup);
        var session = await service.StartPracticeAsync(f.Candidate.Id, start, default);
        Assert.Equal(session.Id, (await service.StartPracticeAsync(f.Candidate.Id, start, default)).Id);
        Assert.Equal(conversation.JourneyContextId, session.JourneyContextId);
        Assert.Equal(conversation.GoalId, session.GoalId);
        var publicJson = JsonSerializer.Serialize(session);
        Assert.DoesNotContain("AnswerKey", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Rubric", publicJson, StringComparison.OrdinalIgnoreCase);
        var answer = new SubmitPracticeAnswerRequest(Guid.NewGuid(), Assert.Single(session.Questions).Id, "{\"answer\":\"A\"}");
        var result = await service.SubmitPracticeAnswerAsync(f.Candidate.Id, session.Id, answer, default);
        Assert.Equal(1m, result.Score);
        Assert.Equal(result.Id, (await service.SubmitPracticeAnswerAsync(f.Candidate.Id, session.Id, answer, default)).Id);
        Assert.Single(await f.Db.AcceleratorPracticeEvaluations.ToArrayAsync());
        Assert.Equal(100m, (await service.PracticeResultsAsync(f.Candidate.Id, session.Id, default)).ScorePercent);
        Assert.Equal(session.Id, Assert.Single(await service.PracticeHistoryAsync(f.Candidate.Id, 1, 20, default)).Id);
        Assert.Equal(AcceleratorPracticeDifficulty.Beginner, Assert.Single(await service.PracticeRecommendationsAsync(f.Candidate.Id, default)).RecommendedDifficulty);
        var evidence = Assert.Single(await f.Db.AcceleratorSkillEvidence.ToArrayAsync());
        Assert.Equal(session.JourneyContextId, evidence.JourneyContextId);
        Assert.Equal(.25m, evidence.Confidence);
        Assert.Equal(AcceleratorProficiency.Developing, evidence.Proficiency);
        var journey = await f.Db.AcceleratorJourneyContexts.SingleAsync(x => x.Id == session.JourneyContextId);
        Assert.Equal(100m, (await new AcceleratorRecognitionStore(f.Db).SignalsAsync(journey, default)).PracticeScore);
        var archived = await service.ArchiveConversationAsync(f.Candidate.Id, conversation.Id, conversation.Revision, default);
        Assert.Equal(AcceleratorTutorConversationStatus.Archived, archived.Status);
    }

    [Fact]
    public async Task OwnershipAndAttributionRejectForeignJourneysAndContent()
    {
        using var f = new AcceleratorFixture(); var setup = await Setup(f, 2); var service = Create(f);
        var conversation = await service.StartConversationAsync(f.Candidate.Id, Conversation(setup), default);
        var session = await service.StartPracticeAsync(f.Candidate.Id, Practice(setup), default);
        await Assert.ThrowsAsync<NotFoundException>(() => service.StartConversationAsync(f.Other.Id, Conversation(setup), default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.StartPracticeAsync(f.Other.Id, Practice(setup), default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.MessagesAsync(f.Other.Id, conversation.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ArchiveConversationAsync(f.Other.Id, conversation.Id, conversation.Revision, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.PracticeSessionAsync(f.Other.Id, session.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.PracticeResultsAsync(f.Other.Id, session.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.SubmitPracticeAnswerAsync(f.Other.Id, session.Id, new(Guid.NewGuid(), session.Questions[0].Id, "{}"), default));
        Assert.Empty(await service.ConversationsAsync(f.Other.Id, 1, 20, default));
        Assert.Empty(await service.PracticeHistoryAsync(f.Other.Id, 1, 20, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.StartConversationAsync(f.Candidate.Id, Conversation(setup) with { ModuleId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<BadRequestException>(() => service.StartPracticeAsync(f.Candidate.Id, Practice(setup) with { CompetencyId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<BadRequestException>(() => service.StartPracticeAsync(f.Candidate.Id, Practice(setup) with { GoalId = Guid.NewGuid() }, default));
        Assert.Empty(await f.Db.AcceleratorUsageReservations.ToArrayAsync());
    }

    [Fact]
    public async Task ReplayKeysCannotBeMovedToAnotherSessionOrPayload()
    {
        using var f = new AcceleratorFixture(); var setup = await Setup(f, 2); var service = Create(f);
        var start = Practice(setup); var first = await service.StartPracticeAsync(f.Candidate.Id, start, default);
        var second = await service.StartPracticeAsync(f.Candidate.Id, Practice(setup), default);
        await Assert.ThrowsAsync<ConflictException>(() => service.StartPracticeAsync(f.Candidate.Id, start with { Difficulty = AcceleratorPracticeDifficulty.Advanced }, default));
        var answer = new SubmitPracticeAnswerRequest(Guid.NewGuid(), first.Questions[0].Id, "{\"answer\":\"A\"}");
        await service.SubmitPracticeAnswerAsync(f.Candidate.Id, first.Id, answer, default);
        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitPracticeAnswerAsync(f.Candidate.Id, second.Id, answer, default));
        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitPracticeAnswerAsync(f.Candidate.Id, first.Id, answer with { AnswerJson = "{}" }, default));
        Assert.Single(await f.Db.AcceleratorPracticeEvaluations.ToArrayAsync());
    }

    [Fact]
    public async Task UnavailableTutorDoesNotReserveQuotaAndForeignPracticeDoesNotContribute()
    {
        using var f = new AcceleratorFixture(); var setup = await Setup(f, 2); var service = Create(f);
        var conversation = await service.StartConversationAsync(f.Candidate.Id, Conversation(setup), default);
        await Assert.ThrowsAsync<TutorProviderUnavailableException>(() => service.SendMessageAsync(f.Candidate.Id, conversation.Id, new(Guid.NewGuid(), AcceleratorTutorMode.Explain, "Explain this concept"), default));
        Assert.Empty(await f.Db.AcceleratorUsageReservations.ToArrayAsync());
        Assert.Empty(await f.Db.AcceleratorAiDispatchRecords.ToArrayAsync());
        var context = await f.Db.AcceleratorJourneyContexts.SingleAsync();
        Assert.Null((await new AcceleratorRecognitionStore(f.Db).SignalsAsync(context, default)).PracticeScore);
        var foreign = new AcceleratorJourneyContext { UserId = f.Other.Id, ScopeType = AcceleratorJourneyScopeType.LearningPathEnrollment, LearningPathEnrollmentId = Guid.NewGuid(), TaxonomyId = context.TaxonomyId };
        f.Db.Add(foreign); await f.Db.SaveChangesAsync();
        var session = await service.StartPracticeAsync(f.Candidate.Id, Practice(setup), default);
        await service.SubmitPracticeAnswerAsync(f.Candidate.Id, session.Id, new(Guid.NewGuid(), session.Questions[0].Id, "{\"answer\":\"A\"}"), default);
        Assert.Null((await new AcceleratorRecognitionStore(f.Db).SignalsAsync(foreign, default)).PracticeScore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnrollmentDispatchReservesBeforeCallingProviderAndReplaysWithoutAnotherCharge(bool uncertain)
    {
        using var f = new AcceleratorFixture(); var setup = await Setup(f, 2); await f.Activate();
        f.Settings.Policies[AcceleratorFeature.Tutor].MaxInputTokens = 6000;
        f.Settings.Policies[AcceleratorFeature.Tutor].InputInrPerMillionTokens = 1;
        var provider = new TestTutorProvider(f, uncertain);
        var service = Create(f, [provider]);
        var conversation = await service.StartConversationAsync(f.Candidate.Id, Conversation(setup), default);
        var request = new SendTutorMessageRequest(Guid.NewGuid(), AcceleratorTutorMode.Explain, "Explain the current lesson.");
        if (uncertain)
        {
            await Assert.ThrowsAsync<TimeoutException>(() => service.SendMessageAsync(f.Candidate.Id, conversation.Id, request, default));
            Assert.Equal(AcceleratorAiDispatchStatus.Uncertain, (await f.Db.AcceleratorAiDispatchRecords.SingleAsync()).Status);
            Assert.Equal(AcceleratorUsageState.Dispatched, (await f.Db.AcceleratorUsageReservations.SingleAsync()).State);
            await Assert.ThrowsAsync<ConflictException>(() => service.SendMessageAsync(f.Candidate.Id, conversation.Id, request, default));
            Assert.Empty(await f.Db.AcceleratorTutorMessages.ToArrayAsync());
        }
        else
        {
            var result = await service.SendMessageAsync(f.Candidate.Id, conversation.Id, request, default);
            var events = await f.Db.AcceleratorUsageEvents.CountAsync();
            Assert.Equal(result.Id, (await service.SendMessageAsync(f.Candidate.Id, conversation.Id, request, default)).Id);
            Assert.Equal(events, await f.Db.AcceleratorUsageEvents.CountAsync());
            Assert.Single(await f.Db.AcceleratorTutorMessages.ToArrayAsync());
            Assert.Equal(AcceleratorAiDispatchStatus.Succeeded, (await f.Db.AcceleratorAiDispatchRecords.SingleAsync()).Status);
            var another = await service.StartConversationAsync(f.Candidate.Id, Conversation(setup), default);
            await Assert.ThrowsAsync<ConflictException>(() => service.SendMessageAsync(f.Candidate.Id, another.Id, request, default));
        }
        Assert.Equal(1, provider.Calls);
        Assert.Single(await f.Db.AcceleratorUsageReservations.ToArrayAsync());
    }

    private sealed class TestTutorProvider(AcceleratorFixture fixture, bool uncertain) : IAiTutorProvider
    {
        public string Name => fixture.Provider.Name;
        public bool IsAvailable => true;
        public int Calls { get; private set; }
        public async Task<TutorProviderResult> SendAsync(TutorProviderRequest request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(AcceleratorUsageState.Dispatched, (await fixture.Db.AcceleratorUsageReservations.SingleAsync(x => x.Id == request.ReservationId, ct)).State);
            Assert.Equal(AcceleratorAiDispatchStatus.Dispatched, (await fixture.Db.AcceleratorAiDispatchRecords.SingleAsync(x => x.Id == request.DispatchId, ct)).Status);
            if (uncertain) throw new TimeoutException("Simulated unknown provider outcome.");
            return new("Explicit test provider response", "test-provider-id", "test-model", 20, 10, .01m);
        }
    }

    internal sealed record JourneySetup(Guid GoalId, Guid? EnrollmentId, Guid CompetencyId, Guid? ModuleId, Guid? LessonId);
    internal static async Task<JourneySetup> Setup(AcceleratorFixture f, int scope)
    {
        var taxonomy = await f.Service.PublishTaxonomyAsync(f.Admin.Id, f.Taxonomy(), default);
        var competency = await f.Db.AcceleratorCompetencies.SingleAsync(x => x.SkillId == f.CodeSkill.Id);
        var question = new AcceleratorQuestion { TaxonomyId = taxonomy.Id, CompetencyId = competency.Id, Version = "journey-v1", Type = AcceleratorQuestionType.MultipleChoice,
            Difficulty = 1, Weight = 1, Prompt = "Choose the correct option", OptionsJson = "[\"A\",\"B\"]", AnswerKeyJson = "{\"answer\":\"A\"}", RubricJson = "{}", Explanation = "Validated fixture", IsActive = true };
        f.Db.Add(question); await f.Db.SaveChangesAsync();
        var role = taxonomy.Roles.Single(x => x.Code == "backend");
        if (scope == 0) return new((await f.Service.CreateGoalAsync(f.Candidate.Id, f.Goal(role.Id), default)).Id, null, competency.Id, null, null);
        var path = new AcceleratorLearningPath { Type = scope == 1 ? AcceleratorLearningPathType.Career : AcceleratorLearningPathType.Skill,
            Slug = "phase3-journey", Title = "Configured learning path", CanonicalCareerRoleId = scope == 1 ? role.Id : null, CanonicalSkillId = scope == 2 ? f.CodeSkill.Id : null,
            Difficulty = 1, Status = AcceleratorLearningCatalogStatus.Published };
        var module = new AcceleratorLearningModule { LearningPath = path, LearningPathId = path.Id, Title = "Configured module", Sequence = 1, Difficulty = 1, Status = AcceleratorLearningCatalogStatus.Published };
        var lesson = new AcceleratorLesson { Module = module, ModuleId = module.Id, Title = "Configured lesson", Content = "Candidate learning content", Sequence = 1, Status = AcceleratorLearningCatalogStatus.Published };
        var enrollment = new AcceleratorPathEnrollment { UserId = f.Candidate.Id, LearningPath = path, LearningPathId = path.Id, Status = AcceleratorLearningEnrollmentStatus.InProgress, WeeklyCommitmentMinutes = 300, TargetLevel = 3 };
        f.Db.AddRange(path, module, lesson, enrollment);
        if (scope == 2) f.Db.Add(new AcceleratorLearningPathCompetencyRequirement { LearningPathId = path.Id, CompetencyId = competency.Id, TargetProficiency = 3, Weight = 1, IsRequired = true });
        await f.Db.SaveChangesAsync();
        return new(Guid.Empty, enrollment.Id, competency.Id, module.Id, lesson.Id);
    }
    internal static AcceleratorPhase3Service Create(AcceleratorFixture f, IEnumerable<IAiTutorProvider>? providers = null)
    {
        var learning = new AcceleratorLearningPathStore(f.Db); var journeys = new AcceleratorJourneyStore(f.Db);
        var contexts = new AcceleratorJourneyContextService(f.Store, journeys, learning, f.Phase2Store, f.Service);
        return new(f.Store, new AcceleratorPhase3Store(f.Db), f.Phase2Store, f.Service, Builder(f), providers ?? [new UnavailableAiTutorProvider()], f.Quota, f.Audit, f.Clock, Options.Create(f.Settings), contexts, learning);
    }
    private static TutorContextBuilder Builder(AcceleratorFixture f)
    {
        var learning = new AcceleratorLearningPathStore(f.Db); var journeys = new AcceleratorJourneyStore(f.Db);
        return new(f.Store, new AcceleratorPhase3Store(f.Db), new AcceleratorJourneyContextService(f.Store, journeys, learning, f.Phase2Store, f.Service), journeys, learning);
    }
    internal static StartTutorConversationRequest Conversation(JourneySetup s) => new(Guid.NewGuid(), s.GoalId, s.CompetencyId, null, "Learning", "Help with learning", s.EnrollmentId, s.ModuleId, s.LessonId);
    internal static StartPracticeSessionRequest Practice(JourneySetup s) => new(Guid.NewGuid(), s.GoalId, s.CompetencyId, null, AcceleratorPracticeDifficulty.Beginner, AcceleratorPracticeQuestionType.MultipleChoice, s.EnrollmentId, s.ModuleId, s.LessonId);
}
