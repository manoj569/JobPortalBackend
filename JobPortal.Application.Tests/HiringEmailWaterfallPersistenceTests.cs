using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class HiringOutreachTests
{
    [Theory]
    [InlineData("Prospeo")]
    [InlineData("Snov")]
    public async Task EmailProviderNeedsIndependentDisplayAndSharingApprovalAndChargesOnlyOnce(string name)
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true;
        f.CoresignalSettings.Enabled=true; f.CoresignalSettings.AllowCandidateDisplay=true; f.CoresignalSettings.AllowSharedPersistence=true;
        HiringEmailProviderOptions settings=name=="Prospeo" ? f.ProspeoSettings : f.SnovSettings; settings.Enabled=true;
        var provider=new ResultProvider(new([Contact("email@example.org","Recruiter") with { Provider="Coresignal+"+name,Source="EmailFinder" }])); f.DiscoveryOverride=provider;
        var blocked=await f.Discover(); Assert.Empty(blocked.Contacts); Assert.False(blocked.Usage.Charged);
        settings.AllowCandidateDisplay=true;
        var accepted=await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.Single(accepted.Contacts); Assert.True(accepted.Usage.Charged); Assert.Empty(await f.Db.Set<HiringContactIntelligence>().ToArrayAsync());
        var calls=provider.Calls; var replay=await f.Service.GetAsync(f.Other.Id,accepted.SessionId,default); Assert.Single(replay.Contacts); Assert.Equal(calls,provider.Calls);
        Assert.Equal(1,await f.Db.Set<HiringOutreachSession>().CountAsync(x=>x.UsageState=="Consumed"));
        settings.AllowCandidateDisplay=false; Assert.Empty((await f.Service.GetAsync(f.Other.Id,accepted.SessionId,default)).Contacts);
    }
}
