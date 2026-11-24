using System.Runtime.CompilerServices;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class HiringMultiProviderTests
{
    private static readonly string[] RateLimited=["HunterRateLimited"];
    private static readonly string[] UnknownOutcome=["HunterOutcomeUnknown"];
    private static HiringDiscoveryContext Context()=>new("Example","example.org","Software Engineer","Engineering","Hyderabad");
    private static HiringProviderContact Contact(string id,string title="Technical Recruiter",string location="Hyderabad, India",string verification="valid")=>
        new("Test Person "+id,"Test",id+"@example.org",title,"",verification,95,null,id,location,"PersonEnrichment","Apollo");
    private static HiringPersonIdentity Person(string id,string title="Technical Recruiter",string location="Hyderabad, India")=>
        new("Apollo",id,"Test","Test Person "+id,title,"","example.org",location,"LocalRecruiting");
    private sealed class People : IHiringPersonDiscoveryProvider,IHiringContactEnrichmentProvider
    {
        public bool IsAvailable { get; set; }=true;
        public List<IReadOnlyList<HiringPersonIdentity>> Batches { get; }=[];
        public Dictionary<string,HiringProviderContact?> Results { get; }=new(StringComparer.Ordinal);
        public Dictionary<string,string> Failures { get; }=new(StringComparer.Ordinal);
        public string? SearchFailure; public int Searches; public List<string> Enriched { get; }=[];
        public async IAsyncEnumerable<IReadOnlyList<HiringPersonIdentity>> SearchAsync(HiringDiscoveryContext context,[EnumeratorCancellation] CancellationToken ct)
        {
            if(SearchFailure is not null) throw new HiringProviderException(SearchFailure);
            foreach(var batch in Batches) { ct.ThrowIfCancellationRequested(); Searches++; await Task.Yield(); yield return batch; }
        }
        public Task<HiringProviderContact?> EnrichAsync(HiringPersonIdentity person,HiringDiscoveryContext context,CancellationToken ct)
        {
            Enriched.Add(person.Reference);
            if(Failures.TryGetValue(person.Reference,out var failure)) throw new HiringProviderException(failure);
            return Task.FromResult(Results.GetValueOrDefault(person.Reference));
        }
    }
    private sealed class Fallback : IHiringFallbackContactProvider
    {
        public string Name=>"FakeHunter"; public bool IsAvailable { get; set; }=true; public int Calls; public int RequestedContacts;
        public HiringDiscoveryResult Result { get; set; }=new([]);
        public Task<IReadOnlyList<HiringProviderContact>> DiscoverAsync(HiringDiscoveryContext context,CancellationToken ct)=>Task.FromResult(Result.Contacts);
        public Task<HiringDiscoveryResult> DiscoverResultAsync(HiringDiscoveryContext context,CancellationToken ct) { Calls++; RequestedContacts=context.DesiredStrongContacts; return Task.FromResult(Result); }
    }
    private sealed class Verification : IHiringEmailVerificationProvider
    {
        public bool IsAvailable { get; set; } public int Calls;
        public Task<HiringProviderContact?> VerifyAsync(HiringProviderContact contact,CancellationToken ct)
        { Calls++; return Task.FromResult<HiringProviderContact?>(contact with { Verification="valid",Provider="Apollo+Hunter",Source="Verification" }); }
    }
    private static HiringProviderOrchestrator Coordinator(People people,Fallback fallback,Verification? verify=null,int maximumEnrichments=3)=>
        new(people,people,verify ?? new Verification(),fallback,Options.Create(new ApolloHiringOptions { Enabled=true,MaximumEnrichments=maximumEnrichments }),NullLogger<HiringProviderOrchestrator>.Instance);
    private sealed class Capture : ILogger<HiringProviderOrchestrator>
    {
        public List<string> Messages { get; }=[];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter)=>Messages.Add(formatter(state,error));
    }
    [Fact]
    public async Task ProviderDiagnosticsContainCountsAndFixedCodesWithoutContactIdentityOrSecrets()
    {
        var people=new People(); people.Batches.Add([Person("private-one"),Person("private-two")]);
        people.Results["private-one"]=Contact("private-one"); people.Results["private-two"]=Contact("private-two");
        var log=new Capture(); var config=new ApolloHiringOptions { ApiKey="fake-secret-for-log-assertion" };
        var service=new HiringProviderOrchestrator(people,people,new Verification(),new Fallback(),Options.Create(config),log);
        await service.DiscoverResultAsync(Context(),default); Assert.NotEmpty(log.Messages);
        Assert.All(log.Messages,message=> { Assert.DoesNotContain("Test Person",message,StringComparison.Ordinal); Assert.DoesNotContain("@example.org",message,StringComparison.Ordinal); Assert.DoesNotContain(config.ApiKey,message,StringComparison.Ordinal); });
    }
    [Fact]
    public async Task SufficientApolloContactsStopSearchEnrichmentAndHunterEarly()
    {
        var people=new People(); people.Batches.Add(Enumerable.Range(1,20).Select(i=>Person("person"+i)).ToArray()); people.Batches.Add([Person("later")]);
        foreach(var p in people.Batches[0]) people.Results[p.Reference]=Contact(p.Reference);
        var hunter=new Fallback(); var result=await Coordinator(people,hunter).DiscoverResultAsync(Context(),default);
        Assert.Equal(2,result.Contacts.Count); Assert.Equal(2,people.Enriched.Count); Assert.Equal(1,people.Searches); Assert.Equal(0,hunter.Calls); Assert.False(result.Uncertain);
    }
    [Fact]
    public async Task ApolloDisabledPreservesHunterFallbackWithoutSearchOrEnrichment()
    {
        var people=new People { IsAvailable=false }; people.Batches.Add([Person("unused")]);
        var hunter=new Fallback { Result=new([Contact("hunter") with { Provider="Hunter",Source="DomainSearch" }]) };
        var result=await Coordinator(people,hunter).DiscoverResultAsync(Context(),default);
        Assert.Single(result.Contacts); Assert.Equal(0,people.Searches); Assert.Empty(people.Enriched); Assert.Equal(1,hunter.Calls);
    }
    [Theory]
    [InlineData("ApolloRateLimited")]
    [InlineData("ApolloConfigurationRequired")]
    [InlineData("ApolloProviderUnavailable")]
    public async Task ApolloFailureFallsBackAndUncertaintyIsNeverForgotten(string failure)
    {
        var people=new People { SearchFailure=failure }; var hunter=new Fallback { Result=new([Contact("hunter") with { Provider="Hunter",Source="DomainSearch" }]) };
        var result=await Coordinator(people,hunter).DiscoverResultAsync(Context(),default);
        Assert.Single(result.Contacts); Assert.Equal(1,hunter.Calls); Assert.Equal(!HiringProviderFailures.ConfirmedRejection(failure),result.Uncertain); Assert.Contains(failure,result.Failures!);
    }
    [Fact]
    public async Task InsufficientApolloAndHunterMergeByNormalizedEmailAndKeepEarlierSuccessOnFailure()
    {
        var people=new People(); people.Batches.Add([Person("one")]); people.Results["one"]=Contact("one");
        var hunter=new Fallback { Result=new([Contact("one") with { Email="ONE@EXAMPLE.ORG",Provider="Hunter" },Contact("two") with { Provider="Hunter" }],false,RateLimited) };
        var result=await Coordinator(people,hunter).DiscoverResultAsync(Context(),default);
        Assert.Equal(2,result.Contacts.Count); Assert.Equal(2,result.Contacts.Select(x=>x.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count()); Assert.Equal(1,hunter.Calls); Assert.Equal(1,hunter.RequestedContacts); Assert.False(result.Uncertain);
    }
    [Fact]
    public async Task LaterUnknownOutcomeKeepsVerifiedEarlierContactAndReservationUncertainty()
    {
        var people=new People(); people.Batches.Add([Person("one")]); people.Results["one"]=Contact("one");
        var hunter=new Fallback { Result=new([],true,UnknownOutcome) };
        var result=await Coordinator(people,hunter).DiscoverResultAsync(Context(),default);
        Assert.Single(result.Contacts); Assert.True(result.Uncertain);
    }
    [Fact]
    public async Task EnrichmentFailuresPreserveEarlierContactsAndNeverRetryTheIdentity()
    {
        var people=new People(); people.Batches.Add([Person("one"),Person("two"),Person("two")]);
        people.Results["one"]=Contact("one"); people.Failures["two"]="ApolloRateLimited";
        var result=await Coordinator(people,new Fallback()).DiscoverResultAsync(Context(),default);
        Assert.Single(result.Contacts); Assert.Equal(2,people.Enriched.Count); Assert.Single(people.Enriched,x=>x=="two"); Assert.False(result.Uncertain);
    }
    [Fact]
    public async Task GenericEmployeesAreNotPurchasedBeforeHiringPeopleAndBudgetsAreHardBounded()
    {
        var people=new People(); people.Batches.Add([Person("ordinary","Software Engineer"),..Enumerable.Range(0,20).Select(i=>Person("recruiter"+i))]);
        await Coordinator(people,new Fallback(),maximumEnrichments:100).DiscoverResultAsync(Context(),default);
        Assert.Equal(5,people.Enriched.Count); Assert.DoesNotContain("ordinary",people.Enriched);
    }
    [Fact]
    public async Task OrchestrationAlsoBoundsEmptySearchBatches()
    {
        var people=new People(); for(var i=0;i<20;i++) people.Batches.Add([]);
        await Coordinator(people,new Fallback()).DiscoverResultAsync(Context(),default);
        Assert.Equal(5,people.Searches);
    }
    [Fact]
    public async Task VerifiedApolloEmailsDoNotPurchaseExtraVerification()
    {
        var people=new People(); people.Batches.Add([Person("one"),Person("two")]); people.Results["one"]=Contact("one"); people.Results["two"]=Contact("two");
        var verification=new Verification { IsAvailable=true };
        await Coordinator(people,new Fallback(),verification).DiscoverResultAsync(Context(),default); Assert.Equal(0,verification.Calls);
    }
    [Fact]
    public async Task UnverifiedApprovedAddressesCanBeVerifiedWithBoundedHunterCalls()
    {
        var people=new People(); people.Batches.Add([Person("one"),Person("two")]); people.Results["one"]=Contact("one",verification:"unknown"); people.Results["two"]=Contact("two",verification:"unknown");
        var verifier=new Verification { IsAvailable=true }; var hunter=new Fallback();
        var result=await Coordinator(people,hunter,verifier).DiscoverResultAsync(Context(),default);
        Assert.Equal(2,result.Contacts.Count); Assert.Equal(2,verifier.Calls); Assert.Equal(0,hunter.Calls);
    }
    [Fact]
    public async Task IndiaRecruiterRanksBeforeUsRecruiterAndOrdinaryEmployeesAreNotStrong()
    {
        var people=new People(); people.Batches.Add([Person("us",location:"United States"),Person("india",location:"India"),Person("local",location:"Hyderabad, India")]);
        people.Results["us"]=Contact("us",location:"United States"); people.Results["india"]=Contact("india",location:"India"); people.Results["local"]=Contact("local");
        var result=await Coordinator(people,new Fallback()).DiscoverResultAsync(Context(),default);
        Assert.Equal("local",result.Contacts[0].Reference); Assert.Equal("india",result.Contacts[1].Reference); Assert.DoesNotContain("us",people.Enriched);
    }
    [Theory]
    [InlineData("Bangalore","Bengaluru")]
    [InlineData("Bombay","Mumbai")]
    [InlineData("Gurgaon","Gurugram")]
    [InlineData("NCR","Delhi NCR")]
    public void IndiaCityAliasesShareRankingAndScopeBucket(string left,string right)=>Assert.Equal(HiringLocationPolicy.Normalize(left),HiringLocationPolicy.Normalize(right));
    [Theory]
    [InlineData("Engineer","TeamMember")]
    [InlineData("Engineering Lead","TeamLead")]
    [InlineData("Engineering Manager","DepartmentManager")]
    [InlineData("Hiring Manager","HiringManager")]
    public void EvidenceCategoryDoesNotInventHiringOwnership(string title,string expected)=>Assert.Equal(expected,HiringContactPolicy.ContactRole(title));
    [Theory]
    [InlineData("Trust & Safety Associate","Hyderabad","Trust & Safety Manager","Operations Manager")]
    [InlineData("Procurement Operations Associate","Mumbai","Operations Manager","Procurement Operations Lead")]
    [InlineData("Principle Software Engineer Embedded Systems","Bengaluru","Engineering Manager","Software Manager")]
    [InlineData("Technical Strategist","Chennai","Engineering Manager","Technical Manager")]
    public void ProductionCaseFixturesPreferHiringRolesAndNeverRecommendEveryEmployee(string job,string city,string manager,string lead)
    {
        var context=new HiringDiscoveryContext("Example","example.org",job,"",city);
        var hr=Contact("hr","HR",city); var recruiter=Contact("recruiter","Talent Acquisition Partner","India");
        var employees=new[] { Contact("peer","Engineer",city),Contact("design","Design Engineer",city),Contact("test","Test Automation Engineer",city) };
        var result=HiringContactPolicy.Select(employees.Concat(new[] {hr,recruiter,Contact("manager",manager,city),Contact("lead",lead,city)}),context,new());
        Assert.Equal(recruiter.Email,result[0].Email);
        Assert.Contains(result,x=>HiringContactPolicy.Type(x.Title)!="TeamMember");
        Assert.All(employees,x=>Assert.False(HiringContactPolicy.Strong(x,context)));
    }
    [Theory]
    [InlineData("Talent Acquisition Partner","India")]
    [InlineData("Engineering Manager","Hyderabad")]
    public async Task IndiaRecruitingAndManagerFallbackContactsCanBeReturnedWithoutInventedResponsibilities(string title,string location)
    {
        var people=new People(); people.Batches.Add([Person("person",title,location)]); people.Results["person"]=Contact("person",title,location);
        var result=await Coordinator(people,new Fallback { IsAvailable=false }).DiscoverResultAsync(Context(),default);
        Assert.Equal(title,Assert.Single(result.Contacts).Title); Assert.Equal(location,result.Contacts[0].Location);
    }
    [Fact]
    public void PtcObservedEmployeesRemainLowerPriorityThanHrAndAreNotMarkedStrong()
    {
        var context=new HiringDiscoveryContext("PTC fixture","example.org","Technical Strategist","","India");
        var hr=Contact("hr","HR","India");
        var result=HiringContactPolicy.Select(new[] {Contact("engineer","Engineer"),Contact("design","Design Engineer"),Contact("test","Test Automation Engineer"),hr},context,new());
        Assert.Equal(hr.Email,result[0].Email); Assert.Equal(4,result.Count);
        Assert.All(result.Where(x=>x.Title!="HR"),person=>Assert.False(HiringContactPolicy.Strong(person,context)));
    }
}
