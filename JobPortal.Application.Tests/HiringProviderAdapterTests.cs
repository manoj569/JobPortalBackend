using System.Net;
using System.Text;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using JobPortal.Application;
using JobPortal.Infrastructure;
using JobPortal.Persistence;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Persistence.Repositories;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class HiringProviderAdapterTests
{
    [Fact]
    public void ProductionRegistrationsResolveOutreachWithoutOpeningAConnectionOrEnablingProviders()
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]="Host=127.0.0.1;Database=di_only" }).Build();
        var services=new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(configuration); services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddApplication(); services.AddPersistence(configuration); services.AddInfrastructure(configuration);
        services.AddSingleton<IAuditWriter>(new AuditWriterTestDouble());
        using var provider=services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });
        using var scope=provider.CreateScope();
        Assert.IsType<HiringOutreachService>(scope.ServiceProvider.GetRequiredService<IHiringOutreachService>());
        Assert.IsType<HunterEmployerContactProvider>(scope.ServiceProvider.GetRequiredService<IHiringContactProvider>());
        Assert.IsType<GoogleCandidateMailboxProvider>(scope.ServiceProvider.GetRequiredService<IHiringMailboxProvider>());
        Assert.False(scope.ServiceProvider.GetRequiredService<IHiringMailboxProvider>().IsAvailable);
        Assert.False(scope.ServiceProvider.GetRequiredService<IHiringContactProvider>().IsAvailable);
    }
    [Theory]
    [InlineData(401,"ContactProviderConfiguration")]
    [InlineData(400,"ContactProviderConfiguration")]
    [InlineData(429,"ContactProviderRateLimited")]
    [InlineData(503,"ContactProviderUnavailable")]
    public async Task HunterErrorsAreRedactedAndNeverAutomaticallyRetried(int status,string code)
    {
        using var handler=new Handler(_=>new((HttpStatusCode)status)); using var factory=new Clients(handler);
        var provider=new HunterEmployerContactProvider(factory,Options.Create(new HunterHiringOptions { ApiKey="test-only-key" }));
        var error=await Assert.ThrowsAsync<HiringProviderException>(()=>provider.DiscoverAsync(new("Example","example.org","Engineer","Engineering"),default));
        Assert.Equal(code,error.Code); Assert.DoesNotContain("test-only-key",error.ToString(),StringComparison.Ordinal); Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task HunterReturnsProviderIndependentVerifiedMetadata()
    {
        using var handler=new Handler(_=>Json("{\"data\":{\"emails\":[{\"value\":\"alex@example.org\",\"first_name\":\"Alex\",\"last_name\":\"Recruiter\",\"position\":\"Technical Recruiter\",\"department\":\"hr\",\"confidence\":95,\"verification\":{\"status\":\"valid\"}}]}}"));
        using var factory=new Clients(handler); var provider=new HunterEmployerContactProvider(factory,Options.Create(new HunterHiringOptions { ApiKey="test-only" }));
        var result=Assert.Single(await provider.DiscoverAsync(new("Example","example.org","Engineer","Engineering"),default));
        Assert.Equal("valid",result.Verification); Assert.Equal("Alex Recruiter",result.FullName); Assert.Equal(95,result.Confidence);
    }
    [Theory]
    [InlineData(200,"Sent")]
    [InlineData(401,"Failed")]
    [InlineData(429,"Failed")]
    [InlineData(503,"Unknown")]
    public async Task GmailRefreshesThenSendsOnlyOnceAndDistinguishesAmbiguousOutcomes(int status,string expected)
    {
        using var handler=new Handler(request=>request.RequestUri!.AbsolutePath=="/token" ? Json("{\"access_token\":\"test-access\"}") :
            status==200 ? Json("{\"id\":\"message-id\",\"threadId\":\"thread-id\"}") : new((HttpStatusCode)status));
        using var factory=new Clients(handler); var provider=new GoogleCandidateMailboxProvider(factory,Options.Create(Settings()));
        var result=await provider.SendAsync("test-refresh",new("candidate@gmail.com","alex@example.org","Reviewed subject","Reviewed body","resume.pdf","application/pdf",Encoding.UTF8.GetBytes("%PDF"),Guid.NewGuid().ToString("N")),default);
        Assert.Equal(expected,result.State); Assert.Equal(2,handler.Calls);
        if(status==200) Assert.Equal("message-id",result.MessageId);
    }
    [Fact]
    public async Task RevokedRefreshTokenDoesNotDispatchGmailMessage()
    {
        using var handler=new Handler(_=>new(HttpStatusCode.BadRequest) { Content=new StringContent("{\"error\":\"invalid_grant\"}",Encoding.UTF8,"application/json") }); using var factory=new Clients(handler);
        var provider=new GoogleCandidateMailboxProvider(factory,Options.Create(Settings()));
        var result=await provider.SendAsync("revoked",new("candidate@gmail.com","alex@example.org","subject","body","resume.pdf","application/pdf",[],Guid.NewGuid().ToString("N")),default);
        Assert.Equal("ReconnectGmailRequired",result.FailureCode); Assert.Equal("Failed",result.State); Assert.Equal(1,handler.Calls);
    }
    [Theory]
    [InlineData(429,"GmailRateLimited")]
    [InlineData(503,"GmailProviderUnavailable")]
    [InlineData(401,"GmailConfigurationRequired")]
    public async Task TransientRefreshOrClientConfigurationFailureDoesNotRevokeTheCandidatesGrant(int status,string code)
    {
        using var handler=new Handler(_=>new((HttpStatusCode)status)); using var factory=new Clients(handler);
        var provider=new GoogleCandidateMailboxProvider(factory,Options.Create(Settings()));
        var result=await provider.SendAsync("still-valid-refresh",new("candidate@gmail.com","alex@example.org","subject","body","resume.pdf","application/pdf",[],Guid.NewGuid().ToString("N")),default);
        Assert.Equal("Failed",result.State); Assert.Equal(code,result.FailureCode); Assert.NotEqual("ReconnectGmailRequired",result.FailureCode); Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public void OAuthUsesOnlyIdentityAndSendScopesWithPkceAndFixedCallback()
    {
        using var handler=new Handler(_=>throw new InvalidOperationException()); using var factory=new Clients(handler);
        var provider=new GoogleCandidateMailboxProvider(factory,Options.Create(Settings())); var start=provider.Authorize("test-state");
        var url=Uri.UnescapeDataString(start.Url);
        Assert.Contains("code_challenge_method=S256",url); Assert.Contains("access_type=offline",url); Assert.Contains("gmail.send",url);
        Assert.DoesNotContain("gmail.read",url,StringComparison.Ordinal); Assert.DoesNotContain("secret",url,StringComparison.Ordinal);
        Assert.Contains("redirect_uri=https://api.example.invalid/api/candidate/outreach/mailbox/google/callback",url); Assert.True(start.Verifier.Length>=43);
        Assert.Equal(0,handler.Calls);
    }
    private static GoogleHiringOptions Settings()=>new() { ClientId="test-client",ClientSecret="test-secret",CallbackUri="https://api.example.invalid/api/candidate/outreach/mailbox/google/callback",FrontendReturnUri="https://app.example.invalid/outreach" };
    private static HttpResponseMessage Json(string content)=>new(HttpStatusCode.OK) { Content=new StringContent(content,Encoding.UTF8,"application/json") };
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(response(request)); }
    }
    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory,IDisposable
    {
        private readonly HttpClient client=new(handler,false);
        public HttpClient CreateClient(string name)=>client;
        public void Dispose()=>client.Dispose();
    }
}
