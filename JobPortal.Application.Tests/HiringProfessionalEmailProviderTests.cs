using System.Net;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class HiringProfessionalEmailProviderTests
{
    private const string PersonJson="""{"error":false,"person":{"full_name":"Test Recruiter","email":{"status":"VERIFIED","revealed":true,"email":"test@example.org"}},"company":{"domain":"example.org"}}""";
    private const string SnovFound="""{"status":"completed","data":[{"people":"Test Recruiter","result":[{"email":"test@example.org","smtp_status":"valid","is_valid_format":true,"is_webmail":false,"is_disposable":false,"is_gibberish":false}]}],"meta":{"task_hash":"mock-task"}}""";
    private const string HunterFound="""{"data":{"first_name":"Test","last_name":"Recruiter","email":"test@example.org","provenance":"found","verification":{"status":"valid"}}}""";
    private static HiringPersonIdentity Person(string id="1")=>new("Coresignal",id,"Test","Test Recruiter","Recruiter","","example.org","India","Recruiters");
    private static HttpResponseMessage Json(string value,HttpStatusCode status=HttpStatusCode.OK)=>new(status) { Content=new StringContent(value,Encoding.UTF8,"application/json") };
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<string> Hosts { get; }=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Hosts.Add(request.RequestUri!.Host); return response(request,ct); }
    }
    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory,IDisposable
    {
        private readonly HttpClient client=new(handler,false);
        public HttpClient CreateClient(string name)=>client;
        public void Dispose()=>client.Dispose();
    }
    private sealed class Capture : ILogger<HiringProfessionalEmailWaterfall>
    {
        public List<string> Messages { get; }=[];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter)=>Messages.Add(formatter(state,error));
    }
    private static ProspeoHiringOptions Prospeo()=>new() { Enabled=true,ApiKey="mock-prospeo-secret",AllowCandidateDisplay=true };
    private static SnovHiringOptions Snov()=>new() { Enabled=true,ClientId="mock-client",ClientSecret="mock-snov-secret",AllowCandidateDisplay=true };
    private static HiringProfessionalEmailWaterfall Waterfall(Clients clients,ProspeoHiringOptions? prospeo=null,SnovHiringOptions? snov=null,Capture? log=null)=>new(
        new(clients,Options.Create(prospeo ?? Prospeo())),new(clients,Options.Create(snov ?? Snov())),
        new(clients,Options.Create(new HunterHiringOptions { ApiKey="mock-hunter-secret" })),log ?? new Capture());
    private static Task<HttpResponseMessage> SnovResponse(HttpRequestMessage request,string result=SnovFound)=>Task.FromResult(Json(request.RequestUri!.AbsolutePath switch {
        "/v1/oauth/access_token"=>"{\"access_token\":\"mock-token\",\"expires_in\":3600}",
        "/v2/emails-by-domain-by-name/start"=>"{\"data\":{\"task_hash\":\"mock-task\"}}",
        "/v2/emails-by-domain-by-name/result"=>result,_=>throw new InvalidOperationException("Unexpected endpoint") }));
    [Theory]
    [InlineData("Prospeo",1,0,0)]
    [InlineData("Snov",1,3,0)]
    [InlineData("Hunter",1,3,1)]
    public async Task WaterfallStopsAtFirstVerifiedProviderAndPreservesProvenance(string winner,int pro,int snov,int hunter)
    {
        using var handler=new Handler((request,_)=>request.RequestUri!.Host switch {
            "api.prospeo.io"=>Task.FromResult(winner=="Prospeo" ? Json(PersonJson) : Json("{\"error\":true,\"error_code\":\"NO_MATCH\"}",HttpStatusCode.BadRequest)),
            "api.snov.io"=>SnovResponse(request,winner=="Snov" ? SnovFound : "{\"status\":\"completed\",\"data\":[],\"meta\":{\"task_hash\":\"mock-task\"}}"),
            "api.hunter.io"=>Task.FromResult(Json(HunterFound)),_=>throw new InvalidOperationException("Unexpected host") });
        using var clients=new Clients(handler); var result=await Waterfall(clients).FindResultAsync(Person(),false,default);
        Assert.NotNull(result.Contact); Assert.Equal("Coresignal+"+winner,result.Contact.Provider); Assert.Equal("valid",result.Contact.Verification);
        Assert.False(result.Uncertain); Assert.Equal(pro,handler.Hosts.Count(x=>x=="api.prospeo.io")); Assert.Equal(snov,handler.Hosts.Count(x=>x=="api.snov.io")); Assert.Equal(hunter,handler.Hosts.Count(x=>x=="api.hunter.io"));
    }
    [Fact]
    public async Task RequestShapesKeepCredentialsOutOfUrlsAndDoNotRequestMobile()
    {
        using var handler=new Handler(async (request,ct)=>
        {
            var body=request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain("secret",request.RequestUri!.ToString(),StringComparison.Ordinal); Assert.DoesNotContain("mock-token",request.RequestUri.ToString(),StringComparison.Ordinal);
            if(request.RequestUri.Host=="api.prospeo.io")
            {
                Assert.Equal("/enrich-person",request.RequestUri.AbsolutePath); Assert.Equal(HttpMethod.Post,request.Method); Assert.True(request.Headers.Contains("X-KEY"));
                using var data=JsonDocument.Parse(body); Assert.True(data.RootElement.GetProperty("only_verified_email").GetBoolean()); Assert.False(data.RootElement.GetProperty("enrich_mobile").GetBoolean());
                Assert.Equal("example.org",data.RootElement.GetProperty("data").GetProperty("company_website").GetString());
                Assert.Equal("Test",data.RootElement.GetProperty("data").GetProperty("first_name").GetString());
                return Json("{\"error\":true,\"error_code\":\"NO_MATCH\"}",HttpStatusCode.BadRequest);
            }
            if(request.RequestUri.AbsolutePath.EndsWith("access_token",StringComparison.Ordinal))
            { Assert.Contains("grant_type=client_credentials",body); Assert.Contains("client_secret=mock-snov-secret",body); }
            else Assert.Equal("Bearer",request.Headers.Authorization!.Scheme);
            if(request.RequestUri.AbsolutePath.EndsWith("/start",StringComparison.Ordinal))
            { using var data=JsonDocument.Parse(body); Assert.Single(data.RootElement.GetProperty("rows").EnumerateArray()); Assert.DoesNotContain("webhook",body,StringComparison.Ordinal); }
            if(request.Method==HttpMethod.Get) Assert.Equal("?task_hash=mock-task",request.RequestUri.Query);
            return await SnovResponse(request);
        });
        using var clients=new Clients(handler); Assert.NotNull((await Waterfall(clients).FindResultAsync(Person(),false,default)).Contact);
    }
    [Theory]
    [InlineData("other.org","VERIFIED")]
    [InlineData("example.org","UNKNOWN")]
    [InlineData("example.org","CATCH_ALL")]
    public async Task ProspeoWrongDomainAndUnverifiedResultsFallThrough(string domain,string status)
    {
        using var handler=new Handler((request,_)=>Task.FromResult(request.RequestUri!.Host=="api.prospeo.io" ? Json(PersonJson.Replace("test@example.org","test@"+domain,StringComparison.Ordinal).Replace("VERIFIED",status,StringComparison.Ordinal)) : Json(HunterFound)));
        using var clients=new Clients(handler); var result=await Waterfall(clients,snov:new SnovHiringOptions()).FindResultAsync(Person(),false,default);
        Assert.Equal("Coresignal+Hunter",result.Contact!.Provider); Assert.Equal(2,handler.Hosts.Count);
    }
    [Theory]
    [InlineData("unknown",false)]
    [InlineData("valid",true)]
    public async Task SnovRejectsUnknownAndWebmail(string status,bool webmail)
    {
        var response=SnovFound.Replace("\"smtp_status\":\"valid\"","\"smtp_status\":\""+status+"\"",StringComparison.Ordinal);
        if(webmail) response=response.Replace("\"is_webmail\":false","\"is_webmail\":true",StringComparison.Ordinal);
        using var handler=new Handler((request,_)=>request.RequestUri!.Host=="api.snov.io" ? SnovResponse(request,response) : Task.FromResult(Json(HunterFound)));
        using var clients=new Clients(handler); var result=await Waterfall(clients,new ProspeoHiringOptions()).FindResultAsync(Person(),false,default);
        Assert.Equal("Coresignal+Hunter",result.Contact!.Provider);
    }
    [Fact]
    public async Task DisabledAndUnapprovedCandidateProvidersAreSkippedButAdminCanProbe()
    {
        using var handler=new Handler((request,_)=>Task.FromResult(Json(request.RequestUri!.Host=="api.prospeo.io" ? PersonJson : HunterFound)));
        using var clients=new Clients(handler); var pro=Prospeo(); pro.AllowCandidateDisplay=false;
        var service=Waterfall(clients,pro,new SnovHiringOptions());
        Assert.Equal("Coresignal+Hunter",(await service.FindResultAsync(Person(),false,default)).Contact!.Provider);
        Assert.DoesNotContain("api.prospeo.io",handler.Hosts); Assert.DoesNotContain("api.snov.io",handler.Hosts);
        Assert.Equal("Coresignal+Prospeo",(await service.FindResultAsync(Person("2"),true,default)).Contact!.Provider);
        Assert.True(new ProspeoHiringOptions().IsValid()); Assert.True(new SnovHiringOptions().IsValid());
    }
    [Fact]
    public async Task TwoIdentitiesMaximumAndDuplicateCallsNeverSpendAgain()
    {
        using var handler=new Handler((_,_)=>Task.FromResult(Json(PersonJson))); using var clients=new Clients(handler); var service=Waterfall(clients);
        Assert.NotNull((await service.FindResultAsync(Person(),false,default)).Contact);
        Assert.NotNull((await service.FindResultAsync(Person(),false,default)).Contact);
        Assert.NotNull((await service.FindResultAsync(Person("2"),false,default)).Contact);
        Assert.Null((await service.FindResultAsync(Person("3"),false,default)).Contact); Assert.Equal(2,handler.Hosts.Count);
    }
    [Theory]
    [InlineData(429,false)]
    [InlineData(503,true)]
    public async Task ProviderFailureContinuesAndPreservesUncertainty(int status,bool uncertain)
    {
        using var handler=new Handler((request,_)=>Task.FromResult(request.RequestUri!.Host=="api.prospeo.io" ? Json("private-body",(HttpStatusCode)status) : Json(HunterFound)));
        using var clients=new Clients(handler); var result=await Waterfall(clients,snov:new SnovHiringOptions()).FindResultAsync(Person(),false,default);
        Assert.Equal("Coresignal+Hunter",result.Contact!.Provider); Assert.Equal(uncertain,result.Uncertain); Assert.NotEmpty(result.Failures);
    }
    [Fact]
    public async Task LogsContainOnlyProviderAndFixedOutcome()
    {
        using var handler=new Handler((_,_)=>Task.FromResult(Json(PersonJson))); using var clients=new Clients(handler); var log=new Capture();
        await Waterfall(clients,log:log).FindResultAsync(Person(),false,default); Assert.NotEmpty(log.Messages);
        Assert.All(log.Messages,message=> { Assert.DoesNotContain("Test Recruiter",message,StringComparison.Ordinal); Assert.DoesNotContain("@",message,StringComparison.Ordinal); Assert.DoesNotContain("secret",message,StringComparison.Ordinal); Assert.DoesNotContain("mock-token",message,StringComparison.Ordinal); });
    }
}
