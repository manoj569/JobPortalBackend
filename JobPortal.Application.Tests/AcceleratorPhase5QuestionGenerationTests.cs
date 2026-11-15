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

public sealed class AcceleratorPhase5QuestionGenerationTests
{
    [Fact]
    public void GeneratedQuestionPolicyRejectsInvalidShapeAndUnapprovedFollowUps()
    {
        var version = new AcceleratorInterviewVersion { Difficulty = AcceleratorPracticeDifficulty.Beginner };
        var competency = Guid.NewGuid();
        version.Competencies.Add(new AcceleratorInterviewCompetency { CompetencyId = competency });
        var section = new AcceleratorInterviewSection { MaxFollowUpsPerQuestion = 0 };
        var source = new AcceleratorInterviewQuestion { CompetencyId = competency,
            Type = AcceleratorInterviewQuestionType.TechnicalConcept };
        var valid = new GeneratedInterviewQuestionPayload("Explain an API retry", source.Type,
            version.Difficulty, [competency], false, "{}");
        Assert.Throws<ConflictException>(() => InterviewQuestionGenerationPolicy.Parse(
            JsonSerializer.Serialize(valid with { QuestionText = " " }), version, section, source));
        Assert.Throws<ConflictException>(() => InterviewQuestionGenerationPolicy.Parse(
            JsonSerializer.Serialize(valid with { FollowUpEligible = true }), version, section, source));
        Assert.Throws<ConflictException>(() => InterviewQuestionGenerationPolicy.Parse(
            JsonSerializer.Serialize(valid with { Difficulty = AcceleratorPracticeDifficulty.Advanced }),
            version, section, source));
        Assert.Throws<ConflictException>(() => InterviewQuestionGenerationPolicy.Parse(
            JsonSerializer.Serialize(valid).TrimEnd('}') + ",\"CandidateId\":\"fake\"}",
            version, section, source));
    }

    [Fact]
    public async Task UnavailableProviderNeverFabricatesAndCandidateCannotUseAnotherSession()
    {
        using var fixture = new AcceleratorFixture();
        var setup = await Setup(fixture, false);
        var ai = Ai(fixture, setup.Service, [], []);
        var request = new GenerateInterviewQuestionRequest(Guid.NewGuid(), setup.Session.Revision);
        await Assert.ThrowsAsync<NotFoundException>(() => ai.GenerateQuestionAsync(fixture.Other.Id,
            setup.Session.Id, setup.Section.Id, request, default));
        await Assert.ThrowsAsync<AcceleratorProviderUnavailableException>(() => ai.GenerateQuestionAsync(
            fixture.Candidate.Id, setup.Session.Id, setup.Section.Id, request, default));
        Assert.Empty(fixture.Db.AcceleratorUsageReservations);
        Assert.Empty(fixture.Db.AcceleratorAiDispatchRecords);
        Assert.Empty(setup.Session.Questions);
    }

    [Fact]
    public async Task StaticFallbackRequiresExplicitPolicyAndDoesNotConsumeQuota()
    {
        using var fixture = new AcceleratorFixture();
        var setup = await Setup(fixture, true);
        var ai = Ai(fixture, setup.Service, [], []);
        var request = new GenerateInterviewQuestionRequest(Guid.NewGuid(), setup.Session.Revision);
        var first = await ai.GenerateQuestionAsync(fixture.Candidate.Id, setup.Session.Id, setup.Section.Id, request, default);
        var replay = await ai.GenerateQuestionAsync(fixture.Candidate.Id, setup.Session.Id, setup.Section.Id, request, default);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal("Approved static fallback", first.Prompt);
        Assert.Equal("ApprovedStaticFallback", Assert.Single(setup.Session.Questions).GenerationProvider);
        Assert.Empty(fixture.Db.AcceleratorUsageReservations);
        Assert.Empty(fixture.Db.AcceleratorAiDispatchRecords);
    }

    [Fact]
    public async Task GenerationPersistsDispatchAndQuotaBeforeProviderAndReplaysExactlyOnce()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var setup = await Setup(fixture, false);
        var provider = new InterviewGenerationTestProvider(fixture.Db, setup.Competency.Id);
        Configure(fixture, provider);
        var ai = Ai(fixture, setup.Service, [provider], []);
        var request = new GenerateInterviewQuestionRequest(Guid.NewGuid(), setup.Session.Revision);
        var first = await ai.GenerateQuestionAsync(fixture.Candidate.Id, setup.Session.Id, setup.Section.Id, request, default);
        var replay = await ai.GenerateQuestionAsync(fixture.Candidate.Id, setup.Session.Id, setup.Section.Id, request, default);
        Assert.Equal(first.Id, replay.Id);
        Assert.True(provider.SawDurableDispatch);
        Assert.True(provider.SawDispatchedReservation);
        Assert.Equal(1, provider.Calls);
        Assert.Single(setup.Session.Questions);
        Assert.Equal(AcceleratorUsageState.Completed, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        var dispatch = Assert.Single(fixture.Db.AcceleratorAiDispatchRecords);
        Assert.Equal(AcceleratorAiDispatchStatus.Reconciled, dispatch.Status);
        Assert.Equal(dispatch.Id, Assert.Single(setup.Session.Questions).GenerationDispatchId);
        Assert.DoesNotContain("HiddenEvaluatorGuidance", JsonSerializer.Serialize(first), StringComparison.Ordinal);
        Assert.DoesNotContain("GenerationMetadata", JsonSerializer.Serialize(first), StringComparison.Ordinal);
        var current = await setup.Service.SessionAsync(fixture.Candidate.Id, setup.Session.Id, default);
        await Assert.ThrowsAsync<ConflictException>(() => ai.GenerateQuestionAsync(fixture.Candidate.Id,
            setup.Session.Id, setup.Section.Id,
            new GenerateInterviewQuestionRequest(Guid.NewGuid(), current.Revision), default));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task UnknownCompetencyIsUncertainAndNeverBlindlyRegenerated()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var setup = await Setup(fixture, false);
        var provider = new InterviewGenerationTestProvider(fixture.Db, Guid.NewGuid());
        Configure(fixture, provider);
        var ai = Ai(fixture, setup.Service, [provider], []);
        var request = new GenerateInterviewQuestionRequest(Guid.NewGuid(), setup.Session.Revision);
        await Assert.ThrowsAsync<ConflictException>(() => ai.GenerateQuestionAsync(fixture.Candidate.Id,
            setup.Session.Id, setup.Section.Id, request, default));
        var dispatch = Assert.Single(fixture.Db.AcceleratorAiDispatchRecords);
        Assert.Equal(AcceleratorAiDispatchStatus.Uncertain, dispatch.Status);
        Assert.NotNull(dispatch.ResultJson);
        Assert.Empty(setup.Session.Questions);
        Assert.Equal(AcceleratorUsageState.Dispatched, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        await Assert.ThrowsAsync<ConflictException>(() => ai.GenerateQuestionAsync(fixture.Candidate.Id,
            setup.Session.Id, setup.Section.Id, request, default));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task TimeoutKeepsReservationUncertainAndDoesNotResend()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var setup = await Setup(fixture, false);
        var provider = new InterviewGenerationTestProvider(fixture.Db, setup.Competency.Id) { ThrowTimeout = true };
        Configure(fixture, provider);
        var ai = Ai(fixture, setup.Service, [provider], []);
        var request = new GenerateInterviewQuestionRequest(Guid.NewGuid(), setup.Session.Revision);
        await Assert.ThrowsAsync<TimeoutException>(() => ai.GenerateQuestionAsync(fixture.Candidate.Id,
            setup.Session.Id, setup.Section.Id, request, default));
        await Assert.ThrowsAsync<ConflictException>(() => ai.GenerateQuestionAsync(fixture.Candidate.Id,
            setup.Session.Id, setup.Section.Id, request, default));
        Assert.Equal(1, provider.Calls);
        Assert.Equal(AcceleratorAiDispatchStatus.Uncertain, Assert.Single(fixture.Db.AcceleratorAiDispatchRecords).Status);
        Assert.Equal(AcceleratorUsageState.Dispatched, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        Assert.Empty(setup.Session.Questions);
    }

    [Fact]
    public async Task VerifiedGenerationSuccessRecoversQuestionAndFinalizesOnlyOnce()
    {
        using var fixture = new AcceleratorFixture();
        var (setup, provider, dispatch) = await TimedOutGeneration(fixture);
        var claims = new InterviewClaimTestStore(fixture.Db, dispatch);
        var verifier = new InterviewVerificationTestDouble(Guid.Empty, provider.Name)
        {
            Result = new ProjectAiVerification(AcceleratorDispatchVerificationStatus.VerifiedSuccess,
                "verified-generation-request", JsonSerializer.Serialize(new GeneratedInterviewQuestionPayload(
                    "Recovered interview question", AcceleratorInterviewQuestionType.TechnicalConcept,
                    AcceleratorPracticeDifficulty.Beginner, [setup.Competency.Id], false, "{}")),
                70, 15, .1m, false, "provider-generation-evidence")
        };
        dispatch.ProviderRequestId = "verified-generation-request";
        await fixture.Db.SaveChangesAsync();
        var ai = Ai(fixture, setup.Service, [provider], [verifier], claims);
        var first = await ai.ProcessBatchAsync(5, default);
        Assert.Equal(1, first.Resolved);
        Assert.Equal("Recovered interview question", Assert.Single(setup.Session.Questions).PublicPrompt);
        Assert.Equal(AcceleratorAiDispatchStatus.Reconciled, dispatch.Status);
        Assert.Equal(AcceleratorUsageState.Completed, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        await ai.ProcessBatchAsync(5, default);
        Assert.Single(setup.Session.Questions);
        Assert.Single(fixture.Db.AcceleratorUsageEvents.Where(x => x.State == AcceleratorUsageState.Completed));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task VerifiedGenerationFailureRequiresNonChargeableEvidence()
    {
        using var fixture = new AcceleratorFixture();
        var (setup, provider, dispatch) = await TimedOutGeneration(fixture);
        var claims = new InterviewClaimTestStore(fixture.Db, dispatch);
        var verifier = new InterviewVerificationTestDouble(Guid.Empty, provider.Name)
        {
            Result = new ProjectAiVerification(AcceleratorDispatchVerificationStatus.VerifiedFailure,
                "verified-generation-request", null, null, null, null, false, null)
        };
        dispatch.ProviderRequestId = "verified-generation-request";
        await fixture.Db.SaveChangesAsync();
        var ai = Ai(fixture, setup.Service, [provider], [verifier], claims);
        await ai.ProcessBatchAsync(5, default);
        Assert.Equal(AcceleratorAiDispatchStatus.Uncertain, dispatch.Status);
        Assert.Equal(AcceleratorUsageState.Dispatched, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        dispatch.NextReconciliationAtUtc = fixture.Clock.Utc.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        verifier.Result = verifier.Result with { ConfirmedNonChargeable = true,
            EvidenceReference = "provider-noncharge-generation" };
        await ai.ProcessBatchAsync(5, default);
        Assert.Equal(AcceleratorAiDispatchStatus.Failed, dispatch.Status);
        Assert.Equal(AcceleratorUsageState.Released, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        Assert.Empty(setup.Session.Questions);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task UnknownGenerationOutcomeRemainsUnresolvedWithoutProviderRetry()
    {
        using var fixture = new AcceleratorFixture();
        var (setup, provider, dispatch) = await TimedOutGeneration(fixture);
        dispatch.ProviderRequestId = "verified-generation-request";
        await fixture.Db.SaveChangesAsync();
        var claims = new InterviewClaimTestStore(fixture.Db, dispatch);
        var verifier = new InterviewVerificationTestDouble(Guid.Empty, provider.Name)
        {
            Result = new ProjectAiVerification(AcceleratorDispatchVerificationStatus.Unknown,
                null, null, null, null, null, false, null)
        };
        var ai = Ai(fixture, setup.Service, [provider], [verifier], claims);
        var result = await ai.ProcessBatchAsync(5, default);
        Assert.Equal(1, result.Pending);
        Assert.Equal(AcceleratorAiDispatchStatus.Uncertain, dispatch.Status);
        Assert.Equal(AcceleratorUsageState.Dispatched, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        Assert.Empty(setup.Session.Questions);
        Assert.Equal(1, provider.Calls);
    }

    private static async Task<((AcceleratorPhase5Service Service, AcceleratorInterviewSession Session,
        AcceleratorInterviewSection Section, AcceleratorCompetency Competency) Setup,
        InterviewGenerationTestProvider Provider, AcceleratorAiDispatchRecord Dispatch)> TimedOutGeneration(
        AcceleratorFixture fixture)
    {
        await fixture.Activate();
        var setup = await Setup(fixture, false);
        var provider = new InterviewGenerationTestProvider(fixture.Db, setup.Competency.Id) { ThrowTimeout = true };
        Configure(fixture, provider);
        var ai = Ai(fixture, setup.Service, [provider], []);
        await Assert.ThrowsAsync<TimeoutException>(() => ai.GenerateQuestionAsync(fixture.Candidate.Id,
            setup.Session.Id, setup.Section.Id,
            new GenerateInterviewQuestionRequest(Guid.NewGuid(), setup.Session.Revision), default));
        var dispatch = Assert.Single(fixture.Db.AcceleratorAiDispatchRecords);
        dispatch.NextReconciliationAtUtc = fixture.Clock.Utc.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        return (setup, provider, dispatch);
    }

    private static async Task<(AcceleratorPhase5Service Service, AcceleratorInterviewSession Session,
        AcceleratorInterviewSection Section, AcceleratorCompetency Competency)> Setup(AcceleratorFixture fixture,
        bool allowStaticFallback)
    {
        var taxonomy = await fixture.Service.PublishTaxonomyAsync(fixture.Admin.Id, fixture.Taxonomy(), default);
        var goal = await fixture.Service.CreateGoalAsync(fixture.Candidate.Id, fixture.Goal(taxonomy.Roles[0].Id), default);
        var competency = await fixture.Db.AcceleratorCompetencies.SingleAsync(x => x.TaxonomyId == taxonomy.Id &&
            x.SkillId == fixture.CodeSkill.Id);
        var definition = new AcceleratorInterviewDefinition { StableKey = "generation-test", Name = "Backend interview",
            TargetRoleId = taxonomy.Roles[0].Id };
        var version = new AcceleratorInterviewVersion { Definition = definition, DefinitionId = definition.Id,
            Version = 1, Type = AcceleratorInterviewType.Technical,
            Difficulty = AcceleratorPracticeDifficulty.Beginner, EstimatedMinutes = 20,
            TargetExperienceLevel = "Entry", Instructions = "Answer questions." };
        var section = new AcceleratorInterviewSection { Version = version, InterviewVersionId = version.Id,
            Sequence = 1, Title = "Technical", Type = AcceleratorInterviewType.Technical,
            EstimatedMinutes = 20, IsRequired = true, QuestionCount = 1,
            AllowStaticFallback = allowStaticFallback };
        var source = new AcceleratorInterviewQuestion { Section = section, SectionId = section.Id,
            Sequence = 1, CompetencyId = competency.Id, Kind = AcceleratorInterviewQuestionKind.AiGenerated,
            Type = AcceleratorInterviewQuestionType.TechnicalConcept,
            EvaluationMode = AcceleratorInterviewEvaluationMode.Ai,
            Difficulty = AcceleratorPracticeDifficulty.Beginner, PublicPrompt = "API reliability scenario",
            HiddenEvaluatorGuidance = "Internal scoring guidance" };
        version.Competencies.Add(new AcceleratorInterviewCompetency { InterviewVersionId = version.Id,
            CompetencyId = competency.Id, Weight = 100, ExpectedProficiency = 2 });
        version.Criteria.Add(new AcceleratorInterviewRubricCriterion { InterviewVersionId = version.Id,
            Sequence = 1, Weight = 100, MaximumScore = 10, Name = "Reasoning" });
        version.Sections.Add(section); section.Questions.Add(source);
        if (allowStaticFallback)
            section.Questions.Add(new AcceleratorInterviewQuestion { Section = section, SectionId = section.Id,
                Sequence = 2, CompetencyId = competency.Id, Kind = AcceleratorInterviewQuestionKind.Static,
                Type = AcceleratorInterviewQuestionType.TechnicalConcept,
                EvaluationMode = AcceleratorInterviewEvaluationMode.Manual,
                PublicPrompt = "Approved static fallback" });
        fixture.Db.Add(version); await fixture.Db.SaveChangesAsync();
        version.Status = AcceleratorInterviewCatalogStatus.Published; await fixture.Db.SaveChangesAsync();
        var store = new AcceleratorPhase5Store(fixture.Db);
        var service = new AcceleratorPhase5Service(fixture.Store, fixture.Phase2Store, store,
            new AcceleratorPhase3Store(fixture.Db), new AcceleratorPhase4Store(fixture.Db),
            fixture.Service, fixture.Audit, fixture.Clock);
        var started = await service.StartAsync(fixture.Candidate.Id,
            new StartInterviewRequest(Guid.NewGuid(), goal.Id, version.Id, true), default);
        Assert.Empty(started.Questions);
        var session = await fixture.Db.AcceleratorInterviewSessions.SingleAsync(x => x.Id == started.Id);
        return (service, session, section, competency);
    }

    private static void Configure(AcceleratorFixture fixture, InterviewGenerationTestProvider provider)
    {
        fixture.Settings.Policies[AcceleratorFeature.Interview].Provider = provider.Name;
        fixture.Settings.Policies[AcceleratorFeature.Interview].MaxInputTokens = 3000;
        fixture.Settings.Policies[AcceleratorFeature.Interview].MaxRequestBytes = 32000;
    }

    private static AcceleratorInterviewAiService Ai(AcceleratorFixture fixture, AcceleratorPhase5Service service,
        IReadOnlyList<IAcceleratorAIProvider> providers, IReadOnlyList<IInterviewAiDispatchVerifier> verifiers,
        IInterviewDispatchStore? claims = null)
    {
        var phase5Store = new AcceleratorPhase5Store(fixture.Db);
        var phase3Store = new AcceleratorPhase3Store(fixture.Db);
        var quota = new AcceleratorQuotaService(fixture.Store, fixture.Service, Options.Create(fixture.Settings),
            fixture.Clock, fixture.Audit, providers);
        return new AcceleratorInterviewAiService(fixture.Store, phase3Store, phase5Store, claims ?? phase5Store,
            service, quota, fixture.Service, Options.Create(fixture.Settings), providers, verifiers,
            fixture.Audit, fixture.Clock);
    }
}

internal sealed class InterviewGenerationTestProvider(JobPortalDbContext context, Guid competencyId) : IAcceleratorAIProvider
{
    public string Name => "InterviewGenerationFake";
    public bool IsAvailable => true;
    public bool ThrowTimeout { get; set; }
    public int Calls { get; private set; }
    public bool SawDurableDispatch { get; private set; }
    public bool SawDispatchedReservation { get; private set; }
    public Task<AcceleratorAIResult> ExecuteAsync(AcceleratorAIRequest request, CancellationToken ct)
    {
        Calls++;
        SawDurableDispatch = context.AcceleratorAiDispatchRecords.Any(x => x.ReservationId == request.ReservationId &&
            x.Operation == "InterviewQuestionGeneration" && x.Status == AcceleratorAiDispatchStatus.Dispatched);
        SawDispatchedReservation = context.AcceleratorUsageReservations.Any(x => x.Id == request.ReservationId &&
            x.State == AcceleratorUsageState.Dispatched);
        if (ThrowTimeout) throw new TimeoutException("Fake provider outcome is ambiguous.");
        return Task.FromResult(new AcceleratorAIResult(JsonSerializer.Serialize(new GeneratedInterviewQuestionPayload(
            "Explain idempotent API design", AcceleratorInterviewQuestionType.TechnicalConcept,
            AcceleratorPracticeDifficulty.Beginner, [competencyId], false, "{}")),
            $"fake-generation-{Calls}", "fake-generation-v1", 80, 20, .1m));
    }
}
