using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.HiringOutreach;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class HiringOutreachTests
{
    private static readonly string[] AnonymousOAuthActions=["AuthorizeGmail","Callback"];
    [Fact]
    public async Task CandidateReviewsConnectsAndExplicitlySendsIndividualAttachmentsExactlyOnce()
    {
        using var f=new Fixture(); var session=await f.Discover();
        Assert.Equal("Found",session.State); Assert.False(session.Mailbox.Connected); Assert.True(session.Usage.Charged);
        Assert.Equal(1,f.Hunter.Calls); Assert.Equal(2,session.Contacts.Count); Assert.Empty(f.Google.Messages);
        var draft=await f.Service.DraftAsync(f.User.Id,session.SessionId,new(session.Contacts.Select(x=>x.Id).ToArray(),f.Resume.Id),default);
        Assert.True(draft.Editable); Assert.Contains("C#",draft.Body); Assert.Contains("Backend Engineer",draft.Subject);
        Assert.DoesNotContain("years",draft.Body,StringComparison.OrdinalIgnoreCase); Assert.Empty(f.Google.Messages);
        await f.Connect(); Assert.True((await f.Service.MailboxAsync(f.User.Id,default)).Connected); Assert.Empty(f.Google.Messages);
        var request=new HiringSendRequest(draft.Id,session.Contacts.Select(x=>x.Id).ToArray(),"My reviewed subject",draft.Body+"\nCandidate edit",f.Resume.Id,Guid.NewGuid());
        var sent=await f.Service.SendAsync(f.User.Id,session.SessionId,request,default);
        Assert.Equal("Sent",sent.State); Assert.Equal(2,f.Google.Messages.Count);
        Assert.All(f.Google.Messages,m=> { Assert.Equal("candidate@gmail.com",m.From); Assert.Equal("My reviewed subject",m.Subject); Assert.Equal(f.Storage.Bytes,m.Attachment); Assert.Contains("Candidate edit",m.Body); Assert.DoesNotContain(',',m.To); });
        Assert.Contains(f.Google.Messages,m=>m.Body.StartsWith("Dear Alex,",StringComparison.Ordinal));
        Assert.Contains(f.Google.Messages,m=>m.Body.StartsWith("Dear Blair,",StringComparison.Ordinal));
        var repeated=await f.Service.SendAsync(f.User.Id,session.SessionId,request,default);
        Assert.Equal("Sent",repeated.State); Assert.Equal(2,f.Google.Messages.Count);
        var cache=await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,Guid.NewGuid()),default);
        Assert.Equal(session.SessionId,cache.SessionId); Assert.False(cache.Usage.Charged); Assert.Equal(1,f.Hunter.Calls);
        Assert.Equal(2,await f.Db.Set<HiringOutreachSend>().CountAsync(x=>x.State=="Sent"));
        Assert.DoesNotContain("refresh",JsonSerializer.Serialize(await f.Service.MailboxAsync(f.User.Id,default)),StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Service.GetAsync(f.Other.Id,session.SessionId,default));
        Assert.False((await f.Service.MailboxAsync(f.Other.Id,default)).Connected);
        await Assert.ThrowsAsync<ConflictException>(()=>f.Service.SendAsync(f.User.Id,session.SessionId,request with { Body="Different content" },default));
    }
    [Theory]
    [InlineData("ContactProviderRateLimited")]
    [InlineData("ContactProviderConfiguration")]
    [InlineData("ContactProviderUnavailable")]
    public async Task ProviderFailureIsSafeAndSameKeyNeverRepeatsPaidSearch(string error)
    {
        using var f=new Fixture(); f.Hunter.Error=error; var key=Guid.NewGuid();
        var first=await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,key),default);
        Assert.Equal("ProviderUnavailable",first.State); Assert.Equal(error,first.FailureCode); Assert.Empty(first.Contacts);
        Assert.Equal(error=="ContactProviderUnavailable" ? 0 : 1,first.Usage.Remaining);
        Assert.Equal(1,f.Hunter.Calls);
        var replay=await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,key),default);
        Assert.Equal(first.SessionId,replay.SessionId); Assert.Equal(1,f.Hunter.Calls);
    }
    [Fact]
    public async Task NoContactsAndMissingDomainNeverInventAddresses()
    {
        using var f=new Fixture(); f.Hunter.Results=[];
        Assert.Equal("NoContactsFound",(await f.Discover()).State);
        f.Job.Company.NormalizedWebsiteDomain=null; f.Job.Company.WebsiteUrl=null; await f.Db.SaveChangesAsync();
        Assert.Equal("NoContactsFound",(await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default)).State);
        Assert.Equal(1,f.Hunter.Calls); Assert.Empty(f.Google.Messages);
    }
    [Fact]
    public async Task QuotasAndUnavailableProviderFailBeforeAnotherPaidDispatch()
    {
        using var f=new Fixture(); f.Limits.PromotionalLifetimeDiscoveries=0;
        Assert.Equal("QuotaExceeded",(await f.Discover()).State); Assert.Equal(0,f.Hunter.Calls);
        f.Hunter.Available=false; Assert.Equal("ProviderUnavailable",(await f.Service.DiscoverAsync(f.Other.Id,new(f.Job.Id,Guid.NewGuid()),default)).State);
        Assert.Equal(0,f.Hunter.Calls);
        await Assert.ThrowsAsync<UnauthorizedException>(()=>f.Service.DiscoverAsync(Guid.NewGuid(),new(f.Job.Id,Guid.NewGuid()),default));
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Service.DiscoverAsync(f.User.Id,new(Guid.NewGuid(),Guid.NewGuid()),default));
    }
    [Fact]
    public async Task ConcurrentDiscoveryClaimsOnceAcrossSeparateContexts()
    {
        using var f=new Fixture(); var key=Guid.NewGuid();
        var sessions=await Task.WhenAll(Enumerable.Range(0,4).Select(async _=> { await using var db=new JobPortalDbContext(f.DbOptions); return await f.For(db).DiscoverAsync(f.User.Id,new(f.Job.Id,key),default); }));
        Assert.Single(sessions.Select(x=>x.SessionId).Distinct()); Assert.Equal(1,f.Hunter.Calls);
        Assert.Equal(1,await f.Db.Set<HiringOutreachSession>().CountAsync(x=>x.DispatchedAtUtc!=null));
    }
    [Fact]
    public async Task OAuthIsBrowserBoundOneTimeAndReconnectAndDisconnectHideCredentials()
    {
        using var f=new Fixture(); var start=await f.Service.ConnectAsync(f.User.Id,default);
        await Assert.ThrowsAsync<BadRequestException>(()=>f.Service.CallbackAsync(f.Google.State,"code",new string('X',64),default));
        var connected=await f.Service.CallbackAsync(f.Google.State,"code",start.BrowserBinding,default);
        Assert.True(connected.Connected); Assert.Equal("candidate@gmail.com",connected.Email);
        await Assert.ThrowsAsync<BadRequestException>(()=>f.Service.CallbackAsync(f.Google.State,"code",start.BrowserBinding,default));
        Assert.Equal(1,f.Google.Exchanges);
        var stored=await f.Db.Set<HiringMailboxSecret>().SingleAsync(); Assert.DoesNotContain("fake-refresh-token",stored.ProtectedRefreshToken,StringComparison.Ordinal);
        await f.Connect(); Assert.Equal(2,f.Google.Exchanges); Assert.Single(await f.Db.CandidateMailboxConnections.ToArrayAsync());
        await f.Service.DisconnectAsync(f.User.Id,default); Assert.False((await f.Service.MailboxAsync(f.User.Id,default)).Connected);
        Assert.Equal("",(await f.Db.Set<HiringMailboxSecret>().SingleAsync()).ProtectedRefreshToken); Assert.Equal(1,f.Google.Revocations);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AiOrderingOrFailureUsesOnlyExistingFactsAndDoesNotSend(bool aiSucceeds)
    {
        using var f=new Fixture(); f.Limits.AiDraftingEnabled=true; f.Ai.Succeed=aiSucceeds;
        var s=await f.Discover(); var draft=await f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[0].Id],f.Resume.Id),default);
        Assert.Equal(aiSucceeds ? "AIOrderedVerifiedFacts" : "Deterministic",draft.Source);
        Assert.Contains("C#",draft.Body); Assert.DoesNotContain("10 years",draft.Body,StringComparison.OrdinalIgnoreCase); Assert.Empty(f.Google.Messages);
        var foreign=new ResumeDocumentBlob { OwnerUserId=f.Other.Id,StorageKey="other",OriginalFileName="other.pdf" }; f.Db.Add(foreign); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[0].Id],foreign.Id),default));
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[0].Id],Guid.NewGuid()),default));
    }
    [Theory]
    [InlineData("Unknown",false)]
    [InlineData("Failed",false)]
    [InlineData("Sent",true)]
    public async Task PerRecipientResultsAreDurableAndReplaysNeverResend(string secondState,bool allSent)
    {
        using var f=new Fixture(); var s=await f.Discover(); await f.Connect();
        var draft=await f.Service.DraftAsync(f.User.Id,s.SessionId,new(s.Contacts.Select(x=>x.Id).ToArray(),f.Resume.Id),default);
        f.Google.SecondState=secondState;
        var request=new HiringSendRequest(draft.Id,s.Contacts.Select(x=>x.Id).ToArray(),draft.Subject,draft.Body,f.Resume.Id,Guid.NewGuid());
        var response=await f.Service.SendAsync(f.User.Id,s.SessionId,request,default);
        Assert.Equal(allSent ? "Sent" : "PartiallySent",response.State);
        await f.Service.SendAsync(f.User.Id,s.SessionId,request,default); Assert.Equal(2,f.Google.Messages.Count);
        await Assert.ThrowsAsync<ConflictException>(()=>f.Service.SendAsync(f.User.Id,s.SessionId,request with { RequestKey=Guid.NewGuid() },default));
    }
    [Fact]
    public async Task SendRequiresConnectedMailboxAndBoundedOwnedRecipientsAndQuota()
    {
        using var f=new Fixture(); var s=await f.Discover();
        var draft=await f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[0].Id],f.Resume.Id),default);
        var request=new HiringSendRequest(draft.Id,[s.Contacts[0].Id],draft.Subject,draft.Body,f.Resume.Id,Guid.NewGuid());
        var missing=await Assert.ThrowsAsync<AppException>(()=>f.Service.SendAsync(f.User.Id,s.SessionId,request,default)); Assert.Equal("ReconnectGmailRequired",missing.Code);
        await f.Connect();
        await Assert.ThrowsAsync<BadRequestException>(()=>f.Service.SendAsync(f.User.Id,s.SessionId,request with { RecipientIds=[Guid.NewGuid()] },default));
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Service.SendAsync(f.Other.Id,s.SessionId,request,default));
        f.Limits.DailySends=1;
        var previous=await f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[1].Id],f.Resume.Id),default);
        await f.Service.SendAsync(f.User.Id,s.SessionId,new(previous.Id,[s.Contacts[1].Id],previous.Subject,previous.Body,f.Resume.Id,Guid.NewGuid()),default);
        var quota=await Assert.ThrowsAsync<AppException>(()=>f.Service.SendAsync(f.User.Id,s.SessionId,request,default)); Assert.Equal("QuotaExceeded",quota.Code);
        Assert.Single(f.Google.Messages);
    }
    [Fact]
    public async Task DoubleClickAcrossContextsSendsOnce()
    {
        using var f=new Fixture(); var s=await f.Discover(); await f.Connect();
        var draft=await f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[0].Id],f.Resume.Id),default);
        var request=new HiringSendRequest(draft.Id,[s.Contacts[0].Id],draft.Subject,draft.Body,f.Resume.Id,Guid.NewGuid());
        await Task.WhenAll(Enumerable.Range(0,3).Select(async _=> { await using var db=new JobPortalDbContext(f.DbOptions); return await f.For(db).SendAsync(f.User.Id,s.SessionId,request,default); }));
        Assert.Single(f.Google.Messages); Assert.Single(await f.Db.Set<HiringOutreachSend>().ToArrayAsync());
    }
    [Fact]
    public async Task TimedOutDispatchRemainsUnresolvedAndIsNeverAutomaticallyRetried()
    {
        using var f=new Fixture(); var s=await f.Discover(); await f.Connect();
        var draft=await f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[0].Id],f.Resume.Id),default);
        var request=new HiringSendRequest(draft.Id,[s.Contacts[0].Id],draft.Subject,draft.Body,f.Resume.Id,Guid.NewGuid());
        f.Google.ThrowAfterDispatch=true;
        Assert.Equal("SendingUnresolved",(await f.Service.SendAsync(f.User.Id,s.SessionId,request,default)).State);
        Assert.Equal("Unknown",(await f.Db.Set<HiringOutreachSend>().SingleAsync()).State);
        await f.Service.SendAsync(f.User.Id,s.SessionId,request,default); Assert.Single(f.Google.Messages);
        await Assert.ThrowsAsync<ConflictException>(()=>f.Service.SendAsync(f.User.Id,s.SessionId,request with { RequestKey=Guid.NewGuid() },default));
    }
    [Fact]
    public async Task OAuthExpiryAndImmutableReviewContentAreEnforced()
    {
        using var f=new Fixture(); var start=await f.Service.ConnectAsync(f.User.Id,default);
        var auth=await f.Db.Set<HiringMailboxAuthorization>().SingleAsync(); auth.ExpiresAtUtc=DateTime.UtcNow.AddMinutes(-1); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BadRequestException>(()=>f.Service.CallbackAsync(f.Google.State,"code",start.BrowserBinding,default)); Assert.Equal(0,f.Google.Exchanges);
        var s=await f.Discover(); var draft=await f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[0].Id],f.Resume.Id),default);
        (await f.Db.Set<HiringOutreachDraft>().SingleAsync(x=>x.Id==draft.Id)).Body="Unreviewed replacement";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Db.SaveChangesAsync());
    }
    [Fact]
    public async Task CachedDiscoveryRequestKeyCannotLaterBeReboundToAnotherJob()
    {
        using var f=new Fixture(); await f.Discover(); var alias=Guid.NewGuid();
        await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,alias),default);
        await Assert.ThrowsAsync<ConflictException>(()=>f.Service.DiscoverAsync(f.User.Id,new(Guid.NewGuid(),alias),default));
        Assert.Equal(1,f.Hunter.Calls);
    }
    [Fact]
    public async Task ResumeRemovalAndHeaderInjectionCannotDispatchMail()
    {
        using var f=new Fixture(); var session=await f.Discover(); await f.Connect();
        var draft=await f.Service.DraftAsync(f.User.Id,session.SessionId,new([session.Contacts[0].Id],f.Resume.Id),default);
        var request=new HiringSendRequest(draft.Id,[session.Contacts[0].Id],draft.Subject,draft.Body,f.Resume.Id,Guid.NewGuid());
        await Assert.ThrowsAsync<BadRequestException>(()=>f.Service.SendAsync(f.User.Id,session.SessionId,request with { Subject="subject\r\nBcc: arbitrary@example.org" },default));
        f.Resume.IsDeleted=true; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Service.SendAsync(f.User.Id,session.SessionId,request,default)); Assert.Empty(f.Google.Messages);
        await Assert.ThrowsAsync<UnauthorizedException>(()=>f.Service.SupportAsync(f.User.Id,1,20,default));
    }
    [Fact]
    public void CandidateRoutesAreAuthorizedAndOnlyProtectedOAuthHandshakeAndCallbackAreAnonymous()
    {
        var type=typeof(JobPortal.API.Controllers.CandidateHiringOutreachController);
        var authorization=Assert.Single(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),true).Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal("Candidate",authorization.Roles);
        var anonymous=type.GetMethods().Where(x=>x.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute),true).Length>0).ToArray();
        Assert.Equal(AnonymousOAuthActions,anonymous.Select(x=>x.Name).Order().ToArray());
    }
    [Fact]
    public async Task HunterTimeoutRetainsUncertainUsageWithoutRediscovery()
    {
        using var f=new Fixture(); f.Hunter.Timeout=true; var key=Guid.NewGuid();
        var result=await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,key),default);
        Assert.Equal("ProviderUnavailable",result.State); Assert.Equal("DiscoveryOutcomeUnknown",result.FailureCode);
        Assert.True(result.Usage.Charged); Assert.Equal(0,result.Usage.Remaining);
        Assert.Equal("Uncertain",(await f.Db.Set<HiringOutreachSession>().SingleAsync()).UsageState);
        await f.Service.DiscoverAsync(f.User.Id,new(f.Job.Id,key),default); Assert.Equal(1,f.Hunter.Calls);
    }
    [Fact]
    public async Task AiTimeoutFallsBackAndOversizedResumeCannotCreateSendableDraft()
    {
        using var f=new Fixture(); f.Limits.AiDraftingEnabled=true; f.Ai.Timeout=true; var s=await f.Discover();
        var draft=await f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[0].Id],f.Resume.Id),default);
        Assert.Equal("Deterministic",draft.Source); Assert.Contains("C#",draft.Body); Assert.Empty(f.Google.Messages);
        f.Resume.FileLength=f.Limits.MaximumAttachmentBytes+1L; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BadRequestException>(()=>f.Service.DraftAsync(f.User.Id,s.SessionId,new([s.Contacts[0].Id],f.Resume.Id),default));
        Assert.Empty(f.Google.Messages);
    }
    [Fact]
    public void TechnicalRecruitersAndEngineeringPeersMatchDeveloperJobsWithoutInventedDepartments()
    {
        var result=HiringContactPolicy.Select(new[] { Contact("generic@example.org","Recruiter") with { Department="HR" },
            Contact("technical@example.org","Technical Recruiter") with { Department="HR" },Contact("peer@example.org","Software Engineer") with { Department="" } },
            "example.org","Full Stack Developer","",new());
        Assert.Equal("technical@example.org",result[0].Email); Assert.Equal("generic@example.org",result[1].Email); Assert.Equal("peer@example.org",result[2].Email);
    }
    [Fact]
    public async Task OAuthBrowserHandshakeSetsFirstPartyCookieAndRejectsTamperedOrUnapprovedRedirects()
    {
        using var f=new Fixture(); var protection=new EphemeralDataProtectionProvider();
        var settings=Options.Create(new GoogleHiringOptions { CallbackUri="https://api.example.invalid/api/candidate/outreach/mailbox/google/callback",FrontendReturnUri="https://frontend.example.invalid/outreach" });
        var controller=new JobPortal.API.Controllers.CandidateHiringOutreachController(f.Service,settings,protection,TimeProvider.System)
        { ControllerContext=new ControllerContext { HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,f.User.Id.ToString())],"Test")) } } };
        var result=Assert.IsType<OkObjectResult>(await controller.Connect(default));
        using var document=JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var start=new Uri(document.RootElement.GetProperty("Data").GetProperty("authorizationUrl").GetString()!);
        Assert.Equal("api.example.invalid",start.Host); var ticket=QueryHelpers.ParseQuery(start.Query)["ticket"].ToString();
        var redirect=Assert.IsType<RedirectResult>(controller.AuthorizeGmail(ticket)); Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth",redirect.Url,StringComparison.Ordinal);
        var cookie=controller.Response.Headers.SetCookie.ToString(); Assert.Contains("__Host-CareerHarborGmail",cookie); Assert.Contains("httponly",cookie,StringComparison.OrdinalIgnoreCase); Assert.Contains("secure",cookie,StringComparison.OrdinalIgnoreCase);
        Assert.IsType<BadRequestObjectResult>(controller.AuthorizeGmail(ticket+"tampered"));
        var forged=protection.CreateProtector("CareerHarbor","HiringOutreach","OAuthBrowserTicket","v1")
            .Protect(JsonSerializer.Serialize(new { AuthorizationUrl="https://evil.example.invalid/redirect",Binding="test",ExpiresAtUtc=DateTimeOffset.UtcNow.AddMinutes(1) }));
        Assert.IsType<BadRequestObjectResult>(controller.AuthorizeGmail(forged)); Assert.Equal(0,f.Google.Exchanges); Assert.Empty(f.Google.Messages);
    }
    [Fact]
    public void RankingDeduplicatesAndExcludesInvalidUnrelatedAndUnverifiedContacts()
    {
        var options=new HiringOutreachOptions();
        var source=new[]{Contact("sales@example.org","Account Executive"),Contact("tech@example.org","Technical Recruiter"),Contact("TECH@example.org","Recruiter"),
            Contact("invalid@example.org","Recruiter") with { Verification="invalid" },Contact("unknown@example.org","Recruiter") with { Verification="unknown",Confidence=99 },
            Contact("other@elsewhere.org","Recruiter"),Contact("manager@example.org","Engineering Manager")};
        var result=HiringContactPolicy.Select(source,"example.org","Software Engineer","Engineering",options);
        Assert.Equal(2,result.Count); Assert.Equal("tech@example.org",result[0].Email); Assert.Equal("manager@example.org",result[1].Email);
        options.AllowUnverifiedContacts=true; Assert.Equal(3,HiringContactPolicy.Select(source,"example.org","Software Engineer","Engineering",options).Count);
    }
    [Fact]
    public void GmailMimeHasSingleRecipientPlainTextAndActualAttachment()
    {
        var bytes=Encoding.UTF8.GetBytes("%PDF-test");
        var mime=Encoding.UTF8.GetString(GoogleCandidateMailboxProvider.Mime(new("candidate@gmail.com","recruiter@example.org","Subject","Body","resume.pdf","application/pdf",bytes,Guid.NewGuid().ToString("N"))));
        Assert.Contains("To: recruiter@example.org\r\n",mime); Assert.DoesNotContain("Bcc:",mime); Assert.DoesNotContain("Cc:",mime);
        Assert.Contains(Convert.ToBase64String(bytes),mime); Assert.Contains("filename=\"resume.pdf\"",mime);
    }
    [HiringPostgresFact]
    public async Task PostgreSqlAdvisoryClaimsPreventDuplicateDiscoveryAndDuplicateSends()
    {
        var connection=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HIRING_OUTREACH_TEST_POSTGRES"));
        Assert.Equal("127.0.0.1",connection.Host); Assert.Equal(55439,connection.Port); Assert.Equal("hiring_outreach_verify",connection.Database);
        using var f=new Fixture(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(connection.ConnectionString,p=>p.EnableRetryOnFailure(3)).Options);
        var key=Guid.NewGuid();
        var discovered=await Task.WhenAll(Enumerable.Range(0,4).Select(async _=> { await using var db=new JobPortalDbContext(f.DbOptions); return await f.For(db).DiscoverAsync(f.User.Id,new(f.Job.Id,key),default); }));
        Assert.Single(discovered.Select(x=>x.SessionId).Distinct()); Assert.Equal(1,f.Hunter.Calls);
        var session=await f.Service.GetAsync(f.User.Id,discovered[0].SessionId,default); await f.Connect();
        var draft=await f.Service.DraftAsync(f.User.Id,session.SessionId,new([session.Contacts[0].Id],f.Resume.Id),default);
        var send=new HiringSendRequest(draft.Id,[session.Contacts[0].Id],draft.Subject,draft.Body,f.Resume.Id,Guid.NewGuid());
        await Task.WhenAll(Enumerable.Range(0,4).Select(async _=> { await using var db=new JobPortalDbContext(f.DbOptions); return await f.For(db).SendAsync(f.User.Id,session.SessionId,send,default); }));
        Assert.Single(f.Google.Messages);
        Assert.Equal(1,await f.Db.Set<HiringOutreachSession>().CountAsync(x=>x.CandidateId==f.User.Id));
        Assert.Equal(1,await f.Db.Set<HiringOutreachSend>().CountAsync(x=>x.CandidateId==f.User.Id && x.State=="Sent"));
    }
    private sealed class HiringPostgresFactAttribute : FactAttribute
    {
        public HiringPostgresFactAttribute()
        { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HIRING_OUTREACH_TEST_POSTGRES"))) Skip="Requires explicitly disposable localhost hiring_outreach_verify PostgreSQL."; }
    }
    private static HiringProviderContact Contact(string email,string title)=>new("Alex Recruiter","Alex",email,title,"Engineering","valid",99,null,"licensed-reference");
    private sealed class Fixture : IDisposable
    {
        public DbContextOptions<JobPortalDbContext> DbOptions { get; }=new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public JobPortalDbContext Db { get; }
        public User User { get; }=new() { FirstName="Casey",LastName="Candidate",Email="candidate@example.invalid",Status=UserStatus.Active };
        public User Other { get; }=new() { Email="other@example.invalid",Status=UserStatus.Active };
        public Job Job { get; }
        public ResumeDocumentBlob Resume { get; }
        public HiringOutreachOptions Limits { get; }=new() { Enabled=true,DailySends=5 };
        public FakeContacts Hunter { get; }=new(); public FakeGoogle Google { get; }=new(); public FakeResume Storage { get; }=new(); public FakeAi Ai { get; }=new();
        public IHiringContactProvider? DiscoveryOverride;
        public CoresignalHiringOptions CoresignalSettings { get; }=new();
        private IHiringCredentialProtector Protector { get; }=new HiringCredentialProtector(new EphemeralDataProtectionProvider());
        private IHiringSharedContactProtector SharedProtector { get; }=new HiringSharedContactProtector(new EphemeralDataProtectionProvider());
        public HiringOutreachService Service=>For(Db);
        public HiringOutreachService For(JobPortalDbContext db)=>new(db,Options.Create(Limits),DiscoveryOverride ?? Hunter,Google,Protector,Storage,new FakeExtractor(),new AuditWriterTestDouble(),Ai,TimeProvider.System,NullLogger<HiringOutreachService>.Instance,SharedProtector,Options.Create(CoresignalSettings));
        public Fixture(DbContextOptions<JobPortalDbContext>? dbOptions=null)
        {
            if(dbOptions is not null) DbOptions=dbOptions;
            Db=new(DbOptions); var role=Db.Roles.SingleOrDefault(x=>x.Name=="Candidate");
            if(role is null) { role=new Role { Name="Candidate",NormalizedName="CANDIDATE" }; Db.Add(role); }
            User.Role=Other.Role=role; User.RoleId=Other.RoleId=role.Id;
            var suffix=Guid.NewGuid().ToString("N"); User.Email=$"outreach-{suffix}@example.invalid"; User.NormalizedEmail=User.Email.ToUpperInvariant();
            Other.Email=$"outreach-other-{suffix}@example.invalid"; Other.NormalizedEmail=Other.Email.ToUpperInvariant();
            var company=new Company { Name="Example "+suffix,NormalizedName="EXAMPLE "+suffix,Slug="outreach-"+suffix,WebsiteUrl="https://example.org",NormalizedWebsiteDomain="example.org",OwnerUserId=User.Id,OwnerUser=User };
            Job=new Job { Title="Backend Engineer",Slug="outreach-"+suffix,ReferenceNumber="outreach-"+suffix,Description="C# services",Company=company,CompanyId=company.Id,Status=JobStatus.Published,PublishedAtUtc=DateTime.UtcNow.AddDays(-1) };
            Resume=new ResumeDocumentBlob { OwnerUserId=User.Id,StorageKey=suffix+".pdf",OriginalFileName="resume.pdf",ContentType="application/pdf",Extension=".pdf",FileLength=Storage.Bytes.Length,Sha256=Convert.ToHexString(SHA256.HashData(Storage.Bytes)),Content=Storage.Bytes };
            Db.AddRange(User,Other,company,Job,Resume,new CandidateSkill { UserId=User.Id,Name="C#",NormalizedName="C#" }); Db.SaveChanges();
        }
        public Task<HiringSession> Discover()=>Service.DiscoverAsync(User.Id,new(Job.Id,Guid.NewGuid()),default);
        public async Task Connect() { var start=await Service.ConnectAsync(User.Id,default); await Service.CallbackAsync(Google.State,"fake-code",start.BrowserBinding,default); }
        public void Dispose()=>Db.Dispose();
    }
    private sealed class FakeContacts : IHiringContactProvider
    {
        public int Calls; public bool Available=true; public string? Error; public bool Timeout;
        public Func<Task>? BeforeReturn;
        public IReadOnlyList<HiringProviderContact> Results=[Contact("alex@example.org","Technical Recruiter"),Contact("blair@example.org","Recruiter") with { FullName="Blair Recruiter",FirstName="Blair" }];
        public string Name=>"FakeHunter"; public bool IsAvailable=>Available;
        public async Task<IReadOnlyList<HiringProviderContact>> DiscoverAsync(HiringDiscoveryContext context,CancellationToken ct)
        { Interlocked.Increment(ref Calls); if(BeforeReturn is not null) await BeforeReturn(); if(Timeout) throw new OperationCanceledException("Fake Hunter timeout"); if(Error is not null) throw new HiringProviderException(Error); return Results; }
    }
    private sealed class FakeGoogle : IHiringMailboxProvider
    {
        public bool IsAvailable=>true; public string State=""; public int Exchanges; public int Revocations; public string SecondState="Sent"; public bool ThrowAfterDispatch;
        public List<HiringMailMessage> Messages { get; }=[];
        public HiringOAuthChallenge Authorize(string state) { State=state; return new("https://accounts.google.com/o/oauth2/v2/auth?state="+state,"fake-pkce-verifier"); }
        public Task<HiringOAuthGrant> ExchangeAsync(string code,string verifier,CancellationToken ct) { Exchanges++; return Task.FromResult(new HiringOAuthGrant("google-account","candidate@gmail.com","fake-refresh-token",GoogleCandidateMailboxProvider.Scopes)); }
        public Task RevokeAsync(string refreshToken,CancellationToken ct) { Revocations++; return Task.CompletedTask; }
        public Task<HiringMailResult> SendAsync(string refreshToken,HiringMailMessage message,CancellationToken ct)
        { lock(Messages) { Messages.Add(message); if(ThrowAfterDispatch) throw new OperationCanceledException("Fake network timeout"); return Task.FromResult(new HiringMailResult(Messages.Count==2 ? SecondState : "Sent","fake-"+Messages.Count,"thread")); } }
    }
    private sealed class FakeResume : IResumeStorage
    {
        public byte[] Bytes { get; }=Encoding.UTF8.GetBytes("%PDF-resume-test");
        public Task<Stream?> OpenReadAsync(Guid ownerUserId,string storageKey,Guid? resumeId=null,string? originalFileName=null,string? contentType=null,CancellationToken cancellationToken=default)=>Task.FromResult<Stream?>(new MemoryStream(Bytes));
        public Task<string> StoreAsync(Guid ownerUserId,Stream content,string extension,Guid? resumeId=null,string? originalFileName=null,string? contentType=null,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task DeleteAsync(Guid ownerUserId,string storageKey,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class FakeExtractor : IResumeTextExtractor
    {
        public Task<string> ExtractAsync(Stream content,string extension,CancellationToken cancellationToken=default)=>Task.FromResult("C# services experience declared in this selected resume.");
    }
    private sealed class FakeAi : IAIApplyLanguageModel
    {
        public bool Succeed; public bool Timeout;
        public Task<AIApplyLanguageModelResult?> GenerateAsync(AIApplyLanguageModelRequest request,CancellationToken ct)
        { if(Timeout) throw new OperationCanceledException("Fake AI timeout"); return Task.FromResult<AIApplyLanguageModelResult?>(Succeed ? new("[\"interest\",\"relevance\",\"resume\",\"cta\"]",1,true,0,0) : null); }
    }
}
