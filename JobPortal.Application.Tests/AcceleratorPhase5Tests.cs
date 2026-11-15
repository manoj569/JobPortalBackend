using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorPhase5Tests
{
    [Fact]
    public void ReadinessWeightsAreVersionedDeterministicAndBounded()
    {
        Assert.Equal(60, CareerReadinessPolicy.Calculate(80, 60, 50, 40));
        Assert.Equal(60, CareerReadinessPolicy.Calculate(80, 60, 50, 40));
        Assert.Equal(100, CareerReadinessPolicy.Calculate(100, 100, 100, 100));
        Assert.Throws<BadRequestException>(() => CareerReadinessPolicy.Calculate(101, 0, 0, 0));
    }

    [Theory]
    [InlineData("A", 100)]
    [InlineData("B", 0)]
    public void ObjectiveInterviewScoringUsesPublishedRules(string answer, decimal expected)
    {
        var criterion = new AcceleratorInterviewRubricCriterion { Sequence = 1, Weight = 100, MaximumScore = 10 };
        var question = new AcceleratorInterviewQuestion { EvaluationMode = AcceleratorInterviewEvaluationMode.Deterministic,
            DeterministicRulesJson = JsonSerializer.Serialize(new[] { new ProjectRule("ExactChoice", criterion.Id, "A") }) };
        InterviewScoringPolicy.ValidateConfiguration(question, [criterion]);
        var result = InterviewScoringPolicy.Score(question, [criterion], answer);
        Assert.Equal(expected, result.Total);
        Assert.Equal(expected / 10, Assert.Single(result.Observations).Score);
    }

    [Fact]
    public void SubjectiveInterviewCannotMasqueradeAsObjective()
    {
        var criterion = new AcceleratorInterviewRubricCriterion { Sequence = 1, Weight = 100, MaximumScore = 10 };
        var question = new AcceleratorInterviewQuestion { EvaluationMode = AcceleratorInterviewEvaluationMode.Ai,
            DeterministicRulesJson = JsonSerializer.Serialize(new[] { new ProjectRule("ExactChoice", criterion.Id, "A") }) };
        Assert.Throws<BadRequestException>(() => InterviewScoringPolicy.ValidateConfiguration(question, [criterion]));
        Assert.Throws<BadRequestException>(() => InterviewScoringPolicy.Score(question, [criterion], "A"));
    }

    [Fact]
    public void CandidateDtosDoNotExposeEvaluatorSecrets()
    {
        var detail = new InterviewVersionResponse(Guid.NewGuid(), "Backend", "Description",
            AcceleratorInterviewType.Technical, AcceleratorPracticeDifficulty.Beginner, 30,
            [new InterviewSectionResponse(Guid.NewGuid(), 1, "Technical", AcceleratorInterviewType.Technical,
                true, [new InterviewQuestionResponse(Guid.NewGuid(), Guid.NewGuid(), null, 1,
                    AcceleratorInterviewQuestionType.TechnicalConcept, "Public prompt", false, false)])]);
        var json = JsonSerializer.Serialize(detail);
        Assert.Contains("Public prompt", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedConceptsJson", json, StringComparison.Ordinal);
        Assert.DoesNotContain("HiddenEvaluatorGuidance", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DeterministicRulesJson", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CandidateOwnedSessionAndAnswerQueriesAreIsolated()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var session = new AcceleratorInterviewSession { UserId = owner, RequestKey = Guid.NewGuid() };
        var question = new AcceleratorSessionQuestion { SessionId = session.Id, SourceQuestionId = Guid.NewGuid(),
            SectionId = Guid.NewGuid(), Sequence = 1, PublicPrompt = "Private session prompt" };
        session.Questions.Add(question);
        var answer = new AcceleratorInterviewAnswer { SessionId = session.Id, SessionQuestionId = question.Id,
            RequestKey = Guid.NewGuid(), Content = "Private answer" };
        db.AddRange(session, answer); await db.SaveChangesAsync();
        var store = new AcceleratorPhase5Store(db);
        Assert.NotNull(await store.SessionAsync(owner, session.Id, default));
        Assert.Null(await store.SessionAsync(other, session.Id, default));
        Assert.Null(await store.AnswerByKeyAsync(other, session.Id, answer.RequestKey, default));
        Assert.Empty(await store.AnswersAsync(other, session.Id, default));
    }

    [Fact]
    public async Task InterviewAnswersEvaluationsAndReadinessSnapshotsAreAppendOnly()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var answer = new AcceleratorInterviewAnswer { SessionId = Guid.NewGuid(), SessionQuestionId = Guid.NewGuid(),
            RequestKey = Guid.NewGuid(), Content = "Original" };
        db.Add(answer); await db.SaveChangesAsync();
        answer.Content = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var evaluation = new AcceleratorInterviewEvaluation { AnswerId = answer.Id, Score = 80,
            Confidence = .6m, Feedback = "Original" };
        evaluation.CriterionScores.Add(new AcceleratorInterviewCriterionScore { EvaluationId = evaluation.Id,
            CriterionId = Guid.NewGuid(), Score = 8 });
        db.Add(evaluation); await db.SaveChangesAsync();
        evaluation.Score = 90;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var snapshot = new AcceleratorReadinessSnapshot { UserId = Guid.NewGuid(), GoalId = Guid.NewGuid(),
            Total = 50, CalculatedAtUtc = DateTime.UtcNow };
        db.Add(snapshot); await db.SaveChangesAsync();
        snapshot.Total = 100;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var completed = new AcceleratorInterviewSession { UserId = Guid.NewGuid(),
            RequestKey = Guid.NewGuid(), Status = AcceleratorInterviewSessionStatus.Completed,
            OverallScore = 80 };
        db.Add(completed); await db.SaveChangesAsync();
        completed.OverallScore = 90;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task PublishedInterviewQuestionsCannotBeEdited()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var definition = new AcceleratorInterviewDefinition { StableKey = "backend", Name = "Backend" };
        var version = new AcceleratorInterviewVersion { Definition = definition, DefinitionId = definition.Id,
            Version = 1, Status = AcceleratorInterviewCatalogStatus.Draft };
        var section = new AcceleratorInterviewSection { Version = version, InterviewVersionId = version.Id,
            Sequence = 1, Title = "Technical", QuestionCount = 1 };
        var question = new AcceleratorInterviewQuestion { Section = section, SectionId = section.Id,
            Sequence = 1, PublicPrompt = "Original" };
        version.Sections.Add(section); section.Questions.Add(question);
        db.Add(version); await db.SaveChangesAsync();
        question.PublicPrompt = "Draft edit"; await db.SaveChangesAsync();
        version.Status = AcceleratorInterviewCatalogStatus.Published; await db.SaveChangesAsync();
        question.PublicPrompt = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CandidateReferencedInterviewVersionCannotBeEditedEvenIfDraft()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var version = new AcceleratorInterviewVersion { Version = 1,
            Status = AcceleratorInterviewCatalogStatus.Draft };
        var section = new AcceleratorInterviewSection { InterviewVersionId = version.Id,
            Version = version, Sequence = 1, Title = "Draft", QuestionCount = 1 };
        version.Sections.Add(section);
        db.Add(version); await db.SaveChangesAsync();
        var session = new AcceleratorInterviewSession { UserId = Guid.NewGuid(),
            InterviewVersionId = version.Id, RequestKey = Guid.NewGuid() };
        db.Add(session); await db.SaveChangesAsync();
        section.Title = "Changed after candidate use";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task PublicPassportRequiresEnabledOpaqueToken()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var token = new string('A', 64);
        var share = new AcceleratorPassportShare { UserId = Guid.NewGuid(), GoalId = Guid.NewGuid(),
            PublicToken = token, IsEnabled = false };
        db.Add(share); await db.SaveChangesAsync();
        var store = new AcceleratorPhase5Store(db);
        Assert.Null(await store.PublicShareAsync(token, default));
        share.IsEnabled = true; await db.SaveChangesAsync();
        Assert.NotNull(await store.PublicShareAsync(token, default));
        share.IsEnabled = false; await db.SaveChangesAsync();
        Assert.Null(await store.PublicShareAsync(token, default));
    }

    [Fact]
    public async Task InterviewLifecycleIsOwnedIdempotentAndCreatesConservativeEvidence()
    {
        using var fixture = new AcceleratorFixture();
        var taxonomy = await fixture.Service.PublishTaxonomyAsync(fixture.Admin.Id, fixture.Taxonomy(), default);
        var goal = await fixture.Service.CreateGoalAsync(fixture.Candidate.Id, fixture.Goal(taxonomy.Roles[0].Id), default);
        var competency = await fixture.Db.AcceleratorCompetencies.SingleAsync(x => x.TaxonomyId == taxonomy.Id &&
            x.SkillId == fixture.CodeSkill.Id);
        var definition = new AcceleratorInterviewDefinition { StableKey = "backend-test", Name = "Backend interview",
            TargetRoleId = taxonomy.Roles[0].Id };
        var version = new AcceleratorInterviewVersion { Definition = definition, DefinitionId = definition.Id,
            Version = 1, Type = AcceleratorInterviewType.Technical, Difficulty = AcceleratorPracticeDifficulty.Beginner,
            EstimatedMinutes = 20, TargetExperienceLevel = "Entry", Instructions = "Answer the questions." };
        var section = new AcceleratorInterviewSection { Version = version, InterviewVersionId = version.Id,
            Sequence = 1, Title = "Technical", Type = AcceleratorInterviewType.Technical, EstimatedMinutes = 20,
            IsRequired = true, QuestionCount = 1 };
        var criterion = new AcceleratorInterviewRubricCriterion { InterviewVersionId = version.Id,
            CompetencyId = competency.Id, Sequence = 1, Name = "Correctness", Weight = 100, MaximumScore = 10 };
        var question = new AcceleratorInterviewQuestion { Section = section, SectionId = section.Id, Sequence = 1,
            CompetencyId = competency.Id, Kind = AcceleratorInterviewQuestionKind.Static,
            Type = AcceleratorInterviewQuestionType.TechnicalConcept,
            EvaluationMode = AcceleratorInterviewEvaluationMode.Deterministic,
            PublicPrompt = "Choose A or B", HiddenEvaluatorGuidance = "Never show this secret",
            DeterministicRulesJson = JsonSerializer.Serialize(new[] { new ProjectRule("ExactChoice", criterion.Id, "A") }) };
        version.Competencies.Add(new AcceleratorInterviewCompetency { InterviewVersionId = version.Id,
            CompetencyId = competency.Id, Weight = 100, ExpectedProficiency = 2 });
        version.Sections.Add(section); version.Criteria.Add(criterion); section.Questions.Add(question);
        fixture.Db.Add(version); await fixture.Db.SaveChangesAsync();
        version.Status = AcceleratorInterviewCatalogStatus.Published; await fixture.Db.SaveChangesAsync();

        var service = new AcceleratorPhase5Service(fixture.Store, fixture.Phase2Store,
            new AcceleratorPhase5Store(fixture.Db), new AcceleratorPhase3Store(fixture.Db),
            new AcceleratorPhase4Store(fixture.Db), fixture.Service, fixture.Audit, fixture.Clock);
        var key = Guid.NewGuid();
        var started = await service.StartAsync(fixture.Candidate.Id, new StartInterviewRequest(key, goal.Id, version.Id), default);
        var replay = await service.StartAsync(fixture.Candidate.Id, new StartInterviewRequest(key, goal.Id, version.Id), default);
        Assert.Equal(started.Id, replay.Id);
        Assert.Single(fixture.Db.AcceleratorInterviewSessions);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(started), StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<NotFoundException>(() => service.SessionAsync(fixture.Other.Id, started.Id, default));
        var assigned = Assert.Single(started.Questions);
        var answerKey = Guid.NewGuid();
        var answer = await service.SubmitAnswerAsync(fixture.Candidate.Id, started.Id,
            new SubmitInterviewAnswerRequest(answerKey, assigned.Id, "A", started.Revision), default);
        var answerReplay = await service.SubmitAnswerAsync(fixture.Candidate.Id, started.Id,
            new SubmitInterviewAnswerRequest(answerKey, assigned.Id, "A", started.Revision), default);
        Assert.Equal(answer.Id, answerReplay.Id);
        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAnswerAsync(fixture.Candidate.Id, started.Id,
            new SubmitInterviewAnswerRequest(answerKey, assigned.Id, "B", started.Revision), default));
        var evaluated = await service.EvaluateDeterministicAsync(fixture.Candidate.Id, started.Id, answer.Id, default);
        Assert.Equal(100, evaluated.Score);
        Assert.Equal(evaluated.Id, (await service.EvaluateDeterministicAsync(fixture.Candidate.Id,
            started.Id, answer.Id, default)).Id);
        var current = await service.SessionAsync(fixture.Candidate.Id, started.Id, default);
        var report = await service.CompleteAsync(fixture.Candidate.Id, started.Id,
            new CompleteInterviewRequest(current.Revision), default);
        Assert.Equal(100, report.OverallScore);
        var completionReplay = await service.CompleteAsync(fixture.Candidate.Id, started.Id,
            new CompleteInterviewRequest(current.Revision), default);
        Assert.Equal(JsonSerializer.Serialize(report), JsonSerializer.Serialize(completionReplay));
        var evidence = Assert.Single(fixture.Db.AcceleratorSkillEvidence);
        Assert.Equal(AcceleratorEvidenceSource.MockInterview, evidence.Source);
        Assert.Equal(AcceleratorProficiency.Intermediate, evidence.Proficiency);
        Assert.Equal(evaluated.Id, evidence.InterviewEvaluationId);
        Assert.Single(fixture.Db.AcceleratorReadinessSnapshots);
        var passport = new AcceleratorPassportService(fixture.Store, fixture.Phase2Store,
            new AcceleratorPhase5Store(fixture.Db), fixture.Service, fixture.Audit, fixture.Clock);
        var readiness = await passport.ReadinessAsync(fixture.Candidate.Id, goal.Id, default);
        Assert.Equal(100, readiness.InterviewPerformance);
        Assert.Equal(0, readiness.SkillCoverage);
        Assert.Equal(30, readiness.Total);
        Assert.Equal(readiness, await passport.ReadinessAsync(fixture.Candidate.Id, goal.Id, default));
        var skill = Assert.Single((await passport.PassportAsync(fixture.Candidate.Id, goal.Id, default)).Skills);
        Assert.Equal("InterviewDemonstrated", skill.TrustLevel);
        Assert.Equal(AcceleratorEvidenceSource.MockInterview, Assert.Single(skill.Evidence).Source);
    }

    [Fact]
    public void AiInterviewParserRejectsUnknownDuplicateAndOutOfRangeCriteria()
    {
        var criterion = new AcceleratorInterviewRubricCriterion { Sequence = 1, Weight = 100, MaximumScore = 10 };
        Assert.Equal(8, Assert.Single(InterviewAiResultParser.Parse(
            JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(criterion.Id, 8) }), [criterion])).Score);
        Assert.Throws<ConflictException>(() => InterviewAiResultParser.Parse(
            JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(Guid.NewGuid(), 8) }), [criterion]));
        Assert.Throws<ConflictException>(() => InterviewAiResultParser.Parse(
            JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(criterion.Id, 12) }), [criterion]));
        Assert.Throws<ConflictException>(() => InterviewAiResultParser.Parse(
            JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(criterion.Id, 8),
                new ProjectAiCriterionResult(criterion.Id, 8) }), [criterion]));
    }

    [Fact]
    public async Task InterviewAiUnavailableDoesNotReserveAndVerifiedReplayDoesNotRedispatch()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var taxonomy = await fixture.Service.PublishTaxonomyAsync(fixture.Admin.Id, fixture.Taxonomy(), default);
        var goal = await fixture.Service.CreateGoalAsync(fixture.Candidate.Id, fixture.Goal(taxonomy.Roles[0].Id), default);
        var competency = await fixture.Db.AcceleratorCompetencies.SingleAsync(x => x.TaxonomyId == taxonomy.Id &&
            x.SkillId == fixture.CodeSkill.Id);
        var definition = new AcceleratorInterviewDefinition { StableKey = "ai-backend-test", Name = "AI Backend",
            TargetRoleId = taxonomy.Roles[0].Id };
        var version = new AcceleratorInterviewVersion { Definition = definition, DefinitionId = definition.Id,
            Version = 1, Type = AcceleratorInterviewType.Technical, Difficulty = AcceleratorPracticeDifficulty.Beginner,
            EstimatedMinutes = 20, TargetExperienceLevel = "Entry", Instructions = "Answer questions." };
        var section = new AcceleratorInterviewSection { Version = version, InterviewVersionId = version.Id,
            Sequence = 1, Title = "Technical", Type = AcceleratorInterviewType.Technical, EstimatedMinutes = 20,
            IsRequired = true, QuestionCount = 1 };
        var criterion = new AcceleratorInterviewRubricCriterion { InterviewVersionId = version.Id,
            CompetencyId = competency.Id, Sequence = 1, Name = "Reasoning", Weight = 100, MaximumScore = 10 };
        var question = new AcceleratorInterviewQuestion { Section = section, SectionId = section.Id,
            Sequence = 1, CompetencyId = competency.Id, Kind = AcceleratorInterviewQuestionKind.Static,
            Type = AcceleratorInterviewQuestionType.TechnicalConcept, EvaluationMode = AcceleratorInterviewEvaluationMode.Ai,
            PublicPrompt = "Explain API reliability", HiddenEvaluatorGuidance = "Assess reliability reasoning." };
        version.Competencies.Add(new AcceleratorInterviewCompetency { InterviewVersionId = version.Id,
            CompetencyId = competency.Id, Weight = 100, ExpectedProficiency = 2 });
        version.Sections.Add(section); version.Criteria.Add(criterion); section.Questions.Add(question);
        fixture.Db.Add(version); await fixture.Db.SaveChangesAsync();
        version.Status = AcceleratorInterviewCatalogStatus.Published; await fixture.Db.SaveChangesAsync();
        var phase5Store = new AcceleratorPhase5Store(fixture.Db);
        var phase3Store = new AcceleratorPhase3Store(fixture.Db);
        var interviews = new AcceleratorPhase5Service(fixture.Store, fixture.Phase2Store, phase5Store,
            phase3Store, new AcceleratorPhase4Store(fixture.Db), fixture.Service, fixture.Audit, fixture.Clock);
        var session = await interviews.StartAsync(fixture.Candidate.Id,
            new StartInterviewRequest(Guid.NewGuid(), goal.Id, version.Id), default);
        var answer = await interviews.SubmitAnswerAsync(fixture.Candidate.Id, session.Id,
            new SubmitInterviewAnswerRequest(Guid.NewGuid(), Assert.Single(session.Questions).Id,
                "I use idempotent requests and durable transactions.", session.Revision), default);
        var unavailable = new AcceleratorInterviewAiService(fixture.Store, phase3Store, phase5Store, phase5Store, interviews,
            new AcceleratorQuotaService(fixture.Store, fixture.Service, Options.Create(fixture.Settings),
                fixture.Clock, fixture.Audit, []), fixture.Service, Options.Create(fixture.Settings),
            [], [], fixture.Audit, fixture.Clock);
        await Assert.ThrowsAsync<AcceleratorProviderUnavailableException>(() => unavailable.EvaluateAsync(
            fixture.Candidate.Id, session.Id, answer.Id, default));
        Assert.Empty(fixture.Db.AcceleratorUsageReservations);
        Assert.Empty(fixture.Db.AcceleratorAiDispatchRecords);

        var provider = new InterviewAiTestProvider(fixture.Db, criterion.Id);
        fixture.Settings.Policies[AcceleratorFeature.Interview].Provider = provider.Name;
        fixture.Settings.Policies[AcceleratorFeature.Interview].MaxInputTokens = 3000;
        fixture.Settings.Policies[AcceleratorFeature.Interview].MaxRequestBytes = 32000;
        var quota = new AcceleratorQuotaService(fixture.Store, fixture.Service,
            Options.Create(fixture.Settings), fixture.Clock, fixture.Audit, [provider]);
        var ai = new AcceleratorInterviewAiService(fixture.Store, phase3Store, phase5Store, phase5Store,
            interviews, quota, fixture.Service, Options.Create(fixture.Settings), [provider], [], fixture.Audit, fixture.Clock);
        var first = await ai.EvaluateAsync(fixture.Candidate.Id, session.Id, answer.Id, default);
        var replay = await ai.EvaluateAsync(fixture.Candidate.Id, session.Id, answer.Id, default);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(80, first.Score);
        Assert.Equal(1, provider.Calls);
        Assert.True(provider.SawDurableDispatch);
        Assert.True(provider.SawDispatchedReservation);
        Assert.Equal(AcceleratorUsageState.Completed, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        Assert.Equal(AcceleratorAiDispatchStatus.Reconciled, Assert.Single(fixture.Db.AcceleratorAiDispatchRecords).Status);
        await Assert.ThrowsAsync<ConflictException>(() => interviews.RecordVerifiedAiAsync(fixture.Candidate.Id,
            session.Id, answer.Id, JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(criterion.Id, 2) }), default));

        var secondSession = await interviews.StartAsync(fixture.Candidate.Id,
            new StartInterviewRequest(Guid.NewGuid(), goal.Id, version.Id), default);
        var secondAnswer = await interviews.SubmitAnswerAsync(fixture.Candidate.Id, secondSession.Id,
            new SubmitInterviewAnswerRequest(Guid.NewGuid(), Assert.Single(secondSession.Questions).Id,
                "Another answer requiring review.", secondSession.Revision), default);
        provider.StructuredJsonOverride = "[]";
        await Assert.ThrowsAsync<ConflictException>(() => ai.EvaluateAsync(fixture.Candidate.Id,
            secondSession.Id, secondAnswer.Id, default));
        var uncertain = fixture.Db.AcceleratorAiDispatchRecords.Single(x => x.InterviewAnswerId == secondAnswer.Id);
        Assert.Equal(AcceleratorAiDispatchStatus.Uncertain, uncertain.Status);
        Assert.NotNull(uncertain.ProviderRequestId);
        Assert.Equal("[]", uncertain.ResultJson);
        Assert.Equal(AcceleratorUsageState.Dispatched,
            fixture.Db.AcceleratorUsageReservations.Single(x => x.Id == uncertain.ReservationId).State);
        await Assert.ThrowsAsync<ConflictException>(() => ai.EvaluateAsync(fixture.Candidate.Id,
            secondSession.Id, secondAnswer.Id, default));
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task ConfiguredFollowUpIsBoundedAndCompletionRequiresEvaluation()
    {
        using var fixture = new AcceleratorFixture();
        var taxonomy = await fixture.Service.PublishTaxonomyAsync(fixture.Admin.Id, fixture.Taxonomy(), default);
        var goal = await fixture.Service.CreateGoalAsync(fixture.Candidate.Id, fixture.Goal(taxonomy.Roles[0].Id), default);
        var competency = await fixture.Db.AcceleratorCompetencies.SingleAsync(x => x.TaxonomyId == taxonomy.Id &&
            x.SkillId == fixture.CodeSkill.Id);
        var definition = new AcceleratorInterviewDefinition { StableKey = "follow-up-test", Name = "Follow-up",
            TargetRoleId = taxonomy.Roles[0].Id };
        var version = new AcceleratorInterviewVersion { Definition = definition, DefinitionId = definition.Id,
            Version = 1, Type = AcceleratorInterviewType.Technical, Difficulty = AcceleratorPracticeDifficulty.Beginner,
            EstimatedMinutes = 20, TargetExperienceLevel = "Entry", Instructions = "Answer questions." };
        var section = new AcceleratorInterviewSection { Version = version, InterviewVersionId = version.Id,
            Sequence = 1, Title = "Technical", Type = AcceleratorInterviewType.Technical,
            EstimatedMinutes = 20, IsRequired = true, QuestionCount = 1, MaxFollowUpsPerQuestion = 1 };
        var criterion = new AcceleratorInterviewRubricCriterion { InterviewVersionId = version.Id,
            CompetencyId = competency.Id, Sequence = 1, Name = "Reasoning", Weight = 100, MaximumScore = 10 };
        var question = new AcceleratorInterviewQuestion { Section = section, SectionId = section.Id,
            Sequence = 1, CompetencyId = competency.Id, Kind = AcceleratorInterviewQuestionKind.Static,
            Type = AcceleratorInterviewQuestionType.TechnicalConcept,
            EvaluationMode = AcceleratorInterviewEvaluationMode.Manual, PublicPrompt = "Initial question",
            FollowUpPrompt = "Bounded follow-up" };
        version.Competencies.Add(new AcceleratorInterviewCompetency { InterviewVersionId = version.Id,
            CompetencyId = competency.Id, Weight = 100, ExpectedProficiency = 2 });
        version.Sections.Add(section); version.Criteria.Add(criterion); section.Questions.Add(question);
        fixture.Db.Add(version); await fixture.Db.SaveChangesAsync();
        version.Status = AcceleratorInterviewCatalogStatus.Published; await fixture.Db.SaveChangesAsync();
        var service = new AcceleratorPhase5Service(fixture.Store, fixture.Phase2Store,
            new AcceleratorPhase5Store(fixture.Db), new AcceleratorPhase3Store(fixture.Db),
            new AcceleratorPhase4Store(fixture.Db), fixture.Service, fixture.Audit, fixture.Clock);
        var started = await service.StartAsync(fixture.Candidate.Id,
            new StartInterviewRequest(Guid.NewGuid(), goal.Id, version.Id), default);
        var root = Assert.Single(started.Questions);
        await service.SubmitAnswerAsync(fixture.Candidate.Id, started.Id,
            new SubmitInterviewAnswerRequest(Guid.NewGuid(), root.Id, "Root answer", started.Revision), default);
        var current = await service.SessionAsync(fixture.Candidate.Id, started.Id, default);
        var followUp = Assert.Single(current.Questions, x => x.ParentQuestionId == root.Id);
        Assert.Equal("Bounded follow-up", followUp.Prompt);
        Assert.Equal(followUp.Id, (await service.NextQuestionAsync(fixture.Candidate.Id, started.Id, default))?.Id);
        await service.SubmitAnswerAsync(fixture.Candidate.Id, started.Id,
            new SubmitInterviewAnswerRequest(Guid.NewGuid(), followUp.Id, "Follow-up answer", current.Revision), default);
        current = await service.SessionAsync(fixture.Candidate.Id, started.Id, default);
        Assert.Equal(2, current.Questions.Count);
        await Assert.ThrowsAsync<ConflictException>(() => service.CompleteAsync(fixture.Candidate.Id,
            started.Id, new CompleteInterviewRequest(current.Revision), default));
    }

    [Fact]
    public async Task PublicPassportIsPrivateUntilExplicitSharingAndRevocation()
    {
        using var fixture = new AcceleratorFixture();
        var taxonomy = await fixture.Service.PublishTaxonomyAsync(fixture.Admin.Id, fixture.Taxonomy(), default);
        var goal = await fixture.Service.CreateGoalAsync(fixture.Candidate.Id, fixture.Goal(taxonomy.Roles[0].Id), default);
        var passport = new AcceleratorPassportService(fixture.Store, fixture.Phase2Store,
            new AcceleratorPhase5Store(fixture.Db), fixture.Service, fixture.Audit, fixture.Clock);
        Assert.False((await passport.PassportAsync(fixture.Candidate.Id, goal.Id, default)).IsPublic);
        await Assert.ThrowsAsync<NotFoundException>(() => passport.PassportAsync(fixture.Other.Id, goal.Id, default));
        var shared = await passport.EnableShareAsync(fixture.Candidate.Id,
            new PassportShareRequest(goal.Id, null), default);
        Assert.True(shared.IsEnabled);
        Assert.Equal(64, shared.PublicToken?.Length);
        var publicDto = await passport.PublicAsync(shared.PublicToken!, default);
        Assert.Equal("Backend Developer", publicDto.TargetRole);
        Assert.DoesNotContain(fixture.Candidate.Id.ToString(), JsonSerializer.Serialize(publicDto), StringComparison.Ordinal);
        var revoked = await passport.RevokeShareAsync(fixture.Candidate.Id,
            new PassportShareRequest(goal.Id, shared.Revision), default);
        Assert.False(revoked.IsEnabled);
        await Assert.ThrowsAsync<NotFoundException>(() => passport.PublicAsync(shared.PublicToken!, default));
    }

    [Fact]
    public async Task CatalogRejectsIncompleteDraftAndRetiredVersionCannotStart()
    {
        using var fixture = new AcceleratorFixture();
        var taxonomy = await fixture.Service.PublishTaxonomyAsync(fixture.Admin.Id, fixture.Taxonomy(), default);
        var competency = await fixture.Db.AcceleratorCompetencies.SingleAsync(x => x.TaxonomyId == taxonomy.Id &&
            x.SkillId == fixture.CodeSkill.Id);
        var goal = await fixture.Service.CreateGoalAsync(fixture.Candidate.Id, fixture.Goal(taxonomy.Roles[0].Id), default);
        var catalog = new AcceleratorInterviewCatalogService(fixture.Store, new AcceleratorPhase5Store(fixture.Db),
            new AcceleratorPhase4Store(fixture.Db), fixture.Service, fixture.Audit, fixture.Clock);
        var definitionId = await catalog.CreateDefinitionAsync(fixture.Admin.Id,
            new CreateInterviewDefinitionRequest("catalog-test", "Catalog", "Technical interview", taxonomy.Roles[0].Id), default);
        var draft = await catalog.CreateDraftAsync(fixture.Admin.Id, new CreateInterviewDraftRequest(definitionId, 1,
            AcceleratorInterviewType.Technical, AcceleratorPracticeDifficulty.Beginner, 20, "Entry",
            "Answer the published questions."), default);
        await Assert.ThrowsAsync<BadRequestException>(() => catalog.PublishAsync(fixture.Admin.Id, draft.Id, default));
        var revision = draft.Revision;
        await catalog.UpsertCompetencyAsync(fixture.Admin.Id, draft.Id,
            new UpsertInterviewCompetencyRequest(revision, competency.Id, 2, 100, true, 100), default);
        revision = (await catalog.AdminVersionAsync(fixture.Admin.Id, draft.Id, default)).Revision;
        var sectionId = await catalog.UpsertSectionAsync(fixture.Admin.Id, draft.Id,
            new UpsertInterviewSectionRequest(revision, null, 1, "Technical", AcceleratorInterviewType.Technical,
                20, true, 1, 0), default);
        revision = (await catalog.AdminVersionAsync(fixture.Admin.Id, draft.Id, default)).Revision;
        var criterionId = await catalog.UpsertCriterionAsync(fixture.Admin.Id, draft.Id,
            new UpsertInterviewCriterionRequest(revision, null, 1, competency.Id, "Correctness", "Objective", 100, 10), default);
        revision = (await catalog.AdminVersionAsync(fixture.Admin.Id, draft.Id, default)).Revision;
        await catalog.UpsertQuestionAsync(fixture.Admin.Id, draft.Id,
            new UpsertInterviewQuestionRequest(revision, null, sectionId, 1, competency.Id,
                AcceleratorInterviewQuestionKind.Static, AcceleratorInterviewQuestionType.TechnicalConcept,
                AcceleratorInterviewEvaluationMode.Deterministic, AcceleratorPracticeDifficulty.Beginner,
                "Choose A", "[]", "secret", JsonSerializer.Serialize(new[] { new ProjectRule("ExactChoice", criterionId, "A") }),
                "", true), default);
        await catalog.PublishAsync(fixture.Admin.Id, draft.Id, default);
        var service = new AcceleratorPhase5Service(fixture.Store, fixture.Phase2Store,
            new AcceleratorPhase5Store(fixture.Db), new AcceleratorPhase3Store(fixture.Db),
            new AcceleratorPhase4Store(fixture.Db), fixture.Service, fixture.Audit, fixture.Clock);
        var recommendation = Assert.Single(await service.RecommendationsAsync(fixture.Candidate.Id, goal.Id, default));
        Assert.Contains("C#", recommendation.Reason, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ConflictException>(() => catalog.EditDraftAsync(fixture.Admin.Id, draft.Id,
            new EditInterviewDraftRequest(revision, AcceleratorInterviewType.Technical,
                AcceleratorPracticeDifficulty.Beginner, 20, "Entry", "Changed"), default));
        await catalog.RetireAsync(fixture.Admin.Id, draft.Id, default);
        Assert.Empty(await service.RecommendationsAsync(fixture.Candidate.Id, goal.Id, default));
        await Assert.ThrowsAsync<ConflictException>(() => service.StartAsync(fixture.Candidate.Id,
            new StartInterviewRequest(Guid.NewGuid(), goal.Id, draft.Id), default));
        fixture.Db.ChangeTracker.Clear();
        var retired = await fixture.Db.AcceleratorInterviewVersions.SingleAsync(x => x.Id == draft.Id);
        retired.Instructions = "Tampered historical instructions";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
    }
}

internal sealed class InterviewAiTestProvider(JobPortalDbContext context, Guid criterionId) : IAcceleratorAIProvider
{
    public string Name => "InterviewFake";
    public bool IsAvailable => true;
    public int Calls { get; private set; }
    public bool SawDurableDispatch { get; private set; }
    public bool SawDispatchedReservation { get; private set; }
    public string? StructuredJsonOverride { get; set; }
    public Task<AcceleratorAIResult> ExecuteAsync(AcceleratorAIRequest request, CancellationToken ct)
    {
        Calls++;
        SawDurableDispatch = context.AcceleratorAiDispatchRecords.Any(x => x.ReservationId == request.ReservationId &&
            x.Operation == "InterviewEvaluation" && x.Status == AcceleratorAiDispatchStatus.Dispatched);
        SawDispatchedReservation = context.AcceleratorUsageReservations.Any(x => x.Id == request.ReservationId &&
            x.State == AcceleratorUsageState.Dispatched);
        return Task.FromResult(new AcceleratorAIResult(
            StructuredJsonOverride ?? JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(criterionId, 8) }),
            $"fake-interview-request-{Calls}", "fake-interview-v1", 80, 20, .1m));
    }
}
