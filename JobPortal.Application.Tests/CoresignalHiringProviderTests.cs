using System.Net;
using System.Text;
using System.Text.Json;
using JobPortal.API.Controllers;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class CoresignalHiringProviderTests
{
    private static CoresignalHiringOptions Settings()=>new() { Enabled=true,ApiKey="mock-only-secret",MaximumSearchRequests=1 };
    private static HiringDiscoveryContext Context()=>new("Accenture","accenture.com","Trust & Safety Associate","","Hyderabad");
    private static HttpResponseMessage Json(string value)=>new(HttpStatusCode.OK) { Content=new StringContent(value,Encoding.UTF8,"application/json") };
    private const string Preview="""[{"id":123,"full_name":"Example Recruiter","active_experience_title":"Recruiter","active_experience_department":"HR","company_website":"https://www.accenture.com","location_full":"Hyderabad, India"}]""";
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) { Calls++; return send(request,ct); }
    }
    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory,IDisposable
    {
        private readonly HttpClient client=new(handler,false);
        public HttpClient CreateClient(string name)=>client;
        public void Dispose()=>client.Dispose();
    }
    private static CoresignalHiringProvider Provider(Clients clients,CoresignalHiringOptions options)=>new(clients,Options.Create(options),NullLogger<CoresignalHiringProvider>.Instance);
    private static async Task<HiringPersonIdentity[]> Search(CoresignalHiringProvider provider)
    {
        var found=new List<HiringPersonIdentity>(); await foreach(var batch in provider.SearchAsync(Context(),default)) found.AddRange(batch); return found.ToArray();
    }
    [Fact]
    public async Task DisabledProviderRequiresNoKeyAndNeverDispatches()
    {
        using var handler=new Handler((_,_)=>throw new InvalidOperationException("Unexpected HTTP")); using var clients=new Clients(handler);
        var settings=new CoresignalHiringOptions(); Assert.True(settings.IsValid()); Assert.False(settings.AllowCandidateDisplay); Assert.False(settings.AllowSharedPersistence);
        Assert.Empty(await Search(Provider(clients,settings))); Assert.Equal(0,handler.Calls);
        settings.Enabled=true; Assert.False(settings.IsValid());
    }
    [Fact]
    public async Task PreviewUsesOfficialShapeAuthCurrentEmployerIndiaAndNoAutomaticPagination()
    {
        using var handler=new Handler(async (request,ct)=>
        {
            Assert.Equal(HttpMethod.Post,request.Method); Assert.Equal("api.coresignal.com",request.RequestUri!.Host);
            Assert.Equal("/cdapi/v2/employee_multi_source/search/es_dsl/preview",request.RequestUri.AbsolutePath);
            Assert.Equal("?page=1",request.RequestUri.Query); Assert.Equal("mock-only-secret",Assert.Single(request.Headers.GetValues("apikey")));
            var body=await request.Content!.ReadAsStringAsync(ct); using var json=JsonDocument.Parse(body);
            Assert.True(json.RootElement.TryGetProperty("query",out _)); Assert.DoesNotContain("size",body,StringComparison.Ordinal);
            Assert.Contains("active_experience_company_website.domain_only",body); Assert.Contains("accenture.com",body); Assert.Contains("active_experience_title",body);
            Assert.Contains("HYDERABAD",body); Assert.Contains("India",body); Assert.DoesNotContain("mock-only-secret",body,StringComparison.Ordinal);
            var response=Json(Preview); response.Headers.Add("x-total-results","10000"); response.Headers.Add("x-snapshot-token","mock-snapshot"); return response;
        });
        using var clients=new Clients(handler); var person=Assert.Single(await Search(Provider(clients,Settings())));
        Assert.Equal("Coresignal",person.Provider); Assert.Equal("123",person.Reference); Assert.Equal("accenture.com",person.Domain); Assert.Equal("Hyderabad, India",person.Location);
        Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task LimitsAndDuplicateIdentitiesDoNotCauseAdditionalPagesOrCollectRequests()
    {
        using var handler=new Handler((_,_)=>Task.FromResult(Json(Preview))); using var clients=new Clients(handler);
        var settings=Settings(); settings.MaximumSearchRequests=2; settings.MaximumCandidatesPerSearch=1;
        var provider=Provider(clients,settings); var found=await Search(provider);
        Assert.Single(found); Assert.Equal(2,handler.Calls);
    }
    [Fact]
    public async Task SearchHardCapAndManagerStageRemainBoundedWithInvalidDirectConstruction()
    {
        var requests=new List<string>();
        using var handler=new Handler(async (request,ct)=> { requests.Add(await request.Content!.ReadAsStringAsync(ct)); return Json("[]"); }); using var clients=new Clients(handler);
        var settings=Settings(); settings.MaximumSearchRequests=999; Assert.False(settings.IsValid());
        Assert.Empty(await Search(Provider(clients,settings))); Assert.Equal(5,handler.Calls);
        Assert.Contains("Procurement Manager",requests[3]); Assert.Contains("Operations Manager",requests[3]);
        Assert.DoesNotContain("Operations Lead",requests[3],StringComparison.Ordinal); Assert.Contains("Operations Lead",requests[4]);
        Assert.DoesNotContain("location_country",requests[4],StringComparison.Ordinal);
    }
    [Fact]
    public async Task EnrichmentBudgetNeverCollectsEverySearchIdentity()
    {
        var preview=Preview.Replace("]",",{\"id\":124,\"full_name\":\"Second Recruiter\",\"active_experience_title\":\"Recruiter\",\"company_website\":\"accenture.com\"}]",StringComparison.Ordinal);
        using var handler=new Handler((request,_)=>Task.FromResult(Json(request.Method==HttpMethod.Post ? preview : "{\"code\":1,\"data\":null}"))); using var clients=new Clients(handler);
        var settings=Settings(); settings.MaximumEnrichments=1; var provider=Provider(clients,settings); var people=await Search(provider); Assert.Equal(2,people.Length);
        Assert.Null(await provider.EnrichAsync(people[0],Context(),default)); Assert.Null(await provider.EnrichAsync(people[1],Context(),default)); Assert.Equal(2,handler.Calls);
    }
    [Fact]
    public async Task WrongCompanyMissingCurrentTitleAndObfuscatedNamesAreNotIdentities()
    {
        using var handler=new Handler((_,_)=>Task.FromResult(Json("""[{"id":1,"full_name":"Other Person","active_experience_title":"Recruiter","company_website":"other.com"},{"id":2,"full_name":"Test Person","headline":"Former Recruiter","company_website":"accenture.com"},{"id":3,"full_name":"T***","active_experience_title":"Recruiter","company_website":"accenture.com"},null]""")));
        using var clients=new Clients(handler); Assert.Empty(await Search(Provider(clients,Settings())));
    }
    [Theory]
    [InlineData("verified","valid")]
    [InlineData("catch_all","unknown")]
    public async Task ContactEndpointUsesEmployeeIdOnlyAndNeverTreatsCatchAllAsVerified(string status,string expected)
    {
        using var handler=new Handler((request,_)=>
        {
            if(request.Method==HttpMethod.Post) return Task.FromResult(Json(Preview));
            Assert.Equal("/enrichment/contacts",request.RequestUri!.AbsolutePath); Assert.Equal("?member_id=123",request.RequestUri.Query);
            Assert.Null(request.Content); Assert.DoesNotContain("phone",request.RequestUri.ToString(),StringComparison.Ordinal);
            return Task.FromResult(Json("{\"code\":0,\"data\":{\"email\":\"example@accenture.com\",\"email_status\":\""+status+"\",\"email_confidence\":100}}"));
        });
        using var clients=new Clients(handler); var provider=Provider(clients,Settings()); var person=Assert.Single(await Search(provider));
        var contact=await provider.EnrichAsync(person,Context(),default); Assert.NotNull(contact); Assert.Equal(expected,contact.Verification);
        Assert.Equal("Coresignal",contact.Provider); Assert.Equal("EmployeeEnrichment",contact.Source);
        Assert.Null(await provider.EnrichAsync(person,Context(),default)); Assert.Equal(2,handler.Calls);
    }
    [Theory]
    [InlineData(400,"CoresignalInvalidRequest")]
    [InlineData(401,"CoresignalConfigurationRequired")]
    [InlineData(403,"CoresignalConfigurationRequired")]
    [InlineData(402,"CoresignalQuotaExceeded")]
    [InlineData(429,"CoresignalRateLimited")]
    [InlineData(503,"CoresignalUnavailable")]
    public async Task FailuresHaveSafeCodesAndNeverRetry(int status,string expected)
    {
        using var handler=new Handler((_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content=new StringContent("private-provider-body") }));
        using var clients=new Clients(handler); var error=await Assert.ThrowsAsync<HiringProviderException>(()=>Search(Provider(clients,Settings())));
        Assert.Equal(expected,error.Code); Assert.DoesNotContain("private-provider-body",error.ToString(),StringComparison.Ordinal); Assert.Equal(1,handler.Calls);
    }
    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    public async Task MalformedPreviewHasExplicitSafeFailure(string response)
    {
        using var handler=new Handler((_,_)=>Task.FromResult(Json(response))); using var clients=new Clients(handler);
        Assert.Equal("CoresignalInvalidResponse",(await Assert.ThrowsAsync<HiringProviderException>(()=>Search(Provider(clients,Settings())))).Code);
    }
    [Fact]
    public async Task TimeoutIsBoundedAndClassifiedAsUncertain()
    {
        using var handler=new Handler(async (_,ct)=> { await Task.Delay(Timeout.InfiniteTimeSpan,ct); return Json("[]"); }); using var clients=new Clients(handler);
        var settings=Settings(); settings.TimeoutSeconds=1;
        var failure=await Assert.ThrowsAsync<HiringProviderException>(()=>Search(Provider(clients,settings)));
        Assert.Equal("CoresignalTimeout",failure.Code); Assert.False(HiringProviderFailures.ConfirmedRejection(failure.Code)); Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task PrivacyRestrictedIdentityCannotProceedToAnotherEnrichment()
    {
        using var handler=new Handler((request,_)=>Task.FromResult(Json(request.Method==HttpMethod.Post ? Preview : "{\"code\":2,\"data\":null}")));
        using var clients=new Clients(handler); var provider=Provider(clients,Settings()); var person=Assert.Single(await Search(provider));
        Assert.Equal("CoresignalPrivacyRestricted",(await Assert.ThrowsAsync<HiringProviderException>(()=>provider.EnrichAsync(person,Context(),default))).Code);
    }
    [Theory]
    [InlineData("personal@gmail.com")]
    [InlineData("person@other.com")]
    public async Task ContactEmailMustBelongToCurrentEmployer(string email)
    {
        using var handler=new Handler((request,_)=>Task.FromResult(Json(request.Method==HttpMethod.Post ? Preview : "{\"code\":0,\"data\":{\"email\":\""+email+"\",\"email_status\":\"verified\"}}")));
        using var clients=new Clients(handler); var provider=Provider(clients,Settings()); var person=Assert.Single(await Search(provider)); Assert.Null(await provider.EnrichAsync(person,Context(),default));
    }
    [Fact]
    public void DiagnosticEndpointRequiresAdministratorAndDisablesResponseCaching()
    {
        var type=typeof(AdminHiringProviderDiagnosticsController);
        Assert.Equal("Administrator",Assert.Single(type.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>()).Roles);
        Assert.True(Assert.Single(type.GetCustomAttributes(typeof(ResponseCacheAttribute),true).Cast<ResponseCacheAttribute>()).NoStore);
        Assert.DoesNotContain(type.GetCustomAttributes(true),x=>x is AllowAnonymousAttribute);
        var fields=typeof(HiringProviderProbeContact).GetProperties().Select(x=>x.Name).ToArray();
        Assert.DoesNotContain("Email",fields); Assert.DoesNotContain("FullName",fields); Assert.DoesNotContain("Reference",fields);
    }
}
