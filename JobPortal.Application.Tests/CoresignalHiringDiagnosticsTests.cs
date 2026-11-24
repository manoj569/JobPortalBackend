using System.Security.Claims;
using System.Text.Json;
using JobPortal.API.Controllers;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class CoresignalHiringProviderTests
{
    private sealed class NoVerification : IHiringEmailVerificationProvider
    {
        public bool IsAvailable=>false;
        public Task<HiringProviderContact?> VerifyAsync(HiringProviderContact contact,CancellationToken ct)=>throw new InvalidOperationException("Unexpected verification");
    }
    private sealed class NoFallback : IHiringFallbackContactProvider
    {
        public string Name=>"MockHunter";
        public bool IsAvailable=>false;
        public Task<IReadOnlyList<HiringProviderContact>> DiscoverAsync(HiringDiscoveryContext context,CancellationToken ct)=>throw new InvalidOperationException("Unexpected fallback");
    }
    private static HiringProviderOrchestrator Coordinator(CoresignalHiringProvider provider)=>new(provider,provider,new NoVerification(),new NoFallback(),
        Options.Create(new ApolloHiringOptions()),NullLogger<HiringProviderOrchestrator>.Instance,[provider]);
    [Fact]
    public async Task AdminProbeWorksWithDisplayDisabledButDoesNotReturnOrAuditContactIdentities()
    {
        using var handler=new Handler((request,_)=>Task.FromResult(Json(request.Method==HttpMethod.Post ? Preview :
            "{\"code\":0,\"data\":{\"email\":\"private@accenture.com\",\"email_status\":\"verified\"}}")));
        using var clients=new Clients(handler); var provider=Provider(clients,Settings()); var coordinator=Coordinator(provider); var audit=new AuditWriterTestDouble();
        Assert.False(provider.AllowCandidateDisplay); Assert.False(coordinator.IsAvailable);
        var normal=await coordinator.DiscoverResultAsync(Context(),default); Assert.Empty(normal.Contacts); Assert.Equal(0,handler.Calls);
        var service=new HiringProviderDiagnostics(provider,coordinator,audit);
        var result=await service.ProbeCoresignalAsync(Guid.NewGuid(),new("Accenture","accenture.com","Trust & Safety Associate","Hyderabad",true),default);
        Assert.Equal("Completed",result.State); Assert.Equal(1,result.IdentityCount); Assert.Equal(1,result.ContactCount);
        Assert.True(Assert.Single(result.Matches).ContactAvailable); Assert.Empty(result.Failures); Assert.Equal(2,handler.Calls);
        var diagnostic=JsonSerializer.Serialize(result)+JsonSerializer.Serialize(audit.Events);
        Assert.DoesNotContain("private@accenture.com",diagnostic,StringComparison.Ordinal); Assert.DoesNotContain("Example Recruiter",diagnostic,StringComparison.Ordinal);
        Assert.DoesNotContain("mock-only-secret",diagnostic,StringComparison.Ordinal); Assert.Equal(2,audit.Events.Count);
    }
    [Fact]
    public async Task ProbeRequiresExplicitCreditAcknowledgmentAndAuthenticatedActorBeforeDispatch()
    {
        using var handler=new Handler((_,_)=>throw new InvalidOperationException("Unexpected HTTP")); using var clients=new Clients(handler);
        var provider=Provider(clients,Settings()); var service=new HiringProviderDiagnostics(provider,Coordinator(provider),new AuditWriterTestDouble());
        var request=new HiringProviderProbeRequest("Accenture","accenture.com","Software Engineer","Hyderabad");
        await Assert.ThrowsAsync<BadRequestException>(()=>service.ProbeCoresignalAsync(Guid.NewGuid(),request,default));
        await Assert.ThrowsAsync<UnauthorizedException>(()=>service.ProbeCoresignalAsync(Guid.Empty,request with { ConfirmProviderCreditUse=true },default));
        Assert.Equal(0,handler.Calls);
    }
    [Fact]
    public async Task ActualDiagnosticAuthorizationPolicyRejectsCandidateAndAnonymousPrincipals()
    {
        var services=new ServiceCollection(); services.AddLogging(); services.AddAuthorizationCore(); using var scope=services.BuildServiceProvider();
        var attributes=typeof(AdminHiringProviderDiagnosticsController).GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<IAuthorizeData>();
        var policy=await AuthorizationPolicy.CombineAsync(scope.GetRequiredService<IAuthorizationPolicyProvider>(),attributes); Assert.NotNull(policy);
        var authorization=scope.GetRequiredService<IAuthorizationService>();
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(),null,policy)).Succeeded);
        var candidate=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role,"Candidate")],"Test"));
        Assert.False((await authorization.AuthorizeAsync(candidate,null,policy)).Succeeded);
        var administrator=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role,"Administrator")],"Test"));
        Assert.True((await authorization.AuthorizeAsync(administrator,null,policy)).Succeeded);
    }
    [Theory]
    [InlineData("accenture.com","Trust & Safety Associate","Hyderabad","Operations Manager")]
    [InlineData("accenture.com","Procurement Operations Associate","Mumbai","Procurement Manager")]
    [InlineData("nvent.com","Principal Software Engineer - Embedded Systems","India","Engineering Manager")]
    [InlineData("ptc.com","Software Engineer","Bengaluru","Engineering Manager")]
    [InlineData("infosys.com",".NET Developer","Pune","Engineering Manager")]
    public async Task IndiaScenarioRanksHiringIdentitiesBeforeOrdinaryForeignEmployees(string domain,string job,string city,string manager)
    {
        var previews=new[] {
            new { id=1,full_name="Local Recruiter",active_experience_title="Recruiter",company_website=domain,location_full=city+", India" },
            new { id=2,full_name="India Recruiter",active_experience_title="Talent Acquisition",company_website=domain,location_full="India" },
            new { id=3,full_name="Foreign Employee",active_experience_title="Software Engineer",company_website=domain,location_full="United States" },
            new { id=4,full_name="Relevant Manager",active_experience_title=manager,company_website=domain,location_full="India" } };
        using var handler=new Handler((request,_)=>Task.FromResult(Json(request.Method==HttpMethod.Post ? JsonSerializer.Serialize(previews) :
            "{\"code\":0,\"data\":{\"email\":\"person"+request.RequestUri!.Query.Split('=')[1]+"@"+domain+"\",\"email_status\":\"verified\"}}")));
        using var clients=new Clients(handler); var settings=Settings(); settings.AllowCandidateDisplay=true;
        var provider=Provider(clients,settings); var result=await Coordinator(provider).DiscoverResultAsync(new("Example",domain,job,"",city),default);
        Assert.Equal(2,result.Contacts.Count); Assert.Equal("Local Recruiter",result.Contacts[0].FullName);
        Assert.Contains(result.Contacts,x=>x.FullName=="India Recruiter"); Assert.DoesNotContain(result.Contacts,x=>x.FullName=="Foreign Employee"); Assert.Equal(3,handler.Calls);
    }
    [Theory]
    [InlineData("found","valid",true)]
    [InlineData("inferred","valid",false)]
    [InlineData("found","accept_all",false)]
    public async Task HunterFinderUsesExactNameDomainAndAcceptsOnlyFoundVerifiedBusinessEmail(string provenance,string status,bool accepted)
    {
        using var handler=new Handler((request,_)=>
        {
            Assert.Equal("/v2/email-finder/found",request.RequestUri!.AbsolutePath);
            var query=Uri.UnescapeDataString(request.RequestUri.Query); Assert.Contains("domain=accenture.com",query); Assert.Contains("full_name=Example Recruiter",query);
            Assert.True(request.Headers.Contains("X-API-KEY"));
            return Task.FromResult(Json("{\"data\":{\"first_name\":\"Example\",\"last_name\":\"Recruiter\",\"email\":\"example@accenture.com\",\"provenance\":\""+provenance+"\",\"verification\":{\"status\":\""+status+"\"}}}"));
        });
        using var clients=new Clients(handler); var finder=new HunterHiringProfessionalEmailFinder(clients,Options.Create(new HunterHiringOptions { ApiKey="mock-only" }));
        var result=await finder.FindAsync(new("Coresignal","123","Example","Example Recruiter","Recruiter","","accenture.com","India","IndiaRecruiters"),default);
        Assert.Equal(accepted,result is not null); if(result is not null) Assert.Equal("Coresignal+Hunter",result.Provider); Assert.Equal(1,handler.Calls);
    }
}
