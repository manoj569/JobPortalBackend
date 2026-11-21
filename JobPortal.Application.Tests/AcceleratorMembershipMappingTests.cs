using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorMembershipMappingTests
{
    private static async Task<Membership> Membership(AcceleratorFixture f)
    {
        f.Clock.Utc=new DateTime(2026,11,21,0,0,0,DateTimeKind.Utc);
        var membership=new Membership { UserId=f.Candidate.Id,PlanCode=AcceleratorOptions.PlanCode,PlanName="CareerHarbor AI Career Accelerator",
            Status=MembershipStatus.Active,StartsAtUtc=f.Clock.Utc,EndsAtUtc=f.Clock.Utc.AddMonths(1) };
        f.Db.Add(membership);
        var payment=await f.Payment(); payment.MembershipId=membership.Id; payment.DurationDays=30;
        await f.Db.SaveChangesAsync(); return membership;
    }

    [Fact]
    public async Task ActiveMembershipRepairsVerifiedPaymentProjectionAndPendingCheckoutDoesNotRevokeAccess()
    {
        using var f=new AcceleratorFixture(); var membership=await Membership(f);
        f.Db.Add(new Payment { UserId=f.Candidate.Id,MembershipId=membership.Id,PlanCode=AcceleratorOptions.PlanCode,Status=PaymentStatus.Pending,Amount=328.04m });
        await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Db.AcceleratorBillingCycles.ToArrayAsync());
        var entitlement=await f.Service.EntitlementAsync(f.Candidate.Id,default);
        Assert.Equal("Active",entitlement.Status); Assert.Equal(membership.StartsAtUtc,entitlement.StartsAtUtc); Assert.Equal(membership.EndsAtUtc,entitlement.EndsAtUtc);
        Assert.Equal(900,entitlement.Usage.Single(x=>x.Feature==AcceleratorFeature.Tutor).Remaining);
        Assert.Equal(MembershipStatus.Active,Assert.Single(await new MembershipRepository(f.Db,f.Clock).GetMembershipsForUserAsync(f.Candidate.Id)).Status);
        await f.Service.EntitlementAsync(f.Candidate.Id,default);
        Assert.Single(await f.Db.AcceleratorBillingCycles.ToArrayAsync());
        var reservation=await f.Quota.ReserveAsync(f.Candidate.Id,AcceleratorFixture.Reservation(),default);
        Assert.Equal(AcceleratorUsageState.Reserved,reservation.State);
        Assert.Single(await f.Db.Payments.Where(x=>x.Status==PaymentStatus.Pending).ToArrayAsync());
        Assert.Equal("NotSubscribed",(await f.Service.EntitlementAsync(f.Other.Id,default)).Status);
    }

    [Fact]
    public async Task QuotaRepairsMissingProjectionWithoutDashboardRequest()
    {
        using var f=new AcceleratorFixture(); await Membership(f);
        await f.Quota.ReserveOutreachAsync(f.Candidate.Id,Guid.NewGuid(),new string('A',64),default);
        Assert.Single(await f.Db.AcceleratorBillingCycles.ToArrayAsync());
        foreach(var feature in new[]{AcceleratorFeature.Tutor,AcceleratorFeature.Practice,AcceleratorFeature.Roadmap,AcceleratorFeature.Review,AcceleratorFeature.Interview})
            Assert.Equal(AcceleratorUsageState.Reserved,(await f.Quota.ReserveAsync(f.Candidate.Id,AcceleratorFixture.Reservation() with { Feature=feature },default)).State);
    }

    [Theory]
    [InlineData(-1,"Expired",MembershipStatus.Expired)]
    [InlineData(1,"Pending",MembershipStatus.Pending)]
    public async Task MembershipAndAcceleratorApplySameUtcBoundaries(int direction,string expected,MembershipStatus membershipStatus)
    {
        using var f=new AcceleratorFixture(); var membership=await Membership(f);
        if(direction<0) membership.EndsAtUtc=f.Clock.Utc;
        else { membership.StartsAtUtc=f.Clock.Utc.AddTicks(1); membership.EndsAtUtc=f.Clock.Utc.AddMonths(1); }
        await f.Db.SaveChangesAsync();
        Assert.Equal(expected,(await f.Service.EntitlementAsync(f.Candidate.Id,default)).Status);
        Assert.Equal(membershipStatus,Assert.Single(await new MembershipRepository(f.Db,f.Clock).GetMembershipsForUserAsync(f.Candidate.Id)).Status);
        var error=await Assert.ThrowsAsync<AppException>(()=>f.Quota.ReserveAsync(f.Candidate.Id,AcceleratorFixture.Reservation(),default));
        Assert.Equal(403,error.StatusCode); Assert.Empty(await f.Db.AcceleratorUsageReservations.ToArrayAsync());
    }

    [Fact]
    public async Task ActiveLabelAloneDoesNotInventVerifiedPaymentOrQuota()
    {
        using var f=new AcceleratorFixture(); var membership=await Membership(f);
        var payment=await f.Db.Payments.SingleAsync(); payment.Status=PaymentStatus.Pending; await f.Db.SaveChangesAsync();
        Assert.Equal("ProvisioningRequired",(await f.Service.EntitlementAsync(f.Candidate.Id,default)).Status);
        await Assert.ThrowsAsync<AppException>(()=>f.Quota.ReserveAsync(f.Candidate.Id,AcceleratorFixture.Reservation(),default));
        Assert.Empty(await f.Db.AcceleratorBillingCycles.ToArrayAsync());
        // A rejected quota write intentionally clears tracking; reload before changing persisted state.
        membership=await f.Db.Memberships.SingleAsync(x=>x.Id==membership.Id);
        membership.PlanCode="career_accelerator"; await f.Db.SaveChangesAsync();
        Assert.Equal("NotSubscribed",(await f.Service.EntitlementAsync(f.Candidate.Id,default)).Status);
        membership.PlanCode=AcceleratorOptions.PlanCode; membership.Status=MembershipStatus.Pending;
        await f.Db.SaveChangesAsync();
        Assert.Equal("NotSubscribed",(await f.Service.EntitlementAsync(f.Candidate.Id,default)).Status);
    }

    [Fact]
    public async Task SuspendedMembershipCannotUseAnOtherwiseActiveQuotaCycle()
    {
        using var f=new AcceleratorFixture(); var membership=await Membership(f);
        Assert.Equal("Active",(await f.Service.EntitlementAsync(f.Candidate.Id,default)).Status);
        membership.Status=MembershipStatus.Suspended; await f.Db.SaveChangesAsync();
        Assert.Equal("Suspended",(await f.Service.EntitlementAsync(f.Candidate.Id,default)).Status);
        await Assert.ThrowsAsync<AppException>(()=>f.Quota.ReserveOutreachAsync(f.Candidate.Id,Guid.NewGuid(),new string('A',64),default));
    }

    [Fact]
    public async Task FullStackCanonicalMappingLearningPathAssessmentAndRoadmapProduceUsableOwnedJourney()
    {
        using var f=new AcceleratorFixture(); await Membership(f);
        var definition=f.Taxonomy();
        var taxonomy=await f.Service.PublishTaxonomyAsync(f.Admin.Id,definition with {
            Roles=[new("full-stack-developer","Full Stack Developer","Reviewed full stack role",[new(f.CodeSkill.Id,3)])]
        },default);
        var role=Assert.Single(taxonomy.Roles); var competency=taxonomy.Skills.Single(x=>x.SkillId==f.CodeSkill.Id);
        await f.Phase2.UpsertQuestionAsync(f.Admin.Id,new(taxonomy.Id,competency.Id,"full-stack-v1",AcceleratorQuestionType.MultipleChoice,1,1,"Which HTTP method retrieves a resource?","[\"GET\",\"POST\"]","{\"answer\":\"GET\"}","{}","GET retrieves a resource.",true),default);
        var market=new MarketRole { Name="Full Stack Developer",NormalizedName="FULL STACK DEVELOPER",Slug="full-stack-test" };
        f.Db.Add(market); await f.Db.SaveChangesAsync();
        var summary=AcceleratorJourneySummaryTests.Service(f);
        Assert.Equal("ProvisioningRequired",(await summary.GetAsync(f.Candidate.Id,market.Id,null,default)).State);
        Assert.Equal(AcceleratorNextAction.ProvisionJourney,(await summary.GetAsync(f.Candidate.Id,market.Id,null,default)).NextAction);
        var service=new MarketIntelligenceService(new MarketIntelligenceStore(f.Db),f.Service,f.Audit,new UnitOfWork(f.Db),Options.Create(new MarketIntelligenceOptions()),f.Clock,NullLogger<MarketIntelligenceService>.Instance);
        var request=new MarketMapAcceleratorRoleRequest(role.Id,market.Revision,"Canonical role and taxonomy reviewed");
        await Assert.ThrowsAsync<AppException>(()=>service.MapAcceleratorRoleAsync(f.Candidate.Id,market.Id,request,default));
        await Assert.ThrowsAsync<ConflictException>(()=>service.MapAcceleratorRoleAsync(f.Admin.Id,market.Id,request with { ExpectedRevision=Guid.NewGuid() },default));
        var mapped=await service.MapAcceleratorRoleAsync(f.Admin.Id,market.Id,request,default);
        Assert.Equal(mapped.Revision,(await service.MapAcceleratorRoleAsync(f.Admin.Id,market.Id,request,default)).Revision);
        Assert.Equal("ProvisioningRequired",(await summary.GetAsync(f.Candidate.Id,market.Id,null,default)).State);
        var templateId=await f.Phase2.UpsertTemplateAsync(f.Admin.Id,new(role.Id,"full-stack-v1",30,10,true),default);
        var path=new AcceleratorLearningPath { Type=AcceleratorLearningPathType.Career,CanonicalCareerRoleId=role.Id,Slug="full-stack-test",Title="Full Stack Developer",Status=AcceleratorLearningCatalogStatus.Published,Difficulty=1 };
        var module=new AcceleratorLearningModule { LearningPathId=path.Id,Title="HTTP fundamentals",Sequence=1,Status=AcceleratorLearningCatalogStatus.Published,Difficulty=1,IsRequired=true,EstimatedMinutes=15 };
        f.Db.AddRange(path,module,new AcceleratorLesson { ModuleId=module.Id,Title="Resource retrieval",Content="HTTP GET retrieves a resource without changing it.",Type=AcceleratorLessonType.Article,IsRequired=true,Sequence=1,EstimatedMinutes=15,Status=AcceleratorLearningCatalogStatus.Published },
            new AcceleratorLearningPathAssessment { LearningPathId=path.Id,AssessmentTemplateId=templateId,Purpose=AcceleratorLearningPathAssessmentPurpose.Baseline });
        await f.Db.SaveChangesAsync();
        Assert.Equal("ProvisioningRequired",(await summary.GetAsync(f.Candidate.Id,market.Id,null,default)).State);
        var learning=new AcceleratorLearningPathService(f.Store,new AcceleratorLearningPathStore(f.Db),f.Service,f.Clock,f.Audit);
        var enrollment=await learning.EnrollAsync(f.Candidate.Id,path.Id,new(Guid.NewGuid(),1,3,300,f.Clock.Utc.AddMonths(3)),default);
        var journey=await summary.GetAsync(f.Candidate.Id,market.Id,null,default);
        Assert.Equal("Active",journey.Entitlement.Status); Assert.Equal("Ready",journey.State); Assert.Equal(path.Id,journey.LearningPathId); Assert.Equal(enrollment.Id,journey.EnrollmentId);
        var attempt=await f.Phase2.StartJourneyAssessmentAsync(f.Candidate.Id,journey.JourneyContextId!.Value,Guid.NewGuid(),default);
        attempt=await f.Phase2.SubmitAnswerAsync(f.Candidate.Id,attempt.Id,new(attempt.Questions[0].Id,"{\"answer\":\"POST\"}"),default);
        await f.Phase2.FinalizeAttemptAsync(f.Candidate.Id,attempt.Id,new(attempt.Revision),default);
        var roadmap=await f.Phase2.GenerateEnrollmentRoadmapAsync(f.Candidate.Id,enrollment.Id,Guid.NewGuid(),default);
        Assert.NotEmpty(roadmap.Nodes); Assert.Empty(await f.Db.AcceleratorCareerGoals.ToArrayAsync());
        Assert.Equal("EnrollmentRequired",(await summary.GetAsync(f.Other.Id,market.Id,null,default)).State);
    }
}
