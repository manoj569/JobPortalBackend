using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorJourneyContextTests
{
    [Fact]
    public async Task CareerEnrollmentUsesCanonicalRoleForAssessmentGapsAndRoadmap()
    {
        using var fixture = new AcceleratorFixture();
        var taxonomy = await fixture.Service.PublishTaxonomyAsync(fixture.Admin.Id, fixture.Taxonomy(), default);
        var role = taxonomy.Roles.Single(x => x.Code == "backend");
        var competency = await fixture.Db.AcceleratorCompetencies.SingleAsync(x => x.SkillId == fixture.CodeSkill.Id);
        var path = new AcceleratorLearningPath { Type = AcceleratorLearningPathType.Career, CanonicalCareerRoleId = role.Id,
            Slug = "test-career", Title = "Test career", Difficulty = 2, Status = AcceleratorLearningCatalogStatus.Published };
        var enrollment = new AcceleratorPathEnrollment { UserId = fixture.Candidate.Id, LearningPath = path, LearningPathId = path.Id,
            Status = AcceleratorLearningEnrollmentStatus.Enrolled, WeeklyCommitmentMinutes = 300, TargetCompletionAtUtc = fixture.Clock.Utc.AddMonths(3) };
        var template = new AcceleratorAssessmentTemplate { RoleId = role.Id, Version = "career-journey", TimeLimitMinutes = 30, MaximumQuestions = 1, IsActive = true };
        fixture.Db.AddRange(path, enrollment, template, new AcceleratorLearningPathAssessment { LearningPathId = path.Id,
            AssessmentTemplateId = template.Id, Purpose = AcceleratorLearningPathAssessmentPurpose.Baseline },
            new AcceleratorQuestion { TaxonomyId = competency.TaxonomyId, CompetencyId = competency.Id, Version = "career-journey",
                Type = AcceleratorQuestionType.MultipleChoice, Difficulty = 1, Weight = 1, Prompt = "Choose A", OptionsJson = "[\"A\"]", AnswerKeyJson = "{\"answer\":\"A\"}", IsActive = true });
        await fixture.Db.SaveChangesAsync();
        var contexts = Create(fixture);
        var engine = new AcceleratorPhase2Service(fixture.Store, fixture.Phase2Store, fixture.Service, fixture.Audit, fixture.Clock,
            contexts, new AcceleratorJourneyStore(fixture.Db), new AcceleratorLearningPathStore(fixture.Db));
        var attempt = await engine.StartEnrollmentAssessmentAsync(fixture.Candidate.Id, enrollment.Id, AcceleratorLearningPathAssessmentPurpose.Baseline, Guid.NewGuid(), default);
        var answered = await engine.SubmitAnswerAsync(fixture.Candidate.Id, attempt.Id, new(attempt.Questions[0].Id, "{\"answer\":\"A\"}"), default);
        Assert.Equal(100m, (await engine.FinalizeAttemptAsync(fixture.Candidate.Id, attempt.Id, new(answered.Revision), default)).Score);
        Assert.Equal(0, Assert.Single(await engine.EnrollmentGapsAsync(fixture.Candidate.Id, enrollment.Id, default)).Gap);
        Assert.Null((await engine.GenerateEnrollmentRoadmapAsync(fixture.Candidate.Id, enrollment.Id, Guid.NewGuid(), default)).GoalId);
        Assert.Empty(await fixture.Db.AcceleratorCareerGoals.ToArrayAsync());
        Assert.NotNull((await fixture.Db.AcceleratorSkillEvidence.SingleAsync()).JourneyContextId);
    }
    [Fact]
    public async Task CareerContextReplaysAndResolvesOriginalRoleRequirements()
    {
        using var fixture = new AcceleratorFixture();
        var taxonomy = await fixture.Service.PublishTaxonomyAsync(fixture.Admin.Id, fixture.Taxonomy(), default);
        var goal = await fixture.Service.CreateGoalAsync(fixture.Candidate.Id, fixture.Goal(taxonomy.Roles[0].Id), default);
        var service = Create(fixture);
        var context = await service.GetOrCreateForCareerGoalAsync(fixture.Candidate.Id, goal.Id, default);
        Assert.Equal(context.Id, (await service.GetOrCreateForCareerGoalAsync(fixture.Candidate.Id, goal.Id, default)).Id);
        var requirement = Assert.Single(await service.ResolveAsync(fixture.Candidate.Id, context.Id, default));
        Assert.Equal(3, requirement.TargetProficiency);
        Assert.Single(await fixture.Db.AcceleratorJourneyContexts.ToArrayAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetOwnedContextAsync(fixture.Other.Id, context.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetOrCreateForCareerGoalAsync(fixture.Other.Id, goal.Id, default));
    }

    [Fact]
    public async Task SkillEnrollmentResolvesConfiguredCompetenciesWithoutCreatingCareerGoal()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Service.PublishTaxonomyAsync(fixture.Admin.Id, fixture.Taxonomy(), default);
        var competency = await fixture.Db.AcceleratorCompetencies.SingleAsync(x => x.SkillId == fixture.CodeSkill.Id);
        var path = new AcceleratorLearningPath { Type = AcceleratorLearningPathType.Skill, CanonicalSkillId = fixture.CodeSkill.Id,
            Slug = "test-skill", Title = "Test skill", Difficulty = 2, Status = AcceleratorLearningCatalogStatus.Published };
        var enrollment = new AcceleratorPathEnrollment { UserId = fixture.Candidate.Id, LearningPath = path,
            LearningPathId = path.Id, Status = AcceleratorLearningEnrollmentStatus.Enrolled,
            WeeklyCommitmentMinutes = 300, TargetCompletionAtUtc = fixture.Clock.Utc.AddMonths(3) };
        fixture.Db.AddRange(path, enrollment, new AcceleratorLearningPathCompetencyRequirement { LearningPathId = path.Id,
            CompetencyId = competency.Id, TargetProficiency = 4, Weight = 2m, IsRequired = true });
        await fixture.Db.SaveChangesAsync();
        var service = Create(fixture);
        var context = await service.GetOrCreateForEnrollmentAsync(fixture.Candidate.Id, enrollment.Id, default);
        Assert.Null(context.CareerGoalId);
        Assert.Equal(competency.TaxonomyId, context.TaxonomyId);
        Assert.Equal(context.Id, (await service.GetOrCreateForEnrollmentAsync(fixture.Candidate.Id, enrollment.Id, default)).Id);
        var requirement = Assert.Single(await service.ResolveAsync(fixture.Candidate.Id, context.Id, default));
        Assert.Equal(competency.Id, requirement.CompetencyId);
        Assert.Equal(4, requirement.TargetProficiency);
        Assert.Equal(2m, requirement.Weight);
        Assert.Empty(await fixture.Db.AcceleratorCareerGoals.ToArrayAsync());
        var template = new AcceleratorAssessmentTemplate { TaxonomyId = competency.TaxonomyId, Version = "skill-v1",
            TimeLimitMinutes = 30, MaximumQuestions = 1, IsActive = true };
        fixture.Db.AddRange(template, new AcceleratorLearningPathAssessment { LearningPathId = path.Id,
            AssessmentTemplateId = template.Id, Purpose = AcceleratorLearningPathAssessmentPurpose.Baseline },
            new AcceleratorQuestion { TaxonomyId = competency.TaxonomyId, CompetencyId = competency.Id, Version = "skill-v1",
                Type = AcceleratorQuestionType.MultipleChoice, Difficulty = 1, Weight = 1, Prompt = "Choose A",
                OptionsJson = "[\"A\",\"B\"]", AnswerKeyJson = "{\"answer\":\"A\"}", IsActive = true });
        await fixture.Db.SaveChangesAsync();
        var engine = new AcceleratorPhase2Service(fixture.Store, fixture.Phase2Store, fixture.Service, fixture.Audit,
            fixture.Clock, service, new AcceleratorJourneyStore(fixture.Db), new AcceleratorLearningPathStore(fixture.Db));
        var attempt = await engine.StartEnrollmentAssessmentAsync(fixture.Candidate.Id, enrollment.Id,
            AcceleratorLearningPathAssessmentPurpose.Baseline, Guid.NewGuid(), default);
        Assert.Null(attempt.GoalId);
        var answered = await engine.SubmitAnswerAsync(fixture.Candidate.Id, attempt.Id,
            new SubmitAssessmentAnswerRequest(Assert.Single(attempt.Questions).Id, "{\"answer\":\"A\"}"), default);
        var finalized = await engine.FinalizeAttemptAsync(fixture.Candidate.Id, attempt.Id, new(answered.Revision), default);
        Assert.Equal(100m, finalized.Score);
        await engine.FinalizeAttemptAsync(fixture.Candidate.Id, attempt.Id, new(Guid.NewGuid()), default);
        var evidence = Assert.Single(await fixture.Db.AcceleratorSkillEvidence.ToArrayAsync());
        Assert.Equal(context.Id, evidence.JourneyContextId);
        Assert.Null(evidence.GoalId);
        Assert.Equal(0, Assert.Single(await engine.EnrollmentGapsAsync(fixture.Candidate.Id, enrollment.Id, default)).Gap);
        var roadmap = await engine.GenerateEnrollmentRoadmapAsync(fixture.Candidate.Id, enrollment.Id, Guid.NewGuid(), default);
        Assert.Null(roadmap.GoalId);
        Assert.Equal(context.Id, (await fixture.Db.AcceleratorRoadmaps.SingleAsync()).JourneyContextId);
        var secondRoadmap = await engine.GenerateEnrollmentRoadmapAsync(fixture.Candidate.Id, enrollment.Id, Guid.NewGuid(), default);
        Assert.Equal(roadmap.Version + 1, secondRoadmap.Version);
        await Assert.ThrowsAsync<NotFoundException>(() => engine.RoadmapAsync(fixture.Other.Id, roadmap.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => engine.SubmitAnswerAsync(fixture.Other.Id, attempt.Id,
            new SubmitAssessmentAnswerRequest(attempt.Questions[0].Id, "{}"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetOrCreateForEnrollmentAsync(fixture.Other.Id, enrollment.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ResolveAsync(fixture.Other.Id, context.Id, default));
    }

    private static AcceleratorJourneyContextService Create(AcceleratorFixture fixture) => new(fixture.Store,
        new AcceleratorJourneyStore(fixture.Db), new AcceleratorLearningPathStore(fixture.Db), fixture.Phase2Store, fixture.Service);
}
