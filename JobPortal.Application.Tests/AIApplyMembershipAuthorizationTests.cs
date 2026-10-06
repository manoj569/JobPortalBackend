using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIApplyMembershipAuthorizationTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("unrelated", AIApplyPlanTier.None)]
    [InlineData("standard", AIApplyPlanTier.Standard)]
    [InlineData("pro", AIApplyPlanTier.Pro)]
    [InlineData("both", AIApplyPlanTier.Pro)]
    [InlineData("both-reversed", AIApplyPlanTier.Pro)]
    [InlineData("expired-pro-active-standard", AIApplyPlanTier.Standard)]
    [InlineData("pending", AIApplyPlanTier.None)]
    [InlineData("cancelled", AIApplyPlanTier.None)]
    [InlineData("suspended", AIApplyPlanTier.None)]
    [InlineData("future", AIApplyPlanTier.None)]
    [InlineData("expired", AIApplyPlanTier.None)]
    [InlineData("deleted", AIApplyPlanTier.None)]
    [InlineData("other-user", AIApplyPlanTier.None)]
    [InlineData("renamed-standard", AIApplyPlanTier.Standard)]
    [InlineData("renamed-pro", AIApplyPlanTier.Pro)]
    [InlineData("starts-now", AIApplyPlanTier.Standard)]
    [InlineData("starts-after-now", AIApplyPlanTier.None)]
    [InlineData("ends-now", AIApplyPlanTier.None)]
    [InlineData("ends-after-now", AIApplyPlanTier.Standard)]
    [InlineData("no-expiry", AIApplyPlanTier.Standard)]
    [InlineData("unrelated-renamed-as-pro", AIApplyPlanTier.None)]
    public async Task MultiplePersistedPlansSelectOnlyEligibleAIApplyEntitlement(
        string scenario, AIApplyPlanTier expected)
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var userId = Guid.NewGuid();
        var standard = Membership(userId, "AIApply");
        var pro = Membership(userId, "AIApplyPro");
        // Actual persisted rows exercise the production repository's EF predicates and query filters.
        db.Memberships.AddRange(Membership(userId, "CareerHarborMembership"),
            Membership(userId, "ReferralContactAccess"));
        switch (scenario)
        {
            case "unrelated": break;
            case "unrelated-renamed-as-pro":
                foreach (var entry in db.ChangeTracker.Entries<Membership>()) entry.Entity.PlanName = "AI Apply Pro";
                break;
            case "pro": db.Add(pro); break;
            case "both": db.AddRange(standard, pro); break;
            case "both-reversed": db.AddRange(pro, standard); break;
            case "expired-pro-active-standard":
                pro.Status = MembershipStatus.Expired; pro.EndsAtUtc = Now.AddDays(-1);
                db.AddRange(pro, standard); break;
            default:
                switch (scenario)
                {
                    case "pending": standard.Status = MembershipStatus.Pending; break;
                    case "cancelled": standard.Status = MembershipStatus.Cancelled; break;
                    case "suspended": standard.Status = MembershipStatus.Suspended; break;
                    case "future": standard.StartsAtUtc = Now.AddDays(1); break;
                    case "expired": standard.Status = MembershipStatus.Expired; standard.EndsAtUtc = Now.AddDays(-1); break;
                    case "deleted": standard.IsDeleted = true; break;
                    case "other-user": standard.UserId = Guid.NewGuid(); break;
                    case "renamed-standard": standard.PlanName = "Renamed display name"; break;
                    case "renamed-pro": standard.PlanCode = "AIApplyPro"; standard.PlanName = "Renamed display name"; break;
                    case "starts-now": standard.StartsAtUtc = Now; break;
                    case "starts-after-now": standard.StartsAtUtc = Now.AddTicks(1); break;
                    case "ends-now": standard.EndsAtUtc = Now; break;
                    case "ends-after-now": standard.EndsAtUtc = Now.AddTicks(1); break;
                    case "no-expiry": standard.EndsAtUtc = null; break;
                }
                db.Add(standard); break;
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var clock = new FixedClock();
        var service = new AIApplyAuthorizationService(new MembershipRepository(db, clock), clock,
            Options.Create(new AIApplyOptions { StandardQueuePriority = 10, ProQueuePriority = 20 }));

        var access = await service.GetAccessAsync(userId);

        Assert.Equal(expected != AIApplyPlanTier.None, access.CanUseAIApply);
        Assert.Equal(expected == AIApplyPlanTier.Pro, access.CanUseAIApplyPro);
        Assert.Equal(expected != AIApplyPlanTier.None, access.SubscriptionActive);
        Assert.Equal(expected == AIApplyPlanTier.Pro ? 20 : expected == AIApplyPlanTier.Standard ? 10 : 0,
            access.ProcessingPriority);
        if (expected != AIApplyPlanTier.None)
        {
            Assert.Equal(expected, access.Tier);
            Assert.Equal(expected == AIApplyPlanTier.Pro ? "AIApplyPro" : "AIApply", access.PlanCode);
            Assert.False(access.SubscriptionExpired);
            await service.RequireAsync(userId);
        }
        else
        {
            var error = await Assert.ThrowsAsync<AppException>(() => service.RequireAsync(userId));
            Assert.Equal("ai_apply_subscription_required", error.Code);
        }
        if (expected == AIApplyPlanTier.Pro) await service.RequireAsync(userId, pro: true);
        else
        {
            var error = await Assert.ThrowsAsync<AppException>(() => service.RequireAsync(userId, pro: true));
            Assert.Equal("ai_apply_pro_required", error.Code);
        }
        if (scenario is "expired" or "ends-now") Assert.True(access.SubscriptionExpired);
        if (scenario is "deleted" or "other-user" or "unrelated" or "unrelated-renamed-as-pro")
            Assert.Null(access.PlanCode);
    }

    private static Membership Membership(Guid userId, string planCode) => new()
    {
        UserId = userId, PlanCode = planCode, PlanName = planCode,
        Status = MembershipStatus.Active, StartsAtUtc = Now.AddDays(-1), EndsAtUtc = Now.AddDays(1)
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
