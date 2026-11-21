using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorPhase4Tests
{
    [Fact]
    public void DeterministicCheckerUsesServerRuleAndRejectsSubjectiveTypes()
    {
        var criterion = new AcceleratorProjectRubricCriterion { Sequence = 1, Weight = 100, MaximumScore = 5 };
        var rubric = new AcceleratorProjectRubric();
        rubric.Criteria.Add(criterion);
        var assignment = new AcceleratorProjectAssignment { Type = AcceleratorAssignmentType.Structured, EvaluationMode = AcceleratorProjectEvaluationMode.Deterministic,
            DeterministicRulesJson = JsonSerializer.Serialize(new[] { new ProjectRule("ExactChoice", criterion.Id, "A") }) };
        var checker = new ProjectDeterministicEvaluator();
        var correct = checker.Evaluate(assignment, rubric, "A");
        Assert.True(correct.Supported);
        Assert.Equal(5, Assert.Single(correct.Observations).Score);
        Assert.Equal(0, Assert.Single(checker.Evaluate(assignment, rubric, "B").Observations).Score);
        assignment.Type = AcceleratorAssignmentType.Architecture;
        Assert.False(checker.Evaluate(assignment, rubric, "A").Supported);
    }

    [Theory]
    [InlineData("RequiredFields", "role|experience", "{\"role\":\"engineer\",\"experience\":3}", true)]
    [InlineData("RequiredFields", "role|experience", "{\"role\":\"engineer\"}", false)]
    [InlineData("RequiredConcepts", "authentication|authorization", "Authorization follows authentication", true)]
    [InlineData("RequiredConcepts", "authentication|authorization", "authentication only", false)]
    [InlineData("Checklist", "tests|documentation", "documentation|tests", true)]
    [InlineData("Checklist", "tests|documentation", "tests", false)]
    public void DeterministicCheckerEvaluatesBoundedServerSideRules(string kind, string expected, string answer, bool passes)
    {
        var criterion = new AcceleratorProjectRubricCriterion { Sequence = 1, Weight = 100, MaximumScore = 10 };
        var rubric = new AcceleratorProjectRubric();
        rubric.Criteria.Add(criterion);
        var assignment = new AcceleratorProjectAssignment { Type = AcceleratorAssignmentType.Structured, EvaluationMode = AcceleratorProjectEvaluationMode.Deterministic,
            DeterministicRulesJson = JsonSerializer.Serialize(new[] { new ProjectRule(kind, criterion.Id, expected) }) };
        var checker = new ProjectDeterministicEvaluator();
        checker.ValidateConfiguration(assignment, rubric);
        var result = checker.Evaluate(assignment, rubric, answer);
        Assert.Equal(passes ? 10 : 0, Assert.Single(result.Observations).Score);
    }

    [Fact]
    public void PublicationConfigurationRejectsInvalidRulesAndSubjectiveRuleMasquerading()
    {
        var criterion = new AcceleratorProjectRubricCriterion { Sequence = 1, Weight = 100, MaximumScore = 5 };
        var rubric = new AcceleratorProjectRubric();
        rubric.Criteria.Add(criterion);
        var assignment = new AcceleratorProjectAssignment { Type = AcceleratorAssignmentType.Structured, EvaluationMode = AcceleratorProjectEvaluationMode.Deterministic,
            DeterministicRulesJson = JsonSerializer.Serialize(new[] { new ProjectRule("ExecuteShell", criterion.Id, "ignored") }) };
        var checker = new ProjectDeterministicEvaluator();
        Assert.Throws<BadRequestException>(() => checker.ValidateConfiguration(assignment, rubric));
        assignment.Type = AcceleratorAssignmentType.Code;
        Assert.Throws<BadRequestException>(() => checker.ValidateConfiguration(assignment, rubric));
    }

    [Fact]
    public void CandidateProjectDtoDoesNotExposeEvaluatorSecrets()
    {
        var detail = new AcceleratorProjectDetailResponse(Guid.NewGuid(), "Project", "Description", "Context", "Instructions",
            [new AcceleratorProjectMilestoneResponse(Guid.NewGuid(), 1, "Milestone", "Description",
                [new AcceleratorProjectAssignmentResponse(Guid.NewGuid(), "Prompt", AcceleratorAssignmentType.Structured, 3)])]);
        var json = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("DeterministicRulesJson", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Expected", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Rubric", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4, AcceleratorProficiency.Beginner, 0.2)]
    [InlineData(8, AcceleratorProficiency.Developing, 0.45)]
    [InlineData(10, AcceleratorProficiency.Intermediate, 0.6)]
    public void ProjectEvidenceIsConservativeAndTraceable(decimal score, AcceleratorProficiency expected, decimal confidence)
    {
        var competencyId = Guid.NewGuid();
        var criterion = new AcceleratorProjectRubricCriterion { CompetencyId = competencyId, Sequence = 1, Weight = 100, MaximumScore = 10 };
        var rubric = new AcceleratorProjectRubric();
        rubric.Criteria.Add(criterion);
        var project = new AcceleratorCandidateProject { UserId = Guid.NewGuid(), GoalId = Guid.NewGuid(), ProjectVersionId = Guid.NewGuid() };
        var submission = new AcceleratorProjectSubmission { CandidateProjectId = project.Id, AssignmentId = Guid.NewGuid() };
        var evaluation = new AcceleratorProjectEvaluation { SubmissionId = submission.Id, EvaluatedAtUtc = DateTime.UtcNow };
        evaluation.Scores.Add(new AcceleratorProjectEvaluationScore { CriterionId = criterion.Id, Score = score });

        var evidence = Assert.Single(ProjectEvidencePolicy.Create(project, submission, evaluation, rubric));
        Assert.Equal(AcceleratorEvidenceSource.VirtualProject, evidence.Source);
        Assert.Equal(expected, evidence.Proficiency);
        Assert.Equal(confidence, evidence.Confidence);
        Assert.Equal(score * 10, evidence.Score);
        Assert.Equal(project.Id, evidence.CandidateProjectId);
        Assert.Equal(project.ProjectVersionId, evidence.ProjectVersionId);
        Assert.Equal(submission.AssignmentId, evidence.AssignmentId);
        Assert.Equal(submission.Id, evidence.SubmissionId);
        Assert.Equal(evaluation.Id, evidence.ProjectEvaluationId);
    }

    [Fact]
    public void ProjectEvidenceCannotDisplaceStrongerVerifiedProficiency()
    {
        var competencyId = Guid.NewGuid();
        var projectEvidence = new AcceleratorSkillEvidence { CompetencyId = competencyId,
            Source = AcceleratorEvidenceSource.VirtualProject, Proficiency = AcceleratorProficiency.Intermediate, Confidence = .6m };
        var prior = new AcceleratorSkillEvidence { CompetencyId = competencyId,
            Source = AcceleratorEvidenceSource.AssessmentDemonstrated, Proficiency = AcceleratorProficiency.Advanced,
            Confidence = .55m };
        ProjectEvidencePolicy.PreserveStrongerVerifiedProficiency([projectEvidence], [prior]);
        Assert.True(projectEvidence.Confidence < prior.Confidence);
        Assert.NotEqual(AcceleratorProficiency.Advanced, projectEvidence.Proficiency);
    }

    [Fact]
    public async Task ProjectEvidenceUpdatesOnlyMatchingActiveRoadmapNodeAndAppendsHistory()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var userId = Guid.NewGuid(); var goalId = Guid.NewGuid(); var competencyId = Guid.NewGuid();
        var roadmap = new AcceleratorRoadmap { UserId = userId, GoalId = goalId, Status = AcceleratorRoadmapStatus.Active,
            Version = 1, PlannedStartAtUtc = DateTime.UtcNow.Date, TargetAtUtc = DateTime.UtcNow.Date.AddDays(30) };
        var matching = new AcceleratorRoadmapNode { Roadmap = roadmap, CompetencyId = competencyId, Status = AcceleratorNodeStatus.Available };
        var unrelated = new AcceleratorRoadmapNode { Roadmap = roadmap, CompetencyId = Guid.NewGuid(), Status = AcceleratorNodeStatus.Available };
        roadmap.Nodes.Add(matching); roadmap.Nodes.Add(unrelated);
        var activity = new AcceleratorLearningActivity { UserId = userId, RoadmapNodeId = matching.Id, Status = AcceleratorNodeStatus.Available };
        db.AddRange(roadmap, activity);
        await db.SaveChangesAsync();

        var project = new AcceleratorCandidateProject { UserId = userId, GoalId = goalId, ProjectVersionId = Guid.NewGuid() };
        var submission = new AcceleratorProjectSubmission { CandidateProjectId = project.Id, AssignmentId = Guid.NewGuid() };
        var criterion = new AcceleratorProjectRubricCriterion { CompetencyId = competencyId, Sequence = 1, Weight = 100, MaximumScore = 10 };
        var rubric = new AcceleratorProjectRubric(); rubric.Criteria.Add(criterion);
        var evaluation = new AcceleratorProjectEvaluation { SubmissionId = submission.Id, EvaluatedAtUtc = DateTime.UtcNow };
        evaluation.Scores.Add(new AcceleratorProjectEvaluationScore { CriterionId = criterion.Id, Score = 9 });
        var phase2 = new AcceleratorPhase2Service(null!, new AcceleratorPhase2Store(db), null!, null!, TimeProvider.System);
        await phase2.RecordProjectEvaluationAsync(project, submission, evaluation, rubric, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(60, matching.ProgressPercent);
        Assert.Equal(AcceleratorNodeStatus.InProgress, matching.Status);
        Assert.Equal(0, unrelated.ProgressPercent);
        Assert.Equal(AcceleratorNodeStatus.Available, unrelated.Status);
        Assert.Single(db.AcceleratorSkillEvidence);
        var progress = Assert.Single(db.AcceleratorProgressEvents);
        Assert.Equal(AcceleratorProgressKind.EvidenceSubmitted, progress.Kind);
        Assert.Contains(evaluation.Id.ToString(), progress.EvidenceJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletionRequiresEveryRequiredAssignmentAndUsesLatestAcceptedAttempt()
    {
        var version = new AcceleratorProjectVersion();
        var first = new AcceleratorProjectMilestone { Sequence = 1, IsRequired = true };
        var second = new AcceleratorProjectMilestone { Sequence = 2, IsRequired = true };
        var optional = new AcceleratorProjectMilestone { Sequence = 3, IsRequired = false };
        var firstAssignment = new AcceleratorProjectAssignment();
        var secondAssignment = new AcceleratorProjectAssignment();
        first.Assignments.Add(firstAssignment); second.Assignments.Add(secondAssignment);
        optional.Assignments.Add(new AcceleratorProjectAssignment());
        version.Milestones.Add(first); version.Milestones.Add(second); version.Milestones.Add(optional);

        var partial = ProjectCompletionPolicy.Calculate(version,
            [new(firstAssignment.Id, 1, 60), new(firstAssignment.Id, 2, 80)]);
        Assert.Equal(50, partial.ProgressPercent);
        Assert.Equal(second.Id, partial.NextMilestoneId);
        Assert.Null(partial.FinalScore);

        var complete = ProjectCompletionPolicy.Calculate(version,
            [new(firstAssignment.Id, 1, 80), new(firstAssignment.Id, 2, 90), new(secondAssignment.Id, 1, 70)]);
        Assert.Equal(100, complete.ProgressPercent);
        Assert.Null(complete.NextMilestoneId);
        Assert.Equal(80, complete.FinalScore);
    }

    [Fact]
    public void ProviderResultMustMatchServerRubricExactly()
    {
        var first = new AcceleratorProjectRubricCriterion { Sequence = 1, Weight = 60, MaximumScore = 10 };
        var second = new AcceleratorProjectRubricCriterion { Sequence = 2, Weight = 40, MaximumScore = 5 };
        var rubric = new AcceleratorProjectRubric(); rubric.Criteria.Add(first); rubric.Criteria.Add(second);
        var valid = JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(first.Id, 8), new ProjectAiCriterionResult(second.Id, 4) });
        Assert.Equal(2, ProjectAiResultParser.Parse(valid, rubric).Count);
        var missing = JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(first.Id, 8) });
        Assert.Throws<ConflictException>(() => ProjectAiResultParser.Parse(missing, rubric));
        var inflated = JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(first.Id, 11), new ProjectAiCriterionResult(second.Id, 4) });
        Assert.Throws<ConflictException>(() => ProjectAiResultParser.Parse(inflated, rubric));
        var duplicated = JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(first.Id, 8), new ProjectAiCriterionResult(first.Id, 8) });
        Assert.Throws<ConflictException>(() => ProjectAiResultParser.Parse(duplicated, rubric));
    }

    [Fact]
    public async Task VerifiedNonChargeableResolutionReleasesOnlyWithEvidenceAndIsIdempotent()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var request = AcceleratorFixture.Reservation() with { Feature = AcceleratorFeature.Review };
        var reservation = await fixture.Quota.ReserveAsync(fixture.Candidate.Id, request, CancellationToken.None);
        await fixture.Quota.MarkDispatchedAsync(fixture.Candidate.Id, reservation.Id, CancellationToken.None);
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Quota.ResolveVerifiedNonChargeableAsync(
            fixture.Candidate.Id, reservation.Id, "provider-request", "", CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Quota.ResolveVerifiedNonChargeableAsync(
            fixture.Other.Id, reservation.Id, "provider-request", "verified-non-chargeable", CancellationToken.None));
        var released = await fixture.Quota.ResolveVerifiedNonChargeableAsync(fixture.Candidate.Id, reservation.Id,
            "provider-request", "verified-non-chargeable", CancellationToken.None);
        Assert.Equal(AcceleratorUsageState.Released, released.State);
        await fixture.Quota.ResolveVerifiedNonChargeableAsync(fixture.Candidate.Id, reservation.Id,
            "provider-request", "verified-non-chargeable", CancellationToken.None);
        Assert.Equal(3, await fixture.Db.Set<AcceleratorUsageEvent>().CountAsync());
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Quota.CompleteAsync(
            fixture.Candidate.Id, reservation.Id, 10, 10, .1m, CancellationToken.None));
    }

    [Fact]
    public async Task ProjectAiUnavailableDoesNotReserveQuotaOrExposeAnotherCandidatesSubmission()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var definition = new AcceleratorProjectDefinition { StableKey = "ai-unavailable", IsActive = true };
        var version = new AcceleratorProjectVersion { Definition = definition, DefinitionId = definition.Id,
            Version = 1, CatalogStatus = AcceleratorProjectCatalogStatus.Draft, IsActive = true };
        var milestone = new AcceleratorProjectMilestone { ProjectVersion = version, ProjectVersionId = version.Id, Sequence = 1 };
        var assignment = new AcceleratorProjectAssignment { Milestone = milestone, MilestoneId = milestone.Id,
            Sequence = 1, Type = AcceleratorAssignmentType.Architecture, EvaluationMode = AcceleratorProjectEvaluationMode.Ai };
        milestone.Assignments.Add(assignment); version.Milestones.Add(milestone);
        var project = new AcceleratorCandidateProject { UserId = fixture.Candidate.Id, GoalId = Guid.NewGuid(),
            ProjectVersionId = version.Id, Status = AcceleratorCandidateProjectStatus.Submitted };
        var submission = new AcceleratorProjectSubmission { CandidateProjectId = project.Id, AssignmentId = assignment.Id,
            AttemptNumber = 1, RequestKey = Guid.NewGuid(), Content = "candidate architecture", ContentHash = new string('A', 64) };
        fixture.Db.AddRange(definition, version, milestone, assignment);
        await fixture.Db.SaveChangesAsync();
        version.CatalogStatus = AcceleratorProjectCatalogStatus.Published;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.AddRange(project, submission);
        await fixture.Db.SaveChangesAsync();

        var projectStore = new AcceleratorPhase4Store(fixture.Db);
        var phase4 = new AcceleratorPhase4Service(fixture.Store, fixture.Phase2,
            projectStore, fixture.Service, new ProjectDeterministicEvaluator(), fixture.Audit, fixture.Clock, fixture.Work);
        var ai = new AcceleratorProjectAiService(fixture.Store, projectStore, new AcceleratorPhase3Store(fixture.Db),
            phase4, fixture.Quota, fixture.Service, Options.Create(new AcceleratorOptions { Enabled = true, AIEnabled = false }),
            [new UnavailableAcceleratorAIProvider()], [], fixture.Audit, fixture.Clock);
        await Assert.ThrowsAsync<NotFoundException>(() => ai.EvaluateAsync(fixture.Other.Id, project.Id, submission.Id, CancellationToken.None));
        await Assert.ThrowsAsync<AcceleratorProviderUnavailableException>(() => ai.EvaluateAsync(
            fixture.Candidate.Id, project.Id, submission.Id, CancellationToken.None));
        Assert.Empty(fixture.Db.AcceleratorUsageReservations);
        Assert.Empty(fixture.Db.AcceleratorAiDispatchRecords);
        Assert.Equal(AcceleratorCandidateProjectStatus.Submitted, project.Status);
    }

    [Fact]
    public async Task TutorReconciliationDoesNotClaimProjectEvaluationDispatches()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var now = DateTime.UtcNow;
        var tutor = new AcceleratorAiDispatchRecord { UserId = Guid.NewGuid(), RequestKey = Guid.NewGuid(),
            Operation = "TutorMessage", Status = AcceleratorAiDispatchStatus.Uncertain, NextReconciliationAtUtc = now.AddMinutes(-1) };
        var project = new AcceleratorAiDispatchRecord { UserId = Guid.NewGuid(), RequestKey = Guid.NewGuid(),
            Operation = "ProjectEvaluation", Status = AcceleratorAiDispatchStatus.Uncertain, NextReconciliationAtUtc = now.AddMinutes(-1) };
        db.AddRange(tutor, project);
        await db.SaveChangesAsync();
        var tutorRows = await new AcceleratorPhase3Store(db).EligibleReconciliationDispatchesAsync(now, 10, CancellationToken.None);
        var projectRows = await new AcceleratorPhase4Store(db).EligibleProjectDispatchesAsync(now, 10, CancellationToken.None);
        Assert.Equal(tutor.Id, Assert.Single(tutorRows).Id);
        Assert.Equal(project.Id, Assert.Single(projectRows).Id);
    }

    [Fact]
    public async Task ReconciledDispatchOutputAndReservationIdentityAreImmutable()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var dispatch = new AcceleratorAiDispatchRecord { UserId = Guid.NewGuid(), RequestKey = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(), Operation = "ProjectEvaluation", Status = AcceleratorAiDispatchStatus.Reconciled,
            ResultJson = "[]" };
        db.Add(dispatch);
        await db.SaveChangesAsync();
        dispatch.ResultJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var loaded = await db.AcceleratorAiDispatchRecords.SingleAsync();
        loaded.ReservationId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task FakeProjectProviderSeesDurableDispatchAndQuotaReplayDoesNotChargeTwice()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var definition = new AcceleratorProjectDefinition { StableKey = "fake-ai-project", IsActive = true };
        var version = new AcceleratorProjectVersion { Definition = definition, DefinitionId = definition.Id,
            Version = 1, CatalogStatus = AcceleratorProjectCatalogStatus.Draft, IsActive = true };
        var milestone = new AcceleratorProjectMilestone { ProjectVersion = version, ProjectVersionId = version.Id,
            Sequence = 1, IsRequired = true };
        var assignment = new AcceleratorProjectAssignment { Milestone = milestone, MilestoneId = milestone.Id,
            Sequence = 1, Type = AcceleratorAssignmentType.Architecture,
            EvaluationMode = AcceleratorProjectEvaluationMode.Ai };
        milestone.Assignments.Add(assignment); version.Milestones.Add(milestone);
        var criterion = new AcceleratorProjectRubricCriterion { Sequence = 1, Weight = 100, MaximumScore = 10 };
        var rubric = new AcceleratorProjectRubric { ProjectVersionId = version.Id, Version = 1, IsActive = true };
        rubric.Criteria.Add(criterion);
        fixture.Db.AddRange(definition, version, milestone, assignment, rubric, criterion);
        await fixture.Db.SaveChangesAsync();
        version.CatalogStatus = AcceleratorProjectCatalogStatus.Published;
        await fixture.Db.SaveChangesAsync();
        var project = new AcceleratorCandidateProject { UserId = fixture.Candidate.Id, GoalId = Guid.NewGuid(),
            ProjectVersionId = version.Id, CurrentMilestoneId = milestone.Id,
            Status = AcceleratorCandidateProjectStatus.Submitted };
        var submission = new AcceleratorProjectSubmission { CandidateProjectId = project.Id, AssignmentId = assignment.Id,
            AttemptNumber = 1, RequestKey = Guid.NewGuid(), Content = "candidate architecture",
            ContentHash = new string('A', 64), Status = AcceleratorSubmissionStatus.Submitted };
        fixture.Db.AddRange(project, submission);
        await fixture.Db.SaveChangesAsync();

        var provider = new ProjectAiTestProvider(fixture.Db, criterion.Id);
        fixture.Settings.Policies[AcceleratorFeature.Review].Provider = provider.Name;
        fixture.Settings.Policies[AcceleratorFeature.Review].MaxInputTokens = 2000;
        var quota = new AcceleratorQuotaService(fixture.Store, fixture.Service,
            Options.Create(fixture.Settings), fixture.Clock, fixture.Audit, [provider]);
        var projectStore = new AcceleratorPhase4Store(fixture.Db);
        var phase4 = new AcceleratorPhase4Service(fixture.Store, fixture.Phase2,
            projectStore, fixture.Service, new ProjectDeterministicEvaluator(), fixture.Audit, fixture.Clock, fixture.Work);
        var ai = new AcceleratorProjectAiService(fixture.Store, projectStore, new AcceleratorPhase3Store(fixture.Db),
            phase4, quota, fixture.Service, Options.Create(fixture.Settings), [provider], [], fixture.Audit, fixture.Clock);

        var first = await ai.EvaluateAsync(fixture.Candidate.Id, project.Id, submission.Id, CancellationToken.None);
        var replay = await ai.EvaluateAsync(fixture.Candidate.Id, project.Id, submission.Id, CancellationToken.None);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(80, first.Score);
        Assert.Equal(1, provider.Calls);
        Assert.True(provider.SawDurableDispatch);
        Assert.True(provider.SawDispatchedReservation);
        Assert.Single(fixture.Db.AcceleratorProjectEvaluations);
        Assert.Equal(AcceleratorUsageState.Completed, Assert.Single(fixture.Db.AcceleratorUsageReservations).State);
        Assert.Equal(AcceleratorAiDispatchStatus.Reconciled, Assert.Single(fixture.Db.AcceleratorAiDispatchRecords).Status);
        Assert.Equal(AcceleratorCandidateProjectStatus.Completed, project.Status);
    }
    [Fact]
    public async Task DraftVersionCanChangeButPublishedVersionCannotReturnToDraft()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var version = new AcceleratorProjectVersion { Version = 1, EstimatedMinutes = 60, Title = "Draft", CatalogStatus = AcceleratorProjectCatalogStatus.Draft };
        db.Add(version);
        await db.SaveChangesAsync();
        version.Title = "Edited draft";
        await db.SaveChangesAsync();
        version.CatalogStatus = AcceleratorProjectCatalogStatus.Published;
        await db.SaveChangesAsync();
        version.CatalogStatus = AcceleratorProjectCatalogStatus.Draft;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DraftChildrenAreEditableUntilPublicationOrCandidateUse()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var version = new AcceleratorProjectVersion { Version = 1, EstimatedMinutes = 60, CatalogStatus = AcceleratorProjectCatalogStatus.Draft };
        var milestone = new AcceleratorProjectMilestone { ProjectVersionId = version.Id, Sequence = 1, EstimatedMinutes = 60, Title = "Draft" };
        db.AddRange(version, milestone);
        await db.SaveChangesAsync();
        milestone.Title = "Revised";
        await db.SaveChangesAsync();
        version.CatalogStatus = AcceleratorProjectCatalogStatus.Published;
        await db.SaveChangesAsync();
        db.Add(new AcceleratorProjectMilestone { ProjectVersionId = version.Id, Sequence = 2, EstimatedMinutes = 30, Title = "Forbidden addition" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var publishedMilestone = await db.Set<AcceleratorProjectMilestone>().SingleAsync(x => x.Id == milestone.Id);
        publishedMilestone.Title = "Forbidden";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var other = new AcceleratorProjectVersion { Version = 1, EstimatedMinutes = 60, CatalogStatus = AcceleratorProjectCatalogStatus.Draft };
        var candidate = new AcceleratorCandidateProject { ProjectVersionId = other.Id, StartedAtUtc = DateTime.UtcNow };
        db.AddRange(other, candidate);
        await db.SaveChangesAsync();
        other.Title = "Forbidden after candidate start";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void PublishedRubricRequiresPositiveUniqueCriteriaAndExactlyOneHundredWeight()
    {
        var rubric = new AcceleratorProjectRubric();
        rubric.Criteria.Add(new AcceleratorProjectRubricCriterion { Sequence = 1, Weight = 60, MaximumScore = 5 });
        rubric.Criteria.Add(new AcceleratorProjectRubricCriterion { Sequence = 2, Weight = 40, MaximumScore = 10 });
        AcceleratorPhase4Service.ValidateRubric(rubric);

        rubric.Criteria.Last().Weight = 39;
        Assert.Throws<BadRequestException>(() => AcceleratorPhase4Service.ValidateRubric(rubric));
        rubric.Criteria.Last().Weight = 40;
        rubric.Criteria.Last().Sequence = 1;
        Assert.Throws<BadRequestException>(() => AcceleratorPhase4Service.ValidateRubric(rubric));
    }

    [Fact]
    public async Task HistoricalSubmissionsAndScoresRejectModificationAndDeletion()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var submission = new AcceleratorProjectSubmission { AttemptNumber = 1, Content = "original", ContentHash = new string('A', 64) };
        var score = new AcceleratorProjectEvaluationScore { Score = 1 };
        db.AddRange(submission, score);
        await db.SaveChangesAsync();

        submission.Content = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Remove(score);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}

internal sealed class ProjectAiTestProvider(JobPortalDbContext context, Guid criterionId) : IAcceleratorAIProvider
{
    public string Name => "ProjectFake";
    public bool IsAvailable => true;
    public int Calls { get; private set; }
    public bool SawDurableDispatch { get; private set; }
    public bool SawDispatchedReservation { get; private set; }
    public Task<AcceleratorAIResult> ExecuteAsync(AcceleratorAIRequest request, CancellationToken ct)
    {
        Calls++;
        SawDurableDispatch = context.AcceleratorAiDispatchRecords.Any(x => x.ReservationId == request.ReservationId &&
            x.Operation == "ProjectEvaluation" && x.Status == AcceleratorAiDispatchStatus.Dispatched);
        SawDispatchedReservation = context.AcceleratorUsageReservations.Any(x => x.Id == request.ReservationId &&
            x.State == AcceleratorUsageState.Dispatched);
        return Task.FromResult(new AcceleratorAIResult(
            JsonSerializer.Serialize(new[] { new ProjectAiCriterionResult(criterionId, 8) }),
            "fake-project-provider-request", "fake-project-v1", 80, 20, .1m));
    }
}
