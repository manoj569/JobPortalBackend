using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class HiringOutreachTests
{
    [Theory]
    [InlineData("Coresignal")]
    [InlineData("Coresignal+Hunter")]
    public async Task CoresignalDisplayGateAlsoProtectsPersistenceEvenIfAnAdapterReturnsForbiddenData(string provider)
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true;
        f.CoresignalSettings.Enabled=true; f.CoresignalSettings.AllowCandidateDisplay=false;
        f.DiscoveryOverride=new ResultProvider(new([Contact("core@example.org","Recruiter") with { Provider=provider }]));
        var result=await f.Discover(); Assert.Empty(result.Contacts); Assert.False(result.Usage.Charged);
        Assert.Empty(await f.Db.Set<HiringOutreachContact>().ToArrayAsync()); Assert.Empty(await f.Db.Set<HiringContactIntelligence>().ToArrayAsync());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoresignalSharedPersistenceRequiresSeparateApprovalAndRevocationHidesHistory(bool shareApproved)
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true;
        f.CoresignalSettings.Enabled=true; f.CoresignalSettings.AllowCandidateDisplay=true; f.CoresignalSettings.AllowSharedPersistence=shareApproved;
        var provider=new ResultProvider(new([Contact("core@example.org","Recruiter") with { Provider="Coresignal+Hunter",Source="EmailFinder" },
            Contact("core2@example.org","Recruiter") with { Provider="Coresignal",Source="EmployeeEnrichment" }])); f.DiscoveryOverride=provider;
        var result=await f.Discover(); Assert.Equal(2,result.Contacts.Count); Assert.True(result.Usage.Charged);
        Assert.Equal(shareApproved ? 2 : 0,await f.Db.Set<HiringContactIntelligence>().CountAsync());
        Assert.Single(await f.Db.Set<HiringOutreachSession>().Where(x=>x.UsageState=="Consumed").ToArrayAsync());
        var replay=await f.Service.GetAsync(f.User.Id,result.SessionId,default); Assert.Equal(2,replay.Contacts.Count); Assert.Equal(1,provider.Calls);
        f.CoresignalSettings.AllowCandidateDisplay=false;
        Assert.Empty((await f.Service.GetAsync(f.User.Id,result.SessionId,default)).Contacts);
        var other=await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.Empty(other.Contacts); Assert.NotEqual("CareerHarborCache",other.Source);
    }
}
