using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class HiringOutreachTests
{
    private static readonly string[] MultiProviderUnknownOutcome=["HunterOutcomeUnknown"];
    private static readonly string[] MultiProviderMixedOutcome=["ApolloRateLimited","HunterOutcomeUnknown"];
    private sealed class ResultProvider(HiringDiscoveryResult result) : IHiringContactProvider
    {
        public string Name=>"Apollo/Hunter"; public bool IsAvailable { get; set; }=true; public int Calls;
        public Task<IReadOnlyList<HiringProviderContact>> DiscoverAsync(HiringDiscoveryContext context,CancellationToken ct)=>Task.FromResult(result.Contacts);
        public Task<HiringDiscoveryResult> DiscoverResultAsync(HiringDiscoveryContext context,CancellationToken ct) { Calls++; return Task.FromResult(result); }
    }
    [Fact]
    public async Task MultiProviderOperationChargesOncePersistsPerContactProvenanceAndNextCandidateUsesDatabaseOnly()
    {
        using var f=new Fixture(); f.Limits.SharedContacts.Enabled=true;
        var apollo=Contact("apollo@example.org","Technical Recruiter") with { Provider="Apollo",Source="PersonEnrichment",Location="Hyderabad, India" };
        var hunter=Contact("hunter@example.org","Talent Acquisition") with { Provider="Hunter",Source="DomainSearch",Location="India" };
        var provider=new ResultProvider(new([apollo,hunter])); f.DiscoveryOverride=provider;
        var key=Guid.NewGuid(); var first=await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,key),default);
        Assert.True(first.Usage.Charged); Assert.Equal(0,first.Usage.Remaining); Assert.Equal(2,first.Usage.ProviderAttemptsRemaining);
        Assert.Equal(1,await f.Db.Set<HiringOutreachSession>().CountAsync(x=>x.DispatchedAtUtc!=null));
        Assert.Contains(first.Contacts,x=>x.Source=="Apollo:PersonEnrichment"); Assert.Contains(first.Contacts,x=>x.Source=="Hunter:DomainSearch");
        var replay=await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,key),default); Assert.False(replay.Usage.Charged); Assert.Equal(1,provider.Calls);
        provider.IsAvailable=false;
        var cached=await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.False(cached.Usage.Charged); Assert.Equal("CareerHarborCache",cached.Source); Assert.Equal(1,provider.Calls); Assert.Equal(0,f.Hunter.Calls);
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.NotFoundException>(()=>f.Service.GetAsync(f.Other.Id,first.SessionId,default));
        var rows=await f.Db.Set<HiringContactIntelligence>().ToArrayAsync();
        Assert.Contains(rows,x=>x.Provider=="Apollo" && x.Source=="PersonEnrichment"); Assert.Contains(rows,x=>x.Provider=="Hunter" && x.Source=="DomainSearch");
    }
    [Theory]
    [InlineData(false,"ApolloRateLimited","Released")]
    [InlineData(true,"ApolloOutcomeUnknown","Uncertain")]
    public async Task MultiProviderEmptyOutcomePreservesSafeQuotaTransitions(bool uncertain,string failure,string expected)
    {
        using var f=new Fixture(); var provider=new ResultProvider(new([],uncertain,new[] {failure})); f.DiscoveryOverride=provider;
        var result=await f.Discover(); var row=await f.Db.Set<HiringOutreachSession>().SingleAsync();
        Assert.Equal(expected,row.UsageState); Assert.Equal(uncertain,result.Usage.Charged); Assert.Equal(1,provider.Calls);
    }
    [Fact]
    public async Task UsefulEarlierContactSurvivesUnknownLaterOutcomeWithoutReleasingItsReservation()
    {
        using var f=new Fixture(); var provider=new ResultProvider(new([Contact("earlier@example.org","Recruiter")],true,MultiProviderUnknownOutcome)); f.DiscoveryOverride=provider;
        var result=await f.Discover(); Assert.Equal("Found",result.State); Assert.Single(result.Contacts); Assert.True(result.Usage.Charged);
        Assert.Equal("Uncertain",(await f.Db.Set<HiringOutreachSession>().SingleAsync()).UsageState);
    }
    [Fact]
    public async Task UnknownLaterProviderFailureIsTheOperatorReasonRatherThanAnEarlierRefundableRejection()
    {
        using var f=new Fixture(); f.DiscoveryOverride=new ResultProvider(new([],true,MultiProviderMixedOutcome));
        var result=await f.Discover(); Assert.Equal("HunterOutcomeUnknown",result.FailureCode); Assert.True(result.Usage.Charged);
    }
}
