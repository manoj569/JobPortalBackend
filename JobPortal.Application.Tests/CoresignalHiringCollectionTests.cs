using System.Net;
using System.Text.Json;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class CoresignalHiringProviderTests
{
    private sealed class UnexpectedFinder : IHiringProfessionalEmailFinder
    {
        public bool IsAvailable=>true;
        public int Calls;
        public Task<HiringProviderContact?> FindAsync(HiringPersonIdentity person,CancellationToken ct)
        { Calls++; throw new InvalidOperationException("An unvalidated employee must not reach email finding."); }
    }
    [Theory]
    [InlineData(400,"CoresignalInvalidRequest")]
    [InlineData(401,"CoresignalConfigurationRequired")]
    [InlineData(403,"CoresignalConfigurationRequired")]
    [InlineData(402,"CoresignalQuotaExceeded")]
    [InlineData(404,"CoresignalEmployeeNotFound")]
    [InlineData(429,"CoresignalRateLimited")]
    [InlineData(503,"CoresignalUnavailable")]
    public async Task CollectFailureCodesNeverExposeBodiesOrRetry(int status,string expected)
    {
        using var handler=new Handler((request,_)=>Task.FromResult(request.Method==HttpMethod.Post ? Json(Preview) :
            new HttpResponseMessage((HttpStatusCode)status) { Content=new StringContent("unsafe-provider-body") }));
        using var clients=new Clients(handler); var provider=Provider(clients,Settings()); var person=Assert.Single(await Search(provider));
        var error=await Assert.ThrowsAsync<HiringProviderException>(()=>provider.EnrichAsync(person,Context(),default));
        Assert.Equal(expected,error.Code); Assert.DoesNotContain("unsafe-provider-body",error.ToString(),StringComparison.Ordinal); Assert.Equal(2,handler.Calls);
        if(status==404) Assert.True(HiringProviderFailures.ConfirmedRejection(error.Code));
    }
    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"id\":\"123\"}")]
    [InlineData("{\"id\":123,\"is_deleted\":false,\"is_working\":1}")]
    public async Task MalformedCollectIsExplicitInvalidResponse(string response)
    {
        using var handler=new Handler((request,_)=>Task.FromResult(Json(request.Method==HttpMethod.Post ? Preview : response)));
        using var clients=new Clients(handler); var provider=Provider(clients,Settings()); var person=Assert.Single(await Search(provider));
        Assert.Equal("CoresignalInvalidResponse",(await Assert.ThrowsAsync<HiringProviderException>(()=>provider.EnrichAsync(person,Context(),default))).Code);
    }
    [Theory]
    [InlineData("domain","CoresignalIdentityMismatch")]
    [InlineData("id","CoresignalIdentityMismatch")]
    [InlineData("name","CoresignalIdentityMismatch")]
    [InlineData("role","CoresignalIdentityMismatch")]
    [InlineData("not-working","CoresignalIdentityMismatch")]
    [InlineData("private","CoresignalPrivacyRestricted")]
    [InlineData("empty","CoresignalEmployeeUnavailable")]
    [InlineData("missing","CoresignalEmployeeNotFound")]
    public async Task InvalidOrMissingCurrentEmployeeNeverReachesHunterFinder(string condition,string expected)
    {
        var collected=condition switch {
            "domain"=>Collection(domain:"other.com"),"id"=>Collection(id:456),"name"=>Collection(name:"Different Person"),
            "role"=>Collection(title:"Software Engineer"),"not-working"=>Collection(working:0),"private"=>Collection(deleted:1),"empty"=>"{}",_=>Collection() };
        using var handler=new Handler((request,_)=>Task.FromResult(request.Method==HttpMethod.Post ? Json(Preview) :
            condition=="missing" ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(collected)));
        using var clients=new Clients(handler); var settings=Settings(); settings.AllowCandidateDisplay=true;
        var finder=new UnexpectedFinder(); var result=await Coordinator(Provider(clients,settings),finder).DiscoverResultAsync(Context(),default);
        Assert.Empty(result.Contacts); Assert.False(result.Uncertain); Assert.Contains(expected,result.Failures!); Assert.Equal(0,finder.Calls); Assert.Equal(2,handler.Calls);
    }
    [Theory]
    [InlineData("example@accenture.com",true)]
    [InlineData("example@other.com",false)]
    [InlineData("example@gmail.com",false)]
    public async Task ValidatedEmployeeUsesHunterAndOnlyVerifiedEmployerEmailIsAccepted(string email,bool accepted)
    {
        using var handler=new Handler((request,_)=>Task.FromResult(Json(request.Method==HttpMethod.Post ? Preview :
            request.RequestUri!.Host=="api.coresignal.com" ? Collection() :
            JsonSerializer.Serialize(new { data=new { first_name="Example",last_name="Recruiter",email,provenance="found",verification=new { status="valid" } } }))));
        using var clients=new Clients(handler); var settings=Settings(); settings.AllowCandidateDisplay=true;
        var finder=new HunterHiringProfessionalEmailFinder(clients,Options.Create(new HunterHiringOptions { ApiKey="mock-only" }));
        var result=await Coordinator(Provider(clients,settings),finder).DiscoverResultAsync(Context(),default);
        Assert.Equal(accepted ? 1 : 0,result.Contacts.Count); Assert.False(result.Uncertain); Assert.Equal(3,handler.Calls);
        if(accepted)
        {
            var contact=Assert.Single(result.Contacts); Assert.Equal("valid",contact.Verification); Assert.Equal(email,contact.Email);
            Assert.Equal("Coresignal+Hunter",contact.Provider); Assert.Equal("EmailFinder",contact.Source); Assert.Equal("123",contact.Reference);
        }
    }
}
