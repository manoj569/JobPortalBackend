using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class HiringMultiProviderTests
{
    private sealed class Finder : IHiringProfessionalEmailFinder
    {
        public bool IsAvailable=>true;
        public int Calls;
        public Task<HiringProviderContact?> FindAsync(HiringPersonIdentity person,CancellationToken ct)
        { Calls++; return Task.FromResult<HiringProviderContact?>(Contact(person.Reference) with { Provider="Coresignal+Hunter",Source="EmailFinder" }); }
    }
    private static HiringProviderOrchestrator CoresignalCoordinator(People core,Fallback hunter,People? apollo=null,Finder? finder=null)
    {
        apollo ??=new People { IsAvailable=false };
        return new(apollo,apollo,new Verification(),hunter,Options.Create(new ApolloHiringOptions()),NullLogger<HiringProviderOrchestrator>.Instance,[core,apollo],finder);
    }
    private static People CoresignalPeople(params string[] ids)
    {
        var people=new People { Name="Coresignal" };
        people.Batches.Add(ids.Select(id=>Person(id) with { Provider="Coresignal" }).ToArray());
        foreach(var id in ids) people.Results[id]=Contact(id) with { Provider="Coresignal",Source="EmployeeEnrichment" };
        return people;
    }
    [Fact]
    public async Task CoresignalDisplayGatePreventsAllCandidateCallsWhileApolloHunterStillWork()
    {
        var core=CoresignalPeople("hidden"); core.AllowCandidateDisplay=false;
        var apollo=new People(); apollo.Batches.Add([Person("apollo")]); apollo.Results["apollo"]=Contact("apollo");
        var hunter=new Fallback(); var result=await CoresignalCoordinator(core,hunter,apollo).DiscoverResultAsync(Context(),default);
        Assert.Equal(0,core.Searches); Assert.Empty(core.Enriched); Assert.Single(result.Contacts); Assert.Equal("Apollo",result.Contacts[0].Provider); Assert.Equal(1,apollo.Searches);
    }
    [Fact]
    public async Task SufficientCoresignalStopsBeforeHunterAndOptionalApollo()
    {
        var core=CoresignalPeople("one","two","third"); var hunter=new Fallback(); var apollo=new People(); apollo.Batches.Add([Person("unused")]);
        var result=await CoresignalCoordinator(core,hunter,apollo).DiscoverResultAsync(Context(),default);
        Assert.Equal(2,result.Contacts.Count); Assert.All(result.Contacts,x=>Assert.Equal("Coresignal",x.Provider));
        Assert.Equal(2,core.Enriched.Count); Assert.Equal(0,hunter.Calls); Assert.Equal(0,apollo.Searches);
    }
    [Fact]
    public async Task CoresignalNoEmailUsesBoundedFinderAndPreservesIdentityProvenance()
    {
        var core=CoresignalPeople("one","two"); core.Results.Clear(); var finder=new Finder(); var hunter=new Fallback();
        var result=await CoresignalCoordinator(core,hunter,finder:finder).DiscoverResultAsync(Context(),default);
        Assert.Equal(2,finder.Calls); Assert.Equal(2,result.Contacts.Count); Assert.Equal(0,hunter.Calls);
        Assert.All(result.Contacts,x=> { Assert.Equal("Coresignal+Hunter",x.Provider); Assert.Equal("EmailFinder",x.Source); });
    }
    [Fact]
    public async Task CoresignalRateLimitSafelyFallsBackWithoutDiscardingEarlierResult()
    {
        var core=CoresignalPeople("one","two"); core.Failures["two"]="CoresignalRateLimited";
        var hunter=new Fallback { Result=new([Contact("hunter") with { Provider="Hunter" }]) };
        var result=await CoresignalCoordinator(core,hunter).DiscoverResultAsync(Context(),default);
        Assert.Equal(2,result.Contacts.Count); Assert.Contains(result.Contacts,x=>x.Provider=="Coresignal"); Assert.Contains(result.Contacts,x=>x.Provider=="Hunter");
        Assert.Contains("CoresignalRateLimited",result.Failures!); Assert.False(result.Uncertain); Assert.Equal(1,hunter.Calls); Assert.Equal(1,hunter.RequestedContacts);
    }
    [Fact]
    public async Task CoresignalPrivacyRestrictionDoesNotInvokeFinderForRestrictedPerson()
    {
        var core=CoresignalPeople("restricted"); core.Failures["restricted"]="CoresignalPrivacyRestricted"; var finder=new Finder();
        var result=await CoresignalCoordinator(core,new Fallback(),finder:finder).DiscoverResultAsync(Context(),default);
        Assert.Equal(0,finder.Calls); Assert.Empty(result.Contacts); Assert.False(result.Uncertain);
    }
    [Fact]
    public async Task CoresignalUnknownFailureKeepsEarlierContactsAndUncertainty()
    {
        var core=CoresignalPeople("one","two"); core.Failures["two"]="CoresignalTimeout";
        var result=await CoresignalCoordinator(core,new Fallback()).DiscoverResultAsync(Context(),default);
        Assert.Single(result.Contacts); Assert.True(result.Uncertain); Assert.Contains("CoresignalTimeout",result.Failures!);
    }
}
