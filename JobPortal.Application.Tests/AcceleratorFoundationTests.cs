using System.Reflection;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorFoundationTests
{
    [Fact]
    public void DefaultsAreSeparateFailClosedAndTaxComesFromExistingPricing()
    {
        using var f = new AcceleratorFixture();
        var defaults = new AcceleratorOptions();
        Assert.True(defaults.IsValid()); Assert.False(defaults.Enabled); Assert.False(defaults.AIEnabled);
        var plan = f.Service.Plan();
        Assert.Equal(278m, plan.BaseAmountInr); Assert.Equal(328.04m, plan.TotalAmountInr);
        Assert.False(plan.CheckoutAvailable); Assert.False(plan.AIAvailable);
        Assert.Equal(900, plan.Allowances[AcceleratorFeature.Tutor]);
        Assert.Equal(15, plan.Allowances[AcceleratorFeature.Review]);
        Assert.Equal(8, plan.Allowances[AcceleratorFeature.Interview]);
        Assert.Equal(10, plan.Allowances[AcceleratorFeature.Roadmap]);
        Assert.Equal(100, plan.Allowances[AcceleratorFeature.Practice]);
        f.Pricing.Gst = 12m;
        Assert.Equal(311.36m, f.Service.Plan().TotalAmountInr);
        defaults.AIEnabled = true;
        Assert.False(defaults.IsValid()); // No provider/model/rates have been configured.
    }

    [Theory]
    [InlineData(typeof(AcceleratorController), "Candidate")]
    [InlineData(typeof(AdminAcceleratorController), "Administrator")]
    public void ControllersRequireExplicitRolesAndHaveNoActivationOrUsageMutationEndpoint(Type type, string role)
    {
        Assert.Equal(role, Assert.Single(type.GetCustomAttributes<AuthorizeAttribute>()).Roles);
        Assert.Empty(type.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.All(type.GetMethods().Where(x => x.DeclaringType == type), method =>
        {
            Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
            Assert.DoesNotContain("Activate", method.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("Reserve", method.Name, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CanonicalSkillCreationIsAdministratorOnlyNormalizedAndIdempotent()
    {
        using var f = new AcceleratorFixture();
        await Assert.ThrowsAsync<AppException>(() => f.Service.CreateCanonicalSkillAsync(f.Candidate.Id, new("New skill"), default));
        var first = await f.Service.CreateCanonicalSkillAsync(f.Admin.Id, new("  Product   Strategy "), default);
        var repeat = await f.Service.CreateCanonicalSkillAsync(f.Admin.Id, new("product strategy"), default);
        Assert.Equal(first.Id, repeat.Id);
        Assert.Equal("Product Strategy", first.Name);
        var found = await f.Service.SkillsAsync(f.Candidate.Id, "strategy", 1, 20, default);
        Assert.Equal(first.Id, Assert.Single(found).Id);
    }

    [Fact]
    public async Task TaxonomySupportsTechnicalAndNontechnicalRolesAndImmutableVersionReplay()
    {
        using var f = new AcceleratorFixture();
        var request = f.Taxonomy();
        var version = await f.Service.PublishTaxonomyAsync(f.Admin.Id, request, default);
        Assert.Equal(2, version.Roles.Count); Assert.Equal(2, version.Skills.Count);
        Assert.Equal(version.Id, (await f.Service.PublishTaxonomyAsync(f.Admin.Id, request, default)).Id);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.PublishTaxonomyAsync(f.Admin.Id,
            request with { Roles = [request.Roles[0] with { Description = "A changed published role." }] }, default));
        await Assert.ThrowsAsync<AppException>(() => f.Service.PublishTaxonomyAsync(f.Candidate.Id, f.Taxonomy("other"), default));
        f.CodeSkill.Name = "Renamed canonical skill";
        await f.Db.SaveChangesAsync();
        Assert.Contains((await f.Service.TaxonomyAsync(f.Candidate.Id, version.Id, default))!.Skills, x => x.Name == "C#");
    }

    [Fact]
    public async Task Phase2AssessmentIsCandidateOwnedIdempotentAndDeterministicallyScored()
    {
        using var f = new AcceleratorFixture();
        var taxonomy = await f.Service.PublishTaxonomyAsync(f.Admin.Id, f.Taxonomy(), default);
        var goal = await f.Service.CreateGoalAsync(f.Candidate.Id, f.Goal(taxonomy.Roles[0].Id), default);
        var competency = await f.Db.Set<AcceleratorCompetency>().SingleAsync(x => x.SkillId == f.CodeSkill.Id);
        var template = new AcceleratorAssessmentTemplate { RoleId = goal.RoleId, Version = "v1", TimeLimitMinutes = 30, MaximumQuestions = 1, IsActive = true };
        var question = new AcceleratorQuestion { TaxonomyId = competency.TaxonomyId, CompetencyId = competency.Id, Version = "v1", Type = AcceleratorQuestionType.MultipleChoice, Difficulty = 2, Weight = 1, Prompt = "Choose A", OptionsJson = "[\"A\",\"B\"]", AnswerKeyJson = "{\"answer\":\"A\"}", RubricJson = "{}", Explanation = "A", IsActive = true };
        f.Db.AddRange(template, question); await f.Db.SaveChangesAsync();
        var request = new CreateAssessmentAttemptRequest(Guid.NewGuid(), goal.Id, template.Id);
        var attempt = await f.Phase2.StartAttemptAsync(f.Candidate.Id, request, default);
        Assert.Equal(attempt.Id, (await f.Phase2.StartAttemptAsync(f.Candidate.Id, request, default)).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Phase2.SubmitAnswerAsync(f.Other.Id, attempt.Id, new(attempt.Questions[0].Id, "{\"answer\":\"A\"}"), default));
        var submitted = await f.Phase2.SubmitAnswerAsync(f.Candidate.Id, attempt.Id, new(attempt.Questions[0].Id, "{\"answer\":\"A\"}"), default);
        var complete = await f.Phase2.FinalizeAttemptAsync(f.Candidate.Id, attempt.Id, new(submitted.Revision), default);
        Assert.Equal(100m, complete.Score); Assert.Equal(AcceleratorAssessmentStatus.Completed, complete.Status);
        Assert.Equal(complete.Id, (await f.Phase2.FinalizeAttemptAsync(f.Candidate.Id, attempt.Id, new(Guid.NewGuid()), default)).Id);
        Assert.Single(await f.Db.Set<AcceleratorSkillEvidence>().ToArrayAsync());
    }

    [Fact]
    public async Task Phase2RoadmapWarnsWhenTimelineIsInfeasibleAndPreservesCompletedNodesOnRecalculation()
    {
        using var f = new AcceleratorFixture();
        var taxonomy = await f.Service.PublishTaxonomyAsync(f.Admin.Id, f.Taxonomy(), default);
        var goal = await f.Service.CreateGoalAsync(f.Candidate.Id, f.Goal(taxonomy.Roles[0].Id) with { TargetAtUtc = f.Clock.Utc.AddDays(1), WeeklyStudyMinutes = 15 }, default);
        var first = await f.Phase2.GenerateRoadmapAsync(f.Candidate.Id, new(Guid.NewGuid(), goal.Id), default);
        Assert.False(first.TimelineFeasible); Assert.Contains("infeasible", first.Explanation, StringComparison.OrdinalIgnoreCase);
        var node = first.Nodes[0]; var activity = first.Schedule.First(x => x.RoadmapNodeId == node.Id);
        await f.Phase2.RecordProgressAsync(f.Candidate.Id, activity.Id, new(Guid.NewGuid(), AcceleratorProgressKind.Completed, "{}", activity.Revision), default);
        var second = await f.Phase2.GenerateRoadmapAsync(f.Candidate.Id, new(Guid.NewGuid(), goal.Id), default);
        Assert.Equal(2, second.Version); Assert.Contains(second.Nodes, x => x.CompetencyId == node.CompetencyId && x.Status == AcceleratorNodeStatus.Completed);
        Assert.Equal(AcceleratorRoadmapStatus.Superseded, (await f.Db.Set<AcceleratorRoadmap>().OrderBy(x => x.Version).FirstAsync()).Status);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("parent-cycle")]
    [InlineData("unknown-skill")]
    [InlineData("resource")]
    [InlineData("proficiency")]
    [InlineData("duplicate-role")]
    public async Task TaxonomyRejectsInvalidGraphsAndUnvalidatedLabels(string defect)
    {
        using var f = new AcceleratorFixture(); var request = f.Taxonomy();
        if (defect == "cycle") request = request with { Skills = [request.Skills[0] with { PrerequisiteSkillIds = [f.ProductSkill.Id] }, request.Skills[1] with { PrerequisiteSkillIds = [f.CodeSkill.Id] }] };
        if (defect == "parent-cycle") request = request with { Skills = [request.Skills[0] with { ParentSkillId = f.ProductSkill.Id }, request.Skills[1] with { ParentSkillId = f.CodeSkill.Id }] };
        if (defect == "unknown-skill") request = request with { Skills = [request.Skills[0] with { SkillId = Guid.NewGuid() }, request.Skills[1]] };
        if (defect == "resource") request = request with { Skills = [request.Skills[0] with { LearningResources = ["javascript:alert(1)"] }, request.Skills[1]] };
        if (defect == "proficiency") request = request with { Roles = [request.Roles[0] with { Requirements = [new(f.CodeSkill.Id, 9)] }] };
        if (defect == "duplicate-role") request = request with { Roles = [request.Roles[0], request.Roles[0]] };
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.PublishTaxonomyAsync(f.Admin.Id, request, default));
        Assert.Empty(await f.Db.Set<AcceleratorTaxonomy>().ToArrayAsync());
    }

    [Fact]
    public async Task ProfileIsPrivateCanonicalSelfReportedAndRevisionProtected()
    {
        using var f = new AcceleratorFixture();
        var request = f.Profile();
        var first = await f.Service.SaveProfileAsync(f.Candidate.Id, request, default);
        Assert.Equal("SelfReported", first.EvidenceMethod);
        Assert.Null(await f.Service.ProfileAsync(f.Other.Id, default));
        Assert.Equal(f.Clock.Utc, first.LinkedResumeUploadedAtUtc);
        var updated = await f.Service.SaveProfileAsync(f.Candidate.Id, request with { ExpectedRevision = first.Revision, EducationSummary = "Updated qualification" }, default);
        Assert.NotEqual(first.Revision, updated.Revision);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.SaveProfileAsync(f.Candidate.Id, request with { ExpectedRevision = first.Revision }, default));
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.SaveProfileAsync(f.Other.Id,
            request with { LinkCurrentResume = false, Skills = [new(Guid.NewGuid(), 5)] }, default));
    }

    [Fact]
    public async Task ProfileDeletionAndDeletedAccountMaintenanceErasePersonalTextAndResumeLinks()
    {
        using var f = new AcceleratorFixture();
        await f.Service.SaveProfileAsync(f.Candidate.Id, f.Profile(), default);
        f.Candidate.IsDeleted = true; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.ProfileAsync(f.Candidate.Id, default));
        Assert.Equal(1, await f.Quota.MaintainAsync(default));
        var erased = await f.Db.Set<AcceleratorProfile>().IgnoreQueryFilters().SingleAsync();
        Assert.True(erased.IsDeleted); Assert.Equal(string.Empty, erased.EducationSummary); Assert.Null(erased.LinkedResumeUploadedAtUtc);
        Assert.All(await f.Db.Set<AcceleratorProfileSkill>().IgnoreQueryFilters().ToArrayAsync(), x => Assert.True(x.IsDeleted));
        Assert.Equal(0, await f.Quota.MaintainAsync(default));
    }

    [Fact]
    public async Task ErasingOwnProfileDoesNotDeleteTheExistingResumeOrGoalHistory()
    {
        using var f = new AcceleratorFixture();
        await f.Service.SaveProfileAsync(f.Candidate.Id, f.Profile(), default);
        var taxonomy = await f.Service.PublishTaxonomyAsync(f.Admin.Id, f.Taxonomy(), default);
        var goal = await f.Service.CreateGoalAsync(f.Candidate.Id, f.Goal(taxonomy.Roles[0].Id), default);
        Assert.True(await f.Service.EraseProfileAsync(f.Candidate.Id, default));
        Assert.Null(await f.Service.ProfileAsync(f.Candidate.Id, default));
        Assert.NotNull(f.Candidate.ResumeStorageKey);
        Assert.Equal(goal.Id, (await f.Service.GoalAsync(f.Candidate.Id, goal.Id, default)).Id);
    }

    [Fact]
    public async Task MultipleGoalsPreservePinnedVersionsAndRejectCrossCandidateMutations()
    {
        using var f = new AcceleratorFixture();
        var firstVersion = await f.Service.PublishTaxonomyAsync(f.Admin.Id, f.Taxonomy(), default);
        var request = f.Goal(firstVersion.Roles[0].Id);
        var first = await f.Service.CreateGoalAsync(f.Candidate.Id, request, default);
        Assert.Equal(first.Id, (await f.Service.CreateGoalAsync(f.Candidate.Id, request, default)).Id);
        var nextVersion = await f.Service.PublishTaxonomyAsync(f.Admin.Id, f.Taxonomy("v2"), default);
        await f.Service.CreateGoalAsync(f.Candidate.Id, f.Goal(nextVersion.Roles[1].Id), default);
        Assert.Equal(2, (await f.Service.GoalsAsync(f.Candidate.Id, 1, 20, default)).Count);
        Assert.Equal("v1", (await f.Service.GoalAsync(f.Candidate.Id, first.Id, default)).TaxonomyVersion);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.CreateGoalAsync(f.Candidate.Id, request with { RoleId = nextVersion.Roles[0].Id }, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GoalAsync(f.Other.Id, first.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ChangeGoalAsync(f.Other.Id, first.Id, new(first.Revision, AcceleratorGoalStatus.Paused), default));
        var paused = await f.Service.ChangeGoalAsync(f.Candidate.Id, first.Id, new(first.Revision, AcceleratorGoalStatus.Paused), default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ChangeGoalAsync(f.Candidate.Id, first.Id, new(first.Revision, AcceleratorGoalStatus.Active), default));
        var resumed = await f.Service.ChangeGoalAsync(f.Candidate.Id, first.Id, new(paused.Revision, AcceleratorGoalStatus.Active), default);
        Assert.Equal(first.RoleId, resumed.RoleId);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Failed)]
    public async Task ActivationRequiresExistingVerifiedPaidPayment(PaymentStatus status)
    {
        using var f = new AcceleratorFixture(); var payment = await f.Payment();
        payment.Status = status; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ActivateVerifiedPaymentAsync(f.Candidate.Id, payment.Id, default));
        Assert.Empty(await f.Db.Set<AcceleratorBillingCycle>().ToArrayAsync());
    }

    [Fact]
    public async Task PortalMembershipsAndMismatchedPaymentSnapshotsNeverGrantAcceleratorAccess()
    {
        using var f = new AcceleratorFixture(); var payment = await f.Payment();
        payment.PlanCode = "AI_APPLY_PRO"; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ActivateVerifiedPaymentAsync(f.Candidate.Id, payment.Id, default));
        Assert.Equal("NotSubscribed", (await f.Service.EntitlementAsync(f.Candidate.Id, default)).Status);
        payment.PlanCode = AcceleratorOptions.PlanCode; payment.Amount = 1; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ActivateVerifiedPaymentAsync(f.Candidate.Id, payment.Id, default));
    }

    [Fact]
    public async Task BillingCyclesArePaymentIdempotentSnapshotAllocationsAndRenewWithoutLosingAccess()
    {
        using var f = new AcceleratorFixture(); var payment = await f.Payment();
        var id = await f.Service.ActivateVerifiedPaymentAsync(f.Candidate.Id, payment.Id, default);
        Assert.Equal(id, await f.Service.ActivateVerifiedPaymentAsync(f.Candidate.Id, payment.Id, default));
        var entitlement = await f.Service.EntitlementAsync(f.Candidate.Id, default);
        Assert.Equal(f.Clock.Utc.AddMonths(1), entitlement.EndsAtUtc);
        Assert.Equal(28, entitlement.EndsAtUtc!.Value.Day);
        f.Settings.Allowances[AcceleratorFeature.Tutor] = 1;
        Assert.Equal(900, (await f.Service.EntitlementAsync(f.Candidate.Id, default)).Usage.Single(x => x.Feature == AcceleratorFeature.Tutor).Allocated);
        var another = await f.Payment();
        var renewalId = await f.Service.ActivateVerifiedPaymentAsync(f.Candidate.Id, another.Id, default);
        Assert.Equal(renewalId, await f.Service.ActivateVerifiedPaymentAsync(f.Candidate.Id, another.Id, default));
        var renewal = await f.Db.AcceleratorBillingCycles.SingleAsync(x => x.Id == renewalId);
        Assert.Equal(entitlement.EndsAtUtc, renewal.StartsAtUtc);
        Assert.Equal(entitlement.EndsAtUtc!.Value.AddMonths(1), renewal.EndsAtUtc);
        f.Clock.Utc = entitlement.EndsAtUtc.Value;
        Assert.Equal("Active", (await f.Service.EntitlementAsync(f.Candidate.Id, default)).Status);
        f.Clock.Utc = renewal.EndsAtUtc;
        Assert.Equal("Expired", (await f.Service.EntitlementAsync(f.Candidate.Id, default)).Status);
        await Assert.ThrowsAsync<AppException>(() => f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default));
    }

    [Fact]
    public async Task RefundedPaymentsImmediatelyRevokeReservationAndDispatchAccess()
    {
        using var f = new AcceleratorFixture(); var payment = await f.Activate();
        var usage = await f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default);
        payment.Status = PaymentStatus.Refunded; await f.Db.SaveChangesAsync();
        Assert.Equal("Revoked", (await f.Service.EntitlementAsync(f.Candidate.Id, default)).Status);
        await Assert.ThrowsAsync<AppException>(() => f.Quota.MarkDispatchedAsync(f.Candidate.Id, usage.Id, default));
        await Assert.ThrowsAsync<AppException>(() => f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default));
    }

    [Fact]
    public async Task ReservationReplayIsPayloadBoundAndConsumesOneUnit()
    {
        using var f = new AcceleratorFixture(); await f.Activate(); var request = AcceleratorFixture.Reservation();
        var first = await f.Quota.ReserveAsync(f.Candidate.Id, request, default);
        Assert.Equal(first.Id, (await f.Quota.ReserveAsync(f.Candidate.Id, request, default)).Id);
        Assert.Single(await f.Db.Set<AcceleratorUsageReservation>().ToArrayAsync());
        Assert.Single(await f.Db.Set<AcceleratorUsageEvent>().ToArrayAsync());
        await Assert.ThrowsAsync<ConflictException>(() => f.Quota.ReserveAsync(f.Candidate.Id, request with { Feature = AcceleratorFeature.Review }, default));
        await Assert.ThrowsAsync<ConflictException>(() => f.Quota.ReserveAsync(f.Candidate.Id, request with { RequestHash = new string('B', 64) }, default));
        var summary = (await f.Service.EntitlementAsync(f.Candidate.Id, default)).Usage.Single(x => x.Feature == AcceleratorFeature.Tutor);
        Assert.Equal(1, summary.Reserved); Assert.Equal(899, summary.Remaining);
    }

    [Fact]
    public async Task ConcurrentReservationsCannotExceedAllowanceInTheTestStore()
    {
        using var f = new AcceleratorFixture(); f.Settings.Allowances[AcceleratorFeature.Tutor] = 2; await f.Activate();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            try { await f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default); return true; }
            catch (AppException ex) when (ex.Code == "accelerator_quota_exhausted") { return false; }
        }));
        Assert.Equal(2, outcomes.Count(x => x));
        Assert.Equal(2, await f.Db.Set<AcceleratorUsageReservation>().CountAsync());
    }

    [Fact]
    public async Task UnusedReleaseIsIdempotentAndCrossCandidateAccessIsRejected()
    {
        using var f = new AcceleratorFixture(); await f.Activate();
        var row = await f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Quota.ReleaseUnusedAsync(f.Other.Id, row.Id, default));
        Assert.Empty(await f.Quota.HistoryAsync(f.Other.Id, 1, 20, default));
        await f.Quota.ReleaseUnusedAsync(f.Candidate.Id, row.Id, default);
        await f.Quota.ReleaseUnusedAsync(f.Candidate.Id, row.Id, default);
        Assert.Equal(2, await f.Db.Set<AcceleratorUsageEvent>().CountAsync());
        Assert.Equal(900, (await f.Service.EntitlementAsync(f.Candidate.Id, default)).Usage.Single(x => x.Feature == AcceleratorFeature.Tutor).Remaining);
    }

    [Fact]
    public async Task DispatchedTimeoutNeverFreesQuotaOrPermitsDuplicateDispatch()
    {
        using var f = new AcceleratorFixture(); await f.Activate(); var request = AcceleratorFixture.Reservation();
        var row = await f.Quota.ReserveAsync(f.Candidate.Id, request, default);
        await f.Quota.MarkDispatchedAsync(f.Candidate.Id, row.Id, default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Quota.MarkDispatchedAsync(f.Candidate.Id, row.Id, default));
        await Assert.ThrowsAsync<ConflictException>(() => f.Quota.ReleaseUnusedAsync(f.Candidate.Id, row.Id, default));
        f.Clock.Utc = f.Clock.Utc.AddSeconds(f.Settings.ReservationSeconds + 1);
        Assert.Equal(1, await f.Quota.MaintainAsync(default));
        Assert.Equal(AcceleratorUsageState.Uncertain, (await f.Quota.ReserveAsync(f.Candidate.Id, request, default)).State);
        Assert.Equal(1, (await f.Service.EntitlementAsync(f.Candidate.Id, default)).Usage.Single(x => x.Feature == AcceleratorFeature.Tutor).Consumed);
        var completed = await f.Quota.CompleteAsync(f.Candidate.Id, row.Id, 80, 20, .1m, default);
        Assert.Equal(AcceleratorUsageState.Completed, completed.State);
        await f.Quota.CompleteAsync(f.Candidate.Id, row.Id, 80, 20, .1m, default);
        Assert.Equal(4, await f.Db.Set<AcceleratorUsageEvent>().CountAsync());
        await Assert.ThrowsAsync<ConflictException>(() => f.Quota.CompleteAsync(f.Candidate.Id, row.Id, 80, 20, .2m, default));
    }

    [Fact]
    public async Task ExpiredUnusedReservationsReleaseAndCannotDispatchAcrossUtcDayBoundary()
    {
        using var f = new AcceleratorFixture(); await f.Activate();
        f.Clock.Utc = f.Clock.Utc.Date.AddHours(23).AddMinutes(59).AddSeconds(50);
        var row = await f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default);
        f.Clock.Utc = f.Clock.Utc.AddSeconds(20);
        await Assert.ThrowsAsync<ConflictException>(() => f.Quota.MarkDispatchedAsync(f.Candidate.Id, row.Id, default));
        f.Clock.Utc = f.Clock.Utc.AddSeconds(121);
        Assert.Equal(1, await f.Quota.MaintainAsync(default));
        Assert.Equal(AcceleratorUsageState.Released, (await f.Db.Set<AcceleratorUsageReservation>().SingleAsync()).State);
    }

    [Fact]
    public async Task DailyAndBillingCycleCostCapsRemainIndependentAcrossCalendarMonthBoundary()
    {
        using var f = new AcceleratorFixture();
        f.Settings.PerRequestCostLimitInr = .2m; f.Settings.UserDailyCostLimitInr = .4m;
        f.Settings.UserCycleCostLimitInr = .6m; f.Settings.GlobalDailyCostLimitInr = .4m; f.Settings.GlobalMonthlyCostLimitInr = .8m;
        await f.Activate();
        await f.Consume(); await f.Consume();
        var daily = await Assert.ThrowsAsync<AppException>(() => f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default));
        Assert.Equal("accelerator_cost_limit", daily.Code);
        f.Clock.Utc = f.Clock.Utc.AddDays(1); // Jan 31 -> Feb 1; still the same subscription cycle.
        await f.Consume();
        var cycle = await Assert.ThrowsAsync<AppException>(() => f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default));
        Assert.Equal("accelerator_cost_limit", cycle.Code);
    }

    [Fact]
    public async Task ActualOverrunsAreRecordedAndBlockFutureSpendRatherThanErasingCharges()
    {
        using var f = new AcceleratorFixture(); await f.Activate();
        var row = await f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default);
        await f.Quota.MarkDispatchedAsync(f.Candidate.Id, row.Id, default);
        var final = await f.Quota.CompleteAsync(f.Candidate.Id, row.Id, 100, 101, 60m, default);
        Assert.True(final.PolicyViolation); Assert.Equal(60m, final.ActualCostInr);
        var error = await Assert.ThrowsAsync<AppException>(() => f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default));
        Assert.Equal("accelerator_cost_limit", error.Code);
    }

    [Fact]
    public async Task RateAndConcurrencyLimitsAreEnforcedEvenWhenAnUnusedCallIsReleased()
    {
        using var f = new AcceleratorFixture(); f.Settings.UserConcurrency = 1; await f.Activate();
        var first = await f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default);
        var busy = await Assert.ThrowsAsync<AppException>(() => f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default));
        Assert.Equal("accelerator_concurrency_limit", busy.Code);
        await f.Quota.ReleaseUnusedAsync(f.Candidate.Id, first.Id, default);
        f.Settings.RequestsPerMinute = 1;
        var rate = await Assert.ThrowsAsync<AppException>(() => f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default));
        Assert.Equal("accelerator_rate_limit", rate.Code);
    }

    [Fact]
    public async Task MissingProviderAndOversizedInputsFailBeforeAnyReservationOrExternalCall()
    {
        using var f = new AcceleratorFixture(); await f.Activate();
        var unavailable = new AcceleratorQuotaService(f.Store, f.Service, Options.Create(f.Settings), f.Clock, f.Audit, [new UnavailableAcceleratorAIProvider()]);
        await Assert.ThrowsAsync<AcceleratorProviderUnavailableException>(() => unavailable.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default));
        await Assert.ThrowsAsync<BadRequestException>(() => f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation() with { InputTokens = 101 }, default));
        await Assert.ThrowsAsync<BadRequestException>(() => f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation() with { RequestBytes = 1001 }, default));
        Assert.Empty(await f.Db.Set<AcceleratorUsageReservation>().ToArrayAsync());
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public async Task PublishedTaxonomyAndUsageLedgerRejectMutation()
    {
        using var f = new AcceleratorFixture(); await f.Activate();
        var reservation = await f.Quota.ReserveAsync(f.Candidate.Id, AcceleratorFixture.Reservation(), default);
        var entry = await f.Db.Set<AcceleratorUsageEvent>().SingleAsync(); entry.CostInr++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        Assert.Equal(reservation.ReservedCostInr, (await f.Db.Set<AcceleratorUsageEvent>().SingleAsync()).CostInr);
        await f.Service.PublishTaxonomyAsync(f.Admin.Id, f.Taxonomy(), default);
        var taxonomy = await f.Db.Set<AcceleratorTaxonomy>().SingleAsync(); taxonomy.Version = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }
}

internal sealed class AcceleratorFixture : IDisposable
{
    public DbContextOptions<JobPortalDbContext> DbOptions { get; } = new DbContextOptionsBuilder<JobPortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    public JobPortalDbContext Db { get; }
    public User Candidate { get; } = new() { Email = "candidate@example.invalid", Status = UserStatus.Active,
        ResumeStorageKey = "test-only-resume-key", ResumeFileName = "resume.pdf" };
    public User Other { get; } = new() { Email = "other@example.invalid", Status = UserStatus.Active };
    public User Admin { get; } = new() { Email = "admin@example.invalid", Status = UserStatus.Active };
    public Skill CodeSkill { get; } = new() { Name = "C#", NormalizedName = "C#" };
    public Skill ProductSkill { get; } = new() { Name = "Product Discovery", NormalizedName = "PRODUCT DISCOVERY" };
    public AcceleratorClock Clock { get; } = new();
    public AcceleratorOptions Settings { get; } = new() { Enabled = true, AIEnabled = true, UserConcurrency = 10 };
    public AcceleratorTestPricing Pricing { get; } = new();
    public AuditWriterTestDouble Audit { get; } = new();
    public AcceleratorTestProvider Provider { get; } = new();
    public AcceleratorStore Store { get; }
    public AcceleratorService Service { get; }
    public AcceleratorQuotaService Quota { get; }
    public AcceleratorPhase2Store Phase2Store { get; }
    public AcceleratorPhase2Service Phase2 { get; }
    public AcceleratorFixture()
    {
        Db = new(DbOptions);
        var candidateRole = new Role { Id = SystemRoleIds.Candidate, Name = "Candidate", NormalizedName = "CANDIDATE" };
        var adminRole = new Role { Id = SystemRoleIds.Administrator, Name = "Administrator", NormalizedName = "ADMINISTRATOR" };
        Candidate.Role = Other.Role = candidateRole; Candidate.RoleId = Other.RoleId = candidateRole.Id;
        Admin.Role = adminRole; Admin.RoleId = adminRole.Id;
        Candidate.ResumeUploadedAtUtc = Clock.Utc;
        Db.AddRange(candidateRole, adminRole, Candidate, Other, Admin, CodeSkill, ProductSkill); Db.SaveChanges();
        foreach (var feature in Enum.GetValues<AcceleratorFeature>()) Settings.Policies[feature] = new()
        { Provider = Provider.Name, Model = "deterministic-test", MaxInputTokens = 100, MaxOutputTokens = 100,
            MaxRequestBytes = 1000, InputInrPerMillionTokens = 1000, OutputInrPerMillionTokens = 1000 };
        Store = new(Db); Service = new(Store, Options.Create(Settings), Clock, Audit, Pricing);
        Quota = new(Store, Service, Options.Create(Settings), Clock, Audit, [Provider]);
        Phase2Store = new(Db); Phase2 = new(Store, Phase2Store, Service, Audit, Clock);
    }
    public PublishTaxonomyRequest Taxonomy(string version = "v1") => new(version,
        [new(CodeSkill.Id, "Build applications", "Engineering", 2, null, [], ["coding"], []),
         new(ProductSkill.Id, "Understand customer problems", "Product", 2, null, [], ["scenario"], [])],
        [new("backend", "Backend Developer", "Build reliable APIs", [new(CodeSkill.Id, 3)]),
         new("product", "Product Manager", "Discover valuable products", [new(ProductSkill.Id, 3)])]);
    public SaveAcceleratorProfileRequest Profile() => new(null, 2, "Candidate-supplied education", 300, true, [new(CodeSkill.Id, 2)]);
    public CreateCareerGoalRequest Goal(Guid roleId) => new(Guid.NewGuid(), roleId, 300, Clock.Utc.AddMonths(3));
    public static ReserveAcceleratorUsageRequest Reservation() => new(Guid.NewGuid(), AcceleratorFeature.Tutor, new string('A', 64), 100, 100);
    public async Task<Payment> Payment(Guid? userId = null)
    {
        var payment = new Payment { UserId = userId ?? Candidate.Id, PlanCode = AcceleratorOptions.PlanCode, Status = PaymentStatus.Paid,
            Provider = PaymentProvider.PhonePe, PaidAtUtc = Clock.Utc, BaseAmount = 278, TaxRate = 18, TaxAmount = 50.04m,
            Amount = 328.04m, CurrencyCode = "INR", ProviderOrderId = Guid.NewGuid().ToString("N"), ProviderPaymentId = Guid.NewGuid().ToString("N") };
        Db.Add(payment); await Db.SaveChangesAsync(); return payment;
    }
    public async Task<Payment> Activate()
    {
        var payment = await Payment(); await Service.ActivateVerifiedPaymentAsync(Candidate.Id, payment.Id, default); return payment;
    }
    public async Task Consume()
    {
        var row = await Quota.ReserveAsync(Candidate.Id, Reservation(), default);
        await Quota.MarkDispatchedAsync(Candidate.Id, row.Id, default);
        await Quota.CompleteAsync(Candidate.Id, row.Id, 80, 20, .2m, default);
    }
    public void Dispose() => Db.Dispose();
}
internal sealed class AcceleratorClock : TimeProvider
{
    public DateTime Utc { get; set; } = new(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc);
    public override DateTimeOffset GetUtcNow() => new(Utc);
}
internal sealed class AcceleratorTestPricing : IMembershipPlanProvider
{
    public decimal Gst { get; set; } = 18m;
    public decimal GetGstRate() => Gst;
    public MembershipPlan GetDefaultPlan() => new("Portal", 99, "INR", 30);
}
internal sealed class AcceleratorTestProvider : IAcceleratorAIProvider
{
    public string Name => "DeterministicTest";
    public bool IsAvailable => true;
    public int Calls { get; private set; }
    public Task<AcceleratorAIResult> ExecuteAsync(AcceleratorAIRequest request, CancellationToken ct)
    { Calls++; return Task.FromResult(new AcceleratorAIResult("{}", "fake-request", "test-v1", 80, 20, .1m)); }
}
