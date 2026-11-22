using JobPortal.API.Extensions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Infrastructure.HiringOutreach;
using JobPortal.Shared.Models;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace JobPortal.API.Controllers;

[ApiController,Authorize(Roles="Candidate"),Route("api/candidate/outreach")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),EnableRateLimiting("HiringOutreach"),RequestSizeLimit(24576)]
public sealed class CandidateHiringOutreachController(IHiringOutreachService service,IOptions<GoogleHiringOptions> google,IDataProtectionProvider protection,TimeProvider clock) : ControllerBase
{
    private const string Cookie="__Host-CareerHarborGmail";
    private Guid Actor=>User.GetRequiredUserId();
    [HttpPost("discover")]
    public async Task<ActionResult<ApiResponse<HiringSession>>> Discover(DiscoverHiringTeamRequest request,CancellationToken ct)=>
        Ok(new ApiResponse<HiringSession>(await service.DiscoverAsync(Actor,request,ct)));
    [HttpGet("{sessionId:guid}")]
    public async Task<ActionResult<ApiResponse<HiringSession>>> Get(Guid sessionId,CancellationToken ct)=>
        Ok(new ApiResponse<HiringSession>(await service.GetAsync(Actor,sessionId,ct)));
    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<HiringSession>>>> History(CancellationToken ct,[FromQuery] int page=1,[FromQuery] int pageSize=20)=>
        Ok(new ApiResponse<IReadOnlyList<HiringSession>>(await service.HistoryAsync(Actor,page,pageSize,ct)));
    [HttpGet("resumes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<HiringResume>>>> Resumes(CancellationToken ct)=>
        Ok(new ApiResponse<IReadOnlyList<HiringResume>>(await service.ResumesAsync(Actor,ct)));
    [HttpPost("{sessionId:guid}/draft")]
    public async Task<ActionResult<ApiResponse<HiringDraft>>> Draft(Guid sessionId,HiringDraftRequest request,CancellationToken ct)=>
        Ok(new ApiResponse<HiringDraft>(await service.DraftAsync(Actor,sessionId,request,ct)));
    [HttpPost("{sessionId:guid}/send")]
    public async Task<ActionResult<ApiResponse<HiringSendResponse>>> Send(Guid sessionId,HiringSendRequest request,CancellationToken ct)=>
        Ok(new ApiResponse<HiringSendResponse>(await service.SendAsync(Actor,sessionId,request,ct)));
    [HttpGet("mailbox")]
    public async Task<ActionResult<ApiResponse<HiringMailbox>>> Mailbox(CancellationToken ct)=>
        Ok(new ApiResponse<HiringMailbox>(await service.MailboxAsync(Actor,ct)));
    [HttpPost("mailbox/google/connect")]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        var start=await service.ConnectAsync(Actor,ct);
        var ticket=protection.CreateProtector("CareerHarbor","HiringOutreach","OAuthBrowserTicket","v1")
            .Protect(JsonSerializer.Serialize(new BrowserTicket(start.AuthorizationUrl,start.BrowserBinding,clock.GetUtcNow().AddMinutes(10))));
        var endpoint=new Uri(new Uri(google.Value.CallbackUri),"/api/candidate/outreach/mailbox/google/authorize");
        return Ok(new ApiResponse<object>(new { authorizationUrl=endpoint.AbsoluteUri+"?ticket="+Uri.EscapeDataString(ticket) }));
    }
    // Top-level navigation sets a first-party cookie before leaving for Google. This also works
    // when frontend/API are on different sites and the browser blocks third-party cookies.
    [AllowAnonymous,HttpGet("mailbox/google/authorize")]
    public IActionResult AuthorizeGmail([FromQuery] string ticket)
    {
        Response.Headers["Referrer-Policy"]="no-referrer";
        if(ticket.Length>8192) return BadRequest(new { code="GmailAuthorizationFailed" });
        BrowserTicket? decoded;
        try { decoded=JsonSerializer.Deserialize<BrowserTicket>(protection.CreateProtector("CareerHarbor","HiringOutreach","OAuthBrowserTicket","v1").Unprotect(ticket)); }
        catch(Exception ex) when(ex is CryptographicException or JsonException) { return BadRequest(new { code="GmailAuthorizationFailed" }); }
        if(decoded is null || decoded.ExpiresAtUtc<=clock.GetUtcNow() || !Uri.TryCreate(decoded.AuthorizationUrl,UriKind.Absolute,out var url) ||
            url.Scheme!="https" || url.Host!="accounts.google.com" || url.AbsolutePath!="/o/oauth2/v2/auth") return BadRequest(new { code="GmailAuthorizationFailed" });
        Response.Cookies.Append(Cookie,decoded.Binding,new CookieOptions { HttpOnly=true,Secure=true,SameSite=SameSiteMode.Lax,Path="/",MaxAge=TimeSpan.FromMinutes(10),IsEssential=true });
        return Redirect(decoded.AuthorizationUrl);
    }
    private sealed record BrowserTicket(string AuthorizationUrl,string Binding,DateTimeOffset ExpiresAtUtc);
    [AllowAnonymous,HttpGet("mailbox/google/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? state,[FromQuery] string? code,CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"]="no-referrer";
        var binding=Request.Cookies[Cookie] ?? "";
        Response.Cookies.Delete(Cookie,new CookieOptions { Secure=true,SameSite=SameSiteMode.Lax,Path="/" });
        if(state is null || code is null) return BadRequest(new { code="GmailAuthorizationFailed" });
        try { await service.CallbackAsync(state,code,binding,ct); }
        catch(HiringProviderException) { return BadRequest(new { code="GmailAuthorizationFailed" }); }
        // Fixed configured destination only; no request-controlled return URL or OAuth material.
        return Redirect(google.Value.FrontendReturnUri);
    }
    [HttpDelete("mailbox")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    { await service.DisconnectAsync(Actor,ct); return NoContent(); }
}
