using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class HiringOutreachTests
{
    [Fact]
    public async Task SharedVerifiedContactsAreReusedAcrossCandidatesWithoutProviderOrDiscoveryCharge()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true;
        f.Job.Title="Trust & Safety Associate"; f.Job.Department="Operations"; f.Job.Location="Hyderabad";
        f.Hunter.Results=[Contact("talent@example.org","Talent Acquisition") with { Department="HR",Location="Hyderabad" },
            Contact("recruitment@example.org","Recruitment Lead") with { Department="HR",Location="India" },
            Contact("manager@example.org","Trust & Safety Manager") with { Department="Operations",Location="Hyderabad" }];
        await f.Db.SaveChangesAsync();
        var first=await f.Discover(); Assert.True(first.Usage.Charged);
        var another=new Job { Company=f.Job.Company,CompanyId=f.Job.CompanyId,Title="Trust & Safety Specialist",Department="Operations",Location="Hyderabad",
            Description="Trust and safety operations",Slug=Guid.NewGuid().ToString("N"),ReferenceNumber=Guid.NewGuid().ToString("N"),Status=JobStatus.Published,PublishedAtUtc=DateTime.UtcNow.AddDays(-1) };
        f.Db.Add(another); await f.Db.SaveChangesAsync();
        var second=await f.Service.DiscoverAsync(f.Other.Id,new(another.Id,Guid.NewGuid()),default);
        Assert.Equal("Found",second.State); Assert.Equal(3,second.Contacts.Count); Assert.False(second.Usage.Charged);
        Assert.Equal("CareerHarborCache",second.Source);
        Assert.Equal(1,second.Usage.Remaining); Assert.Equal(1,f.Hunter.Calls); Assert.NotEqual(first.SessionId,second.SessionId);
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Service.GetAsync(f.Other.Id,first.SessionId,default));
        Assert.All(await f.Db.Set<HiringContactIntelligence>().ToArrayAsync(),x=>Assert.Equal(1,x.SuccessfulReuseCount));
        Assert.Equal("CareerHarborCache",(await f.Db.Set<HiringOutreachSession>().SingleAsync(x=>x.Id==second.SessionId)).Provider);
    }
    [Fact]
    public async Task CacheReuseWorksWithoutProviderConfigurationOrRemainingDiscoveryCredit()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true; await f.Discover();
        f.Hunter.Available=false; f.Limits.PromotionalLifetimeDiscoveries=0;
        var result=await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.Equal("Found",result.State); Assert.False(result.Usage.Charged); Assert.Equal(1,f.Hunter.Calls);
    }
    [Fact]
    public async Task WrongCompanyIntelligenceIsNeverSharedEvenWhenDomainMatches()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true; await f.Discover();
        foreach(var row in await f.Db.Set<HiringContactIntelligence>().ToArrayAsync()) row.CompanyId=Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        f.Hunter.Results=[];
        var result=await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.Empty(result.Contacts); Assert.Equal(2,f.Hunter.Calls); Assert.False(result.Usage.Charged);
    }
    [Theory]
    [InlineData("Finance Recruiter","Finance",0)]
    [InlineData("Trust & Safety Manager","Operations",0)]
    [InlineData("Marketing Manager","Marketing",0)]
    [InlineData("Technical Recruiter","HR",1)]
    [InlineData("Talent Acquisition Partner","HR",1)]
    [InlineData("Human Resources Manager","HR",1)]
    [InlineData("HR Manager","HR",1)]
    [InlineData("Sales Engineer","Sales",0)]
    public void SpecialistMismatchIsRejectedButRelevantRecruitersAreEligible(string title,string department,int expected)
    {
        var score=HiringContactPolicy.Score(Contact("test@example.org",title) with { Department=department },new("Example","example.org","Software Engineer","","Hyderabad"));
        Assert.Equal(expected,score>0 ? 1 : 0);
    }
    [Theory]
    [InlineData("Trust & Safety Associate","Operations")]
    [InlineData("Full Stack Developer","Engineering")]
    [InlineData("Financial Analyst","Finance")]
    [InlineData("Data Scientist","Data")]
    [InlineData("Product Manager","Product")]
    [InlineData("Designer","Design")]
    public void JobFamilyInferenceUsesTitleBeforeDescription(string title,string family)=>Assert.Equal(family,HiringContactPolicy.Family(title,"software engineering finance"));
    [Fact]
    public void LocalRecruitersRankAboveNationalGlobalAndMismatchedCityRecruiters()
    {
        var context=new HiringDiscoveryContext("Example","example.org","Software Engineer","Engineering","Hyderabad");
        var local=Contact("local@example.org","Technical Recruiter") with { Location="Hyderabad" };
        var global=local with { Email="global@example.org",Location="Global" };
        var other=local with { Email="other@example.org",Location="Mumbai" };
        var ranked=HiringContactPolicy.Select(new[] { other,global,local },context,new());
        Assert.Equal(local.Email,ranked[0].Email); Assert.Equal(other.Email,ranked[1].Email); Assert.Equal(global.Email,ranked[2].Email);
    }
    [Fact]
    public async Task ExpiredContactsRequireNewVerifiedObservationAndRefreshWithoutDuplicates()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true; await f.Discover();
        foreach(var row in await f.Db.Set<HiringContactIntelligence>().ToArrayAsync()) row.ExpiresAtUtc=DateTime.UtcNow.AddDays(-1);
        await f.Db.SaveChangesAsync();
        var result=await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.True(result.Usage.Charged); Assert.Equal(2,f.Hunter.Calls);
        var rows=await f.Db.Set<HiringContactIntelligence>().ToArrayAsync(); Assert.Equal(2,rows.Length);
        Assert.All(rows,x=>Assert.True(x.ExpiresAtUtc>DateTime.UtcNow));
    }
    [Fact]
    public async Task LowerConfidenceObservationDoesNotOverwriteFreshStrongerMetadata()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true; await f.Discover();
        f.Limits.SharedContacts.MinimumContacts=4;
        f.Hunter.Results=f.Hunter.Results.Select(x=>x with { Confidence=60,Title="Recruiter" }).ToArray();
        await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.All(await f.Db.Set<HiringContactIntelligence>().ToArrayAsync(),x=>Assert.Equal(99,x.Confidence));
    }
    [Theory]
    [InlineData("role",99)]
    [InlineData("role",100)]
    [InlineData("department",99)]
    [InlineData("location",99)]
    public async Task WeakerOrIncompleteObservationDoesNotReplaceFreshRecruiterIntelligence(string loss,int confidence)
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true;
        f.Hunter.Results=f.Hunter.Results.Select(x=>x with { Location="Hyderabad" }).ToArray();
        await f.Discover();
        var original=await f.Db.Set<HiringContactIntelligence>().ToDictionaryAsync(x=>x.Id,x=>x.ProtectedContact);
        f.Limits.SharedContacts.MinimumContacts=4;
        f.Hunter.Results=f.Hunter.Results.Select(x=>x with { Confidence=confidence,Title=loss=="role" ? "Software Engineer" : x.Title,
            Department=loss=="department" ? "" : x.Department,Location=loss=="location" ? "" : x.Location }).ToArray();
        await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.All(await f.Db.Set<HiringContactIntelligence>().ToArrayAsync(),row=>
        {
            Assert.Equal(original[row.Id],row.ProtectedContact);
            Assert.Contains("RECRUIT",row.Title,StringComparison.Ordinal);
        });
    }
    [Fact]
    public async Task DuplicateNormalizedEmailsHaveOneEncryptedSharedIdentityAndNoPlaintextPersonData()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true;
        f.Hunter.Results=[Contact("alex@example.org","Technical Recruiter"),Contact(" ALEX@EXAMPLE.ORG ","Recruiter")];
        await f.Discover(); var row=await f.Db.Set<HiringContactIntelligence>().SingleAsync();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("alex@example.org"))),row.EmailHash);
        var serialized=JsonSerializer.Serialize(row);
        Assert.DoesNotContain("alex@example.org",serialized,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Alex Recruiter",serialized,StringComparison.Ordinal);
        Assert.DoesNotContain("licensed-reference",serialized,StringComparison.Ordinal);
    }
    [Fact]
    public async Task ConfirmedZeroUsableResultsReleaseCreditAndReplayNeverCallsProviderAgain()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true;
        f.Hunter.Results=[Contact("finance@example.org","Finance Recruiter") with { Department="Finance" }];
        var key=Guid.NewGuid(); var first=await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,key),default);
        Assert.Equal("NoContactsFound",first.State); Assert.Empty(first.Contacts); Assert.False(first.Usage.Charged); Assert.Equal(1,first.Usage.Remaining);
        var replay=await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,key),default);
        Assert.Equal(first.SessionId,replay.SessionId); Assert.False(replay.Usage.Charged); Assert.Equal(1,f.Hunter.Calls);
        Assert.Empty(await f.Db.Set<HiringContactIntelligence>().ToArrayAsync());
    }
    [Fact]
    public async Task LicenseGateDefaultsOffAndNeverPersistsCrossCandidateIntelligence()
    {
        using var f=new Fixture(); Assert.False(f.Limits.SharedContacts.Enabled); await f.Discover();
        Assert.Empty(await f.Db.Set<HiringContactIntelligence>().ToArrayAsync());
    }
    [Fact]
    public async Task ReuseNeverExtendsVerifiedLifetimeAndSessionExpiresWithItsSource()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true; await f.Discover();
        var expires=DateTime.UtcNow.AddHours(1);
        var rows=await f.Db.Set<HiringContactIntelligence>().ToArrayAsync();
        foreach(var row in rows) row.ExpiresAtUtc=expires;
        await f.Db.SaveChangesAsync(); var verified=rows.Select(x=>x.LastVerifiedAtUtc).ToArray();
        var result=await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.False(result.Usage.Charged);
        Assert.Equal(expires,(await f.Db.Set<HiringOutreachSession>().SingleAsync(x=>x.Id==result.SessionId)).ExpiresAtUtc);
        Assert.Equal(verified,rows.Select(x=>x.LastVerifiedAtUtc).ToArray()); Assert.All(rows,x=>Assert.Equal(expires,x.ExpiresAtUtc));
    }
    [Fact]
    public async Task SuppressedSharedIdentitiesAreNotResurrectedByFreshProviderResults()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true; await f.Discover();
        var rows=await f.Db.Set<HiringContactIntelligence>().ToArrayAsync();
        rows[0].IsActive=false; rows[1].IsDeleted=true; await f.Db.SaveChangesAsync();
        var result=await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.Empty(result.Contacts); Assert.False(result.Usage.Charged);
        Assert.False(rows[0].IsActive); Assert.True(rows[1].IsDeleted);
        Assert.Equal(2,await f.Db.Set<HiringContactIntelligence>().IgnoreQueryFilters().CountAsync());
    }
    [Fact]
    public void ExplicitDepartmentPrecedesIncidentalDescriptionHints()=>
        Assert.Equal("Engineering",HiringContactPolicy.Family("Specialist","Engineering","financial operations"));
    [Fact]
    public void UnknownJobFamilyDoesNotAuthorizeSpecialistRecruitersByCompanyAlone()
    {
        var context=new HiringDiscoveryContext("Example","example.org","Unclassified Position","");
        Assert.Equal(0,HiringContactPolicy.Score(Contact("finance@example.org","Finance Recruiter"),context));
        Assert.True(HiringContactPolicy.Score(Contact("general@example.org","Recruiter") with { Department="HR" },context)>0);
    }
    [Fact]
    public async Task PersonalMailboxDomainIsNotACompanyDiscoveryDomain()
    {
        using var f=new Fixture(); f.Job.Company.WebsiteUrl="https://gmail.com"; await f.Db.SaveChangesAsync();
        var result=await f.Discover(); Assert.Equal("CompanyDomainUnresolved",result.FailureCode); Assert.False(result.Usage.Charged); Assert.Equal(0,f.Hunter.Calls);
    }
    [Fact]
    public async Task ReleasedCreditsAndSoftDeletesCannotBypassDailyProviderAttemptBudget()
    {
        using var f=new Fixture(); f.Hunter.Results=[];
        for(var i=0;i<f.Limits.DailyDiscoveries+1;i++)
        {
            var job=new Job { Company=f.Job.Company,CompanyId=f.Job.CompanyId,Title="Backend Engineer",Description="Software",Slug=Guid.NewGuid().ToString("N"),ReferenceNumber=Guid.NewGuid().ToString("N"),Status=JobStatus.Published,PublishedAtUtc=DateTime.UtcNow.AddDays(-1) };
            f.Db.Add(job); await f.Db.SaveChangesAsync();
            var result=await f.Service.DiscoverAsync(f.User.Id,new(job.Id,Guid.NewGuid()),default);
            Assert.False(result.Usage.Charged); Assert.Equal(1,result.Usage.Remaining);
            if(i==f.Limits.DailyDiscoveries) Assert.Equal("DiscoveryProviderDailyLimit",result.FailureCode);
            foreach(var session in await f.Db.Set<HiringOutreachSession>().ToArrayAsync()) session.IsDeleted=true;
            await f.Db.SaveChangesAsync();
        }
        Assert.Equal(f.Limits.DailyDiscoveries,f.Hunter.Calls);
    }
    [Fact]
    public async Task ConcurrentDifferentCandidatesUseDurableClaimThenReuseCommittedCache()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true;
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Hunter.BeforeReturn=async ()=> { entered.SetResult(); await finish.Task; };
        await using var firstDb=new JobPortalDbContext(f.DbOptions);
        var first=f.For(firstDb).DiscoverAsync(f.User.Id,new(f.Job.Id,Guid.NewGuid()),default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            await using var secondDb=new JobPortalDbContext(f.DbOptions);
            var error=await Assert.ThrowsAsync<AppException>(()=>f.For(secondDb).DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default));
            Assert.Contains("in progress",error.Message,StringComparison.Ordinal);
        }
        finally { finish.TrySetResult(); }
        await first; f.Hunter.BeforeReturn=null;
        await using var retryDb=new JobPortalDbContext(f.DbOptions);
        var retry=await f.For(retryDb).DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.False(retry.Usage.Charged); Assert.Equal(1,f.Hunter.Calls);
    }
    [Fact]
    public async Task UnknownOutcomeRetainsReservationAndScopeCooldownWithoutBlindRetry()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true; f.Hunter.Timeout=true;
        var first=await f.Discover(); Assert.True(first.Usage.Charged); Assert.Equal(0,first.Usage.Remaining);
        var lease=await f.Db.Set<HiringContactDiscoveryLease>().SingleAsync(); Assert.True(lease.ExpiresAtUtc>DateTime.UtcNow.AddMinutes(4));
        await Assert.ThrowsAsync<AppException>(()=>f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default));
        Assert.Equal(1,f.Hunter.Calls);
    }
    [Fact]
    public async Task ExpiredClaimCanBeRecoveredAfterRestart()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true; f.Hunter.Timeout=true; await f.Discover();
        var lease=await f.Db.Set<HiringContactDiscoveryLease>().SingleAsync(); lease.ExpiresAtUtc=DateTime.UtcNow.AddSeconds(-1); await f.Db.SaveChangesAsync();
        f.Hunter.Timeout=false;
        await using var restarted=new JobPortalDbContext(f.DbOptions);
        var result=await f.For(restarted).DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.Equal("Found",result.State); Assert.Equal(2,f.Hunter.Calls);
    }
    [HiringPostgresFact]
    public async Task PostgreSqlSharedIdentityAndCrossCandidateReuseSurviveSeparateContexts()
    {
        var connection=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HIRING_OUTREACH_TEST_POSTGRES"));
        Assert.Equal("127.0.0.1",connection.Host); Assert.Equal(55439,connection.Port); Assert.Equal("hiring_outreach_verify",connection.Database);
        using var f=new Fixture(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(connection.ConnectionString,p=>p.EnableRetryOnFailure(3)).Options);
        var domain="shared-"+f.Job.CompanyId.ToString("N")+".example.org";
        f.Job.Company.WebsiteUrl="https://"+domain;
        f.Hunter.Results=f.Hunter.Results.Select(x=>x with { Email=x.Email.Replace("example.org",domain,StringComparison.Ordinal) }).ToArray();
        await f.Db.SaveChangesAsync();
        f.Limits.SharedContacts.Enabled=true;
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Hunter.BeforeReturn=async ()=> { entered.SetResult(); await finish.Task; };
        await using var firstDb=new JobPortalDbContext(f.DbOptions);
        var first=f.For(firstDb).DiscoverAsync(f.User.Id,new(f.Job.Id,Guid.NewGuid()),default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            await using var competing=new JobPortalDbContext(f.DbOptions);
            await Assert.ThrowsAsync<AppException>(()=>f.For(competing).DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default));
        }
        finally { finish.TrySetResult(); }
        await first; f.Hunter.BeforeReturn=null;
        await using var restarted=new JobPortalDbContext(f.DbOptions);
        var cached=await f.For(restarted).DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.False(cached.Usage.Charged); Assert.Equal(1,f.Hunter.Calls);
        var rows=await restarted.Set<HiringContactIntelligence>().Where(x=>x.CompanyId==f.Job.CompanyId).ToArrayAsync();
        Assert.Equal(2,rows.Length); Assert.Equal(2,rows.Select(x=>x.EmailHash).Distinct().Count());
        await using var duplicate=new JobPortalDbContext(f.DbOptions);
        duplicate.Add(new HiringContactIntelligence { CompanyId=f.Job.CompanyId,CompanyDomain=domain,EmailHash=rows[0].EmailHash });
        var error=await Assert.ThrowsAsync<DbUpdateException>(()=>duplicate.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation,Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
