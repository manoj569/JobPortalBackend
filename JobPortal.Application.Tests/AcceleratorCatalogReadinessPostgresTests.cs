using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorCatalogReadinessPostgresTests
{
    private static JobPortalDbContext Open()
    {
        var cs=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ACCELERATOR_READINESS_TEST_POSTGRES"));
        Assert.Equal("127.0.0.1",cs.Host); Assert.Equal(55439,cs.Port); Assert.Equal("accelerator_readiness_verify",cs.Database);
        return new(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(cs.ConnectionString,p=>{p.MigrationsAssembly(typeof(PostgresMigrationMarker).Assembly.FullName);p.EnableRetryOnFailure(3);}).Options);
    }
    private static AcceleratorService Access(JobPortalDbContext db) => new(new AcceleratorStore(db),Options.Create(new AcceleratorOptions { Enabled=true }),TimeProvider.System,new AuditWriterTestDouble(),new AcceleratorTestPricing());
    private static AcceleratorCatalogReadinessService Service(JobPortalDbContext db) => new(new AcceleratorStore(db),new AcceleratorCatalogReadinessStore(db),new AcceleratorJourneyProvisioningStore(db),Access(db),new AuditWriterTestDouble(),TimeProvider.System);
    [ReadinessPostgresFact]
    public async Task ConcurrentBootstrapFillsSharedPoolsOnceAndFullStackCanProvision()
    {
        Guid adminId; Guid roleId; Guid taxId; Guid marketId;
        await using(var db=Open())
        {
            var adminRole=await db.Roles.SingleOrDefaultAsync(x=>x.Name=="Administrator");
            if(adminRole is null) { adminRole=new Role { Name="Administrator",NormalizedName="ADMINISTRATOR" }; db.Add(adminRole); }
            var suffix=Guid.NewGuid().ToString("N"); var email=$"readiness-admin-{suffix}@example.invalid";
            var admin=new User { Email=email,NormalizedEmail=email.ToUpperInvariant(),RoleId=adminRole.Id,Status=UserStatus.Active }; db.Add(admin); await db.SaveChangesAsync(); adminId=admin.Id;
            var skills=new List<Skill>();
            foreach(var name in AcceleratorCatalogReadinessTests.CoreSkills.Append("Data Structures"))
            {
                var normalized=name.ToUpperInvariant(); var skill=await db.Skills.SingleOrDefaultAsync(x=>x.NormalizedName==normalized);
                if(skill is null) { skill=new Skill { Name=name,NormalizedName=normalized }; db.Add(skill); } skills.Add(skill);
            }
            await db.SaveChangesAsync();
            var tax=await Access(db).PublishTaxonomyAsync(adminId,new("readiness-"+suffix,skills.Select(s=>new CompetencyDefinition(s.Id,"Canonical competency","Engineering",1,null,[],["mcq"],[])).ToArray(),
                [new("full-stack-developer","Full Stack Developer","Core full stack baseline",skills.Select(s=>new RoleRequirementDefinition(s.Id,3)).ToArray())]),default);
            taxId=tax.Id; roleId=tax.Roles[0].Id;
            var market=new MarketRole { Name="Readiness full stack "+suffix,NormalizedName="READINESS FULL STACK "+suffix.ToUpperInvariant(),Slug="readiness-"+suffix,AcceleratorRoleId=roleId }; db.Add(market); await db.SaveChangesAsync(); marketId=market.Id;
            var before=await Service(db).RoleAsync(adminId,roleId,default); Assert.Equal(0,before.CoveredCompetencyCount); Assert.Equal(11,before.RequiredCompetencyCount);
        }
        var seeded=await Task.WhenAll(Enumerable.Range(0,3).Select(async _=>{ await using var db=Open(); return await Service(db).BootstrapAsync(adminId,new(taxId,true),default); }));
        Assert.Equal(22,seeded.Sum(x=>x.AddedQuestions));
        await using var verify=Open();
        var after=await Service(verify).RoleAsync(adminId,roleId,default);
        Assert.Equal(100,after.ObjectiveQuestionCoveragePercent); Assert.Equal(22,after.EligibleObjectiveQuestionCount); Assert.True(after.AssessmentReady);
        var dataStructuresId=await verify.AcceleratorCompetencies.Where(x=>x.TaxonomyId==taxId && x.SkillNameSnapshot=="Data Structures").Select(x=>x.Id).SingleAsync();
        Assert.Equal(2,await verify.AcceleratorQuestions.CountAsync(x=>x.CompetencyId==dataStructuresId));
        Assert.Single((await Service(verify).RolesAsync(adminId,new(Code:"full-stack-developer",TaxonomyId:taxId),default)).Items);
        var candidateRole=await verify.Roles.SingleAsync(x=>x.Name=="Candidate"); var candidateEmail=$"readiness-candidate-{Guid.NewGuid():N}@example.invalid";
        var candidate=new User { Email=candidateEmail,NormalizedEmail=candidateEmail.ToUpperInvariant(),RoleId=candidateRole.Id,Status=UserStatus.Active };
        var payment=new Payment { User=candidate,UserId=candidate.Id,PlanCode=AcceleratorOptions.PlanCode,PlanName="CareerHarbor AI Career Accelerator",PlanVersion="accelerator-v1",DurationDays=30,
            Status=PaymentStatus.Paid,Provider=PaymentProvider.PhonePe,PaidAtUtc=DateTime.UtcNow.AddMinutes(-1),ProviderOrderId=Guid.NewGuid().ToString("N"),ProviderPaymentId=Guid.NewGuid().ToString("N"),CurrencyCode="INR",BaseAmount=278,TaxRate=18,TaxAmount=50.04m,Amount=328.04m };
        verify.AddRange(candidate,payment); await verify.SaveChangesAsync(); await Access(verify).ActivateVerifiedPaymentAsync(candidate.Id,payment.Id,default);
        var writes=new AcceleratorStore(verify); var phase2=new AcceleratorPhase2Store(verify); var journeys=new AcceleratorJourneyStore(verify); var learning=new AcceleratorLearningPathStore(verify); var access=Access(verify);
        var contexts=new AcceleratorJourneyContextService(writes,journeys,learning,phase2,access); var work=new AcceleratorJourneyWorkService(contexts,journeys,writes,learning);
        var summary=new AcceleratorJourneySummaryService(new AcceleratorJourneySummaryStore(verify),work,access,TimeProvider.System,Options.Create(new AcceleratorJourneySummaryOptions()),new AcceleratorJourneyProvisioningStore(verify));
        Assert.Equal("ProvisioningRequired",(await summary.GetAsync(candidate.Id,marketId,null,default)).State);
        var provision=new AcceleratorJourneyProvisioningService(writes,new AcceleratorJourneyProvisioningStore(verify),access,new AuditWriterTestDouble(),TimeProvider.System,summary);
        var result=await provision.ProvisionAsync(candidate.Id,new(Guid.NewGuid(),marketId),default); Assert.Equal("Ready",result.State);
        Assert.True((await summary.GetAsync(candidate.Id,null,result.JourneyContextId,default)).Assessment!.CanStart);
        Assert.Equal(0,(await Service(verify).BootstrapAsync(adminId,new(taxId,true),default)).AddedQuestions);
    }
    private sealed class ReadinessPostgresFactAttribute : FactAttribute
    {
        public ReadinessPostgresFactAttribute()
        { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCELERATOR_READINESS_TEST_POSTGRES"))) Skip="Requires the explicitly disposable readiness PostgreSQL clone."; }
    }
}
