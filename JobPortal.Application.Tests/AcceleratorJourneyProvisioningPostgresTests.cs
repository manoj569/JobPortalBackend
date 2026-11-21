using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorJourneyProvisioningPostgresTests
{
    private static AcceleratorService Access(JobPortalDbContext db) => new(new AcceleratorStore(db),Options.Create(new AcceleratorOptions { Enabled=true }),TimeProvider.System,new AuditWriterTestDouble(),new AcceleratorTestPricing());
    private static AcceleratorJourneyProvisioningService Service(JobPortalDbContext db) => new(new AcceleratorStore(db),new AcceleratorJourneyProvisioningStore(db),Access(db),new AuditWriterTestDouble(),TimeProvider.System,AcceleratorPhase45PostgresTests.Services(db).Summary);

    [ProvisioningPostgresFact]
    public async Task ConcurrentRequestsCreateOneSharedConfigurationAndIsolatedOwnedEnrollments()
    {
        Guid marketId; Guid roleId; Guid first; Guid second;
        await using(var db=AcceleratorPhase45PostgresTests.Open())
        {
            var original=await db.AcceleratorCareerRoles.SingleAsync(x=>x.Id==Guid.Parse("2a000000-0000-0000-0000-000000000004"));
            var suffix=Guid.NewGuid().ToString("N");
            var role=new AcceleratorCareerRole { Code="provision-"+suffix,Name="Provision "+suffix,TaxonomyId=original.TaxonomyId,Description="Disposable reviewed canonical role" };
            var market=new MarketRole { Name=role.Name,NormalizedName=role.Name.ToUpperInvariant(),Slug=role.Code };
            var competency=Guid.Parse("2a000000-0000-0000-0000-000000000005");
            db.AddRange(role,market,new AcceleratorRoleRequirement { RoleId=role.Id,TaxonomyId=role.TaxonomyId,CompetencyId=competency,ExpectedProficiency=3 },
                new AcceleratorQuestion { TaxonomyId=role.TaxonomyId,CompetencyId=competency,Version="prov-"+suffix,Type=AcceleratorQuestionType.MultipleChoice,
                    Difficulty=1,Weight=1,Prompt="Validated fixture",OptionsJson="[\"A\",\"B\"]",AnswerKeyJson="{\"answer\":\"A\"}",IsActive=true });
            await db.SaveChangesAsync(); marketId=market.Id; roleId=role.Id;
            first=await Candidate(db); second=await Candidate(db);
        }
        var key=Guid.NewGuid();
        var responses=await Task.WhenAll(Enumerable.Range(0,5).Select(async _=>
        { await using var db=AcceleratorPhase45PostgresTests.Open(); return await Service(db).ProvisionAsync(first,new(key,marketId),default); }));
        Assert.All(responses,x=>Assert.Equal("Ready",x.State));
        Assert.Single(responses.Select(x=>x.EnrollmentId).Distinct()); Assert.Single(responses.Select(x=>x.LearningPathId).Distinct());
        var provisioned=responses[0];
        await using(var db=AcceleratorPhase45PostgresTests.Open())
        {
            Assert.Equal(provisioned.EnrollmentId,(await Service(db).ProvisionAsync(first,new(Guid.NewGuid(),marketId),default)).EnrollmentId);
            var other=await Service(db).ProvisionAsync(second,new(key,marketId),default);
            Assert.Equal(provisioned.LearningPathId,other.LearningPathId); Assert.NotEqual(provisioned.EnrollmentId,other.EnrollmentId);
        }
        await using(var verify=AcceleratorPhase45PostgresTests.Open())
        {
            Assert.Equal(roleId,(await verify.MarketRoles.SingleAsync(x=>x.Id==marketId)).AcceleratorRoleId);
            Assert.Single(await verify.AcceleratorLearningPaths.Where(x=>x.CanonicalCareerRoleId==roleId).ToArrayAsync());
            Assert.Single(await verify.AcceleratorAssessmentTemplates.Where(x=>x.RoleId==roleId).ToArrayAsync());
            Assert.Equal(2,await verify.AcceleratorPathEnrollments.CountAsync(x=>x.LearningPathId==provisioned.LearningPathId));
            Assert.Single(await verify.AcceleratorJourneyProvisioningReceipts.Where(x=>x.UserId==first && x.RequestKey==key).ToArrayAsync());
            Assert.Null(await new AcceleratorJourneyStore(verify).OwnedAsync(second,provisioned.JourneyContextId!.Value,default));
            Assert.Empty(await verify.AcceleratorSkillEvidence.Where(x=>x.UserId==first || x.UserId==second).ToArrayAsync());
        }
        await using(var db=AcceleratorPhase45PostgresTests.Open())
        {
            var store=new AcceleratorStore(db); var phase2=new AcceleratorPhase2Store(db); var learning=new AcceleratorLearningPathStore(db); var journeys=new AcceleratorJourneyStore(db); var access=Access(db);
            var service=new AcceleratorPhase2Service(store,phase2,access,new AuditWriterTestDouble(),TimeProvider.System,new(store,journeys,learning,phase2,access),journeys,learning);
            Assert.NotEmpty((await service.StartJourneyAssessmentAsync(first,provisioned.JourneyContextId!.Value,Guid.NewGuid(),default)).Questions);
            await Assert.ThrowsAsync<NotFoundException>(()=>service.CurrentAttemptAsync(second,provisioned.JourneyContextId.Value,default));
        }
    }
    [ProvisioningPostgresFact]
    public async Task IndependentDemandCreatesOneNewRoleUnderConcurrentRequests()
    {
        Guid marketId; Guid candidate; string code;
        await using(var db=AcceleratorPhase45PostgresTests.Open())
        {
            var competency=await db.AcceleratorCompetencies.SingleAsync(x=>x.Id==Guid.Parse("2a000000-0000-0000-0000-000000000005"));
            code="demand-"+Guid.NewGuid().ToString("N");
            var market=new MarketRole { Name=code.Replace('-',' '),NormalizedName=code.Replace('-',' ').ToUpperInvariant(),Slug=code };
            var skill=await db.MarketSkills.SingleOrDefaultAsync(x=>x.SkillId==competency.SkillId);
            if(skill is null) { skill=new MarketSkill { SkillId=competency.SkillId,CompetencyId=competency.Id,Slug="demand-skill-"+Guid.NewGuid().ToString("N") }; db.Add(skill); }
            var companies=new[]{new Company { Name="Demand A "+code,NormalizedName="DEMAND A "+code.ToUpperInvariant() },new Company { Name="Demand B "+code,NormalizedName="DEMAND B "+code.ToUpperInvariant() }};
            foreach(var company in companies) { company.OwnerUserId=Guid.Parse("2a000000-0000-0000-0000-000000000001"); company.Slug="demand-company-"+Guid.NewGuid().ToString("N"); }
            db.AddRange(companies); db.Add(market);
            for(var index=0;index<3;index++)
            {
                var job=new Job { Title=market.Name,ReferenceNumber=code+index,Slug=code+index,Description="Disposable canonical demand evidence",ApplicationUrl="https://example.invalid/apply",
                    CompanyId=companies[index%2].Id,Company=companies[index%2],Status=JobStatus.Published,PublishedAtUtc=DateTime.UtcNow.AddDays(-1),ExpiresAtUtc=DateTime.UtcNow.AddDays(1) };
                db.AddRange(job,new MarketJobRole { MarketRoleId=market.Id,JobId=job.Id,Confidence=1,Source=MarketMappingSource.Administrator },
                    new MarketJobSkill { MarketSkillId=skill.Id,JobId=job.Id,Confidence=1,Source=MarketMappingSource.Administrator });
            }
            db.Add(new AcceleratorQuestion { TaxonomyId=competency.TaxonomyId,CompetencyId=competency.Id,Version=code,Type=AcceleratorQuestionType.MultipleChoice,Prompt="Reviewed fixture",OptionsJson="[\"A\",\"B\"]",AnswerKeyJson="{\"answer\":\"A\"}",Difficulty=1,Weight=1,IsActive=true });
            await db.SaveChangesAsync(); marketId=market.Id; candidate=await Candidate(db);
        }
        var requests=await Task.WhenAll(Enumerable.Range(0,3).Select(async _=>
        { await using var db=AcceleratorPhase45PostgresTests.Open(); return await Service(db).ProvisionAsync(candidate,new(Guid.NewGuid(),marketId),default); }));
        Assert.All(requests,x=>Assert.Equal("Ready",x.State)); Assert.Single(requests.Select(x=>x.LearningPathId).Distinct());
        await using var verify=AcceleratorPhase45PostgresTests.Open();
        var role=Assert.Single(await verify.AcceleratorCareerRoles.Where(x=>x.Code==code).ToArrayAsync());
        Assert.Equal(AcceleratorConfigurationSource.AutoProvisioned,role.ConfigurationSource);
        Assert.Single(await verify.AcceleratorRoleRequirements.Where(x=>x.RoleId==role.Id).ToArrayAsync());
        Assert.Single(await verify.AcceleratorPathEnrollments.Where(x=>x.UserId==candidate && x.LearningPathId==requests[0].LearningPathId).ToArrayAsync());
    }

    private static async Task<Guid> Candidate(JobPortalDbContext db)
    {
        var email=$"prov-{Guid.NewGuid():N}@example.invalid";
        var role=await db.Users.Where(x=>x.Id==Guid.Parse("2a000000-0000-0000-0000-000000000001")).Select(x=>x.RoleId).SingleAsync();
        var candidate=new User { RoleId=role,Status=UserStatus.Active,Email=email,NormalizedEmail=email.ToUpperInvariant() };
        var payment=new Payment { UserId=candidate.Id,User=candidate,PlanCode=AcceleratorOptions.PlanCode,PlanName="CareerHarbor AI Career Accelerator",PlanVersion="accelerator-v1",Status=PaymentStatus.Paid,Provider=PaymentProvider.PhonePe,
            PaidAtUtc=DateTime.UtcNow.AddMinutes(-1),DurationDays=30,ProviderPaymentId=Guid.NewGuid().ToString("N"),ProviderOrderId=Guid.NewGuid().ToString("N"),CurrencyCode="INR",BaseAmount=278,TaxRate=18,TaxAmount=50.04m,Amount=328.04m };
        db.AddRange(candidate,payment); await db.SaveChangesAsync();
        await Access(db).ActivateVerifiedPaymentAsync(candidate.Id,payment.Id,default); return candidate.Id;
    }
    private sealed class ProvisioningPostgresFactAttribute : FactAttribute
    {
        public ProvisioningPostgresFactAttribute()
        { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCELERATOR_PHASE45_TEST_POSTGRES"))) Skip="Requires the explicitly disposable local PostgreSQL fixture."; }
    }
}
