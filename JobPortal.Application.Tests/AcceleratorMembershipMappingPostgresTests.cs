using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorMembershipMappingPostgresTests
{
    private static AcceleratorService Access(JobPortalDbContext db) => new(new AcceleratorStore(db),Options.Create(new AcceleratorOptions { Enabled=true }),TimeProvider.System,new AuditWriterTestDouble(),new AcceleratorTestPricing());

    [MembershipPostgresFact]
    public async Task ConcurrentAccessAndQuotaRequestsRecoverOnePaymentBackedCycle()
    {
        Guid userId; Guid paymentId;
        await using(var db=AcceleratorPhase45PostgresTests.Open())
        {
            var roleId=await db.Users.Where(x=>x.Id==Guid.Parse("2a000000-0000-0000-0000-000000000001")).Select(x=>x.RoleId).SingleAsync();
            var start=DateTime.UtcNow.AddMinutes(-1);
            var email=$"membership-{Guid.NewGuid():N}@example.invalid";
            var user=new User { Email=email,NormalizedEmail=email.ToUpperInvariant(),RoleId=roleId,Status=UserStatus.Active };
            var membership=new Membership { User=user,UserId=user.Id,PlanCode=AcceleratorOptions.PlanCode,PlanName="CareerHarbor AI Career Accelerator",Status=MembershipStatus.Active,StartsAtUtc=start,EndsAtUtc=start.AddDays(30) };
            var payment=new Payment { User=user,UserId=user.Id,Membership=membership,MembershipId=membership.Id,PlanCode=AcceleratorOptions.PlanCode,PlanVersion="accelerator-v1",PlanName=membership.PlanName,
                DurationDays=30,Status=PaymentStatus.Paid,Provider=PaymentProvider.PhonePe,PaidAtUtc=start,ProviderOrderId=Guid.NewGuid().ToString("N"),ProviderPaymentId=Guid.NewGuid().ToString("N"),
                CurrencyCode="INR",BaseAmount=278,TaxRate=18,TaxAmount=50.04m,Amount=328.04m };
            db.AddRange(user,membership,payment,new Payment { User=user,UserId=user.Id,Membership=membership,MembershipId=membership.Id,PlanCode=AcceleratorOptions.PlanCode,Status=PaymentStatus.Pending,Amount=328.04m,CurrencyCode="INR" });
            await db.SaveChangesAsync(); userId=user.Id; paymentId=payment.Id;
        }
        await Task.WhenAll(Enumerable.Range(0,4).Select(async index=>
        {
            await using var db=AcceleratorPhase45PostgresTests.Open(); var access=Access(db);
            if(index%2==0) Assert.Equal("Active",(await access.EntitlementAsync(userId,default)).Status);
            else
            {
                var quota=new AcceleratorQuotaService(new AcceleratorStore(db),access,Options.Create(new AcceleratorOptions { Enabled=true }),TimeProvider.System,new AuditWriterTestDouble(),[]);
                await quota.ReserveOutreachAsync(userId,Guid.NewGuid(),new string('A',64),default);
            }
        }));
        await using var verify=AcceleratorPhase45PostgresTests.Open();
        var cycle=Assert.Single(await verify.AcceleratorBillingCycles.Include(x=>x.Allowances).Where(x=>x.UserId==userId).ToArrayAsync());
        Assert.Equal(paymentId,cycle.PaymentId); Assert.Equal(6,cycle.Allowances.Count);
        Assert.Equal(2,await verify.AcceleratorUsageReservations.CountAsync(x=>x.CycleId==cycle.Id));
        Assert.Equal(MembershipStatus.Active,Assert.Single(await new MembershipRepository(verify,TimeProvider.System).GetMembershipsForUserAsync(userId)).Status);
        Assert.Equal("Active",(await Access(verify).EntitlementAsync(userId,default)).Status);
    }
    private sealed class MembershipPostgresFactAttribute : FactAttribute
    {
        public MembershipPostgresFactAttribute()
        { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCELERATOR_PHASE45_TEST_POSTGRES"))) Skip="Requires the explicitly disposable Phase 4/5 PostgreSQL database."; }
    }
}
