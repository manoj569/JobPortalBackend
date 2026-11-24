using System.Net;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class HiringProviderAdapterTests
{
    private static ApolloHiringOptions ApolloSettings()=>new() { Enabled=true,ApiKey="fake-apollo-key" };
    [Fact]
    public async Task ApolloDisabledNeverDispatchesAndRequiresNoKey()
    {
        using var handler=new Handler(_=>throw new InvalidOperationException("Unexpected request.")); using var clients=new Clients(handler);
        var options=new ApolloHiringOptions(); Assert.True(options.IsValid());
        var provider=new ApolloHiringProvider(clients,Options.Create(options));
        await foreach(var batch in provider.SearchAsync(new("Example","example.org","Software Engineer","","Hyderabad"),default)) Assert.Empty(batch);
        Assert.Equal(0,handler.Calls);
    }
    [Fact]
    public async Task ApolloSearchUsesDocumentedFiltersAndNeverTreatsObfuscatedNamesOrLocationFlagsAsPersonalData()
    {
        var queries=new List<string>();
        using var handler=new Handler(request=>
        {
            Assert.Equal(HttpMethod.Post,request.Method); Assert.Equal("api.apollo.io",request.RequestUri!.Host);
            var query=Uri.UnescapeDataString(request.RequestUri.Query); queries.Add(query);
            Assert.True(request.Headers.Contains("x-api-key")); Assert.DoesNotContain("fake-apollo-key",query,StringComparison.Ordinal);
            return Json("""{"people":[{"id":"identity-one","first_name":"Test","last_name_obfuscated":"P***n","title":"Technical Recruiter","has_city":true,"has_email":true}]}""");
        });
        using var clients=new Clients(handler); var settings=ApolloSettings(); settings.MaximumSearchRequests=1;
        var provider=new ApolloHiringProvider(clients,Options.Create(settings)); var found=new List<HiringPersonIdentity>();
        await foreach(var batch in provider.SearchAsync(new("Example","example.org","Software Engineer","","Hyderabad"),default)) found.AddRange(batch);
        var person=Assert.Single(found); Assert.Equal("identity-one",person.Reference); Assert.Empty(person.FullName); Assert.Empty(person.Location);
        Assert.Contains("q_organization_domains_list[]=example.org",queries[0]); Assert.Contains("person_locations[]=HYDERABAD, India",queries[0]);
        Assert.Contains("person_titles[]=Recruiter",queries[0]); Assert.Contains("per_page=20",queries[0]); Assert.Equal(1,handler.Calls);
    }
    [Theory]
    [InlineData("verified",true)]
    [InlineData("unverified",false)]
    public async Task ApolloEnrichmentUsesExactIdentityAndBusinessOnlyFlags(string verification,bool verified)
    {
        using var handler=new Handler(request=>
        {
            var query=Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Equal("/api/v1/people/match",request.RequestUri.AbsolutePath); Assert.Contains("id=person-id",query);
            Assert.Contains("reveal_personal_emails=false",query); Assert.Contains("reveal_phone_number=false",query); Assert.Contains("run_waterfall_email=false",query);
            return Json("{\"person\":{\"id\":\"person-id\",\"name\":\"Test Person\",\"first_name\":\"Test\",\"title\":\"Technical Recruiter\",\"email\":\"test@example.org\",\"email_status\":\""+verification+"\",\"city\":\"Hyderabad\",\"country\":\"India\",\"organization\":{\"primary_domain\":\"example.org\"}}}");
        });
        using var clients=new Clients(handler); var provider=new ApolloHiringProvider(clients,Options.Create(ApolloSettings()));
        var result=await provider.EnrichAsync(new("Apollo","person-id","Test","","Technical Recruiter","","example.org","","LocalRecruiting"),new("Example","example.org","Software Engineer",""),default);
        Assert.NotNull(result); Assert.Equal(verified ? "valid" : "unknown",result.Verification); Assert.Equal("Apollo",result.Provider); Assert.Equal("PersonEnrichment",result.Source);
    }
    [Theory]
    [InlineData("other.org","person-id","high")]
    [InlineData("example.org","different-person","high")]
    [InlineData("example.org","person-id","low")]
    public async Task ApolloRejectsFormerEmployerDifferentIdentityAndLowConfidenceMatches(string domain,string id,string confidence)
    {
        using var handler=new Handler(_=>Json("{\"person\":{\"id\":\""+id+"\",\"match_confidence\":\""+confidence+"\",\"name\":\"Test Person\",\"email\":\"test@example.org\",\"email_status\":\"verified\",\"organization\":{\"primary_domain\":\""+domain+"\"}}}"));
        using var clients=new Clients(handler); var provider=new ApolloHiringProvider(clients,Options.Create(ApolloSettings()));
        Assert.Null(await provider.EnrichAsync(new("Apollo","person-id","Test","","Recruiter","","example.org","",""),new("Example","example.org","Engineer",""),default));
    }
    [Theory]
    [InlineData(400,"ApolloInvalidRequest")]
    [InlineData(401,"ApolloConfigurationRequired")]
    [InlineData(403,"ApolloConfigurationRequired")]
    [InlineData(402,"ApolloQuotaExceeded")]
    [InlineData(429,"ApolloRateLimited")]
    [InlineData(503,"ApolloProviderUnavailable")]
    public async Task ApolloErrorsAreSafeAndNotAutomaticallyRetried(int status,string code)
    {
        using var handler=new Handler(_=>new((HttpStatusCode)status)); using var clients=new Clients(handler);
        var provider=new ApolloHiringProvider(clients,Options.Create(ApolloSettings()));
        var error=await Assert.ThrowsAsync<HiringProviderException>(async ()=> { await foreach(var unused in provider.SearchAsync(new("Example","example.org","Engineer",""),default)) { } });
        Assert.Equal(code,error.Code); Assert.DoesNotContain("fake-apollo-key",error.ToString(),StringComparison.Ordinal); Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task ApolloSearchBudgetIsBoundedEvenWhenConstructedWithoutOptionsValidation()
    {
        using var handler=new Handler(_=>Json("""{"people":[]}""")); using var clients=new Clients(handler);
        var settings=ApolloSettings(); settings.MaximumSearchRequests=100;
        var provider=new ApolloHiringProvider(clients,Options.Create(settings));
        await foreach(var unused in provider.SearchAsync(new("Example","example.org","Engineer","","Hyderabad"),default)) { }
        Assert.Equal(5,handler.Calls); Assert.False(settings.IsValid());
        settings.Enabled=false; Assert.True(settings.IsValid());
    }
    [Fact]
    public async Task HunterVerificationDoesNotPromoteCatchAllToVerified()
    {
        using var handler=new Handler(_=>Json("""{"data":{"status":"accept_all"}}""")); using var clients=new Clients(handler);
        var verifier=new HunterHiringEmailVerifier(clients,Options.Create(new HunterHiringOptions { ApiKey="test-only" }));
        Assert.Null(await verifier.VerifyAsync(new("Test Person","Test","test@example.org","Recruiter","HR","unknown",0,null,"identity",Provider:"Apollo"),default));
        Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task ApolloQuotaMetadataIsClassifiedWithoutExposingRawProviderMessage()
    {
        using var handler=new Handler(_=>new(HttpStatusCode.TooManyRequests) { Content=new StringContent("""{"error_details":{"code":"BILLING.CREDITS_EXHAUSTED","message":"fake-sensitive-provider-message"}}""") });
        using var clients=new Clients(handler); var provider=new ApolloHiringProvider(clients,Options.Create(ApolloSettings()));
        var error=await Assert.ThrowsAsync<HiringProviderException>(async ()=> { await foreach(var unused in provider.SearchAsync(new("Example","example.org","Engineer",""),default)) { } });
        Assert.Equal("ApolloQuotaExceeded",error.Code); Assert.DoesNotContain("fake-sensitive-provider-message",error.ToString(),StringComparison.Ordinal);
    }
    [Fact]
    public async Task UnexpectedJsonShapeHasAnExplicitSafeFailure()
    {
        using var handler=new Handler(_=>Json("[]")); using var clients=new Clients(handler); var provider=new ApolloHiringProvider(clients,Options.Create(ApolloSettings()));
        var error=await Assert.ThrowsAsync<HiringProviderException>(async ()=> { await foreach(var unused in provider.SearchAsync(new("Example","example.org","Engineer",""),default)) { } });
        Assert.Equal("ApolloInvalidResponse",error.Code);
    }
}
