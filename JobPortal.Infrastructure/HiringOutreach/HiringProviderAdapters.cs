using System.Net;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using JobPortal.Application.Features.HiringOutreach;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed class HunterHiringOptions
{
    public string ApiKey { get; set; } = "";
    public int MaximumRequests { get; set; } = 4;
}
public sealed class GoogleHiringOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CallbackUri { get; set; } = "";
    public string FrontendReturnUri { get; set; } = "";
    public bool IsAvailable=>ClientId.Length>0 && ClientSecret.Length>0 && SafeUri(CallbackUri) && SafeUri(FrontendReturnUri);
    private static bool SafeUri(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri) &&
        (uri.Scheme=="https" || uri.IsLoopback && uri.Scheme=="http") && uri.UserInfo.Length==0 && uri.Fragment.Length==0;
}
public sealed class HiringCredentialProtector(IDataProtectionProvider protection) : IHiringCredentialProtector
{
    private IDataProtector Purpose(Guid owner)=>protection.CreateProtector("CareerHarbor","HiringOutreach","Gmail","v1",owner.ToString("N"));
    public string Protect(Guid owner,string secret)=>Purpose(owner).Protect(secret);
    public string Unprotect(Guid owner,string secret)=>Purpose(owner).Unprotect(secret);
}
public sealed class HiringSharedContactProtector(IDataProtectionProvider protection) : IHiringSharedContactProtector
{
    private IDataProtector Purpose(string domain)=>protection.CreateProtector("CareerHarbor","HiringOutreach","SharedContacts","v1",domain);
    public string Protect(string domain,string value)=>Purpose(domain).Protect(value);
    public string Unprotect(string domain,string value)=>Purpose(domain).Unprotect(value);
}
public sealed class HunterEmployerContactProvider(IHttpClientFactory clients,IOptions<HunterHiringOptions> settings) : IHiringContactProvider
{
    public const string ClientName="HiringHunter";
    public string Name=>"Hunter";
    public bool IsAvailable=>!string.IsNullOrWhiteSpace(settings.Value.ApiKey);
    public async Task<IReadOnlyList<HiringProviderContact>> DiscoverAsync(HiringDiscoveryContext context,CancellationToken ct)
    {
        if(!IsAvailable) throw new HiringProviderException("ContactProviderUnavailable");
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.Value.MaximumRequests,1,4)*20+10));
        var family=HiringContactPolicy.Family(context.JobTitle,context.Department,context.Description);
        var department=family switch { "Engineering"=>"it", "Data"=>"research,it", "Operations"=>"operations", "Finance"=>"finance", "Product"=>"product", "Design"=>"design", "Marketing"=>"marketing", "Sales"=>"sales", _=>"hr" };
        var stages=new[] {
            "job_titles=Recruiter,Talent%20Acquisition,Recruitment,Talent%20Partner",
            "department=hr",
            "department="+department+"&job_titles=Manager,Lead",
            "department="+department };
        var result=new List<HiringProviderContact>();
        foreach(var stage in stages.Take(Math.Clamp(settings.Value.MaximumRequests,1,4)))
        {
            // No paid retry of the same search. Later stages target different official filters.
            try { result.AddRange(await Search(context.Domain,stage,budget.Token)); }
            catch(HiringProviderException ex) when(result.Count>0 && ex.Code is "ContactProviderRateLimited" or "ContactProviderConfiguration")
            { break; } // Confirmed rejection does not invalidate usable earlier observations.
            var selected=HiringContactPolicy.Select(result,context,new HiringOutreachOptions());
            if(selected.Count(x=>HiringContactPolicy.Score(x,context)>=70)>=2) break;
        }
        return result.DistinctBy(x=>x.Email,StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
    }
    private async Task<IReadOnlyList<HiringProviderContact>> Search(string domain,string stage,CancellationToken ct)
    {
        // Official Domain Search filters, reviewed 2026-11-23. No guessed identities/email patterns.
        var url="https://api.hunter.io/v2/domain-search?domain="+Uri.EscapeDataString(domain)+"&limit=10&type=personal&verification_status=valid&required_field=full_name,position&"+stage;
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.Add("X-API-KEY",settings.Value.ApiKey);
        using var response=await clients.CreateClient(ClientName).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode) throw new HiringProviderException(response.StatusCode switch {
            HttpStatusCode.TooManyRequests=>"ContactProviderRateLimited",HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>"ContactProviderConfiguration",_=>"ContactProviderUnavailable" });
        if(response.Content.Headers.ContentLength>512*1024) throw new HiringProviderException("ContactProviderInvalidResponse");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);
        using var bounded=new MemoryStream(); var chunk=new byte[8192]; int read;
        while((read=await stream.ReadAsync(chunk,ct))>0) { if(bounded.Length+read>512*1024) throw new HiringProviderException("ContactProviderInvalidResponse"); await bounded.WriteAsync(chunk.AsMemory(0,read),ct); }
        using var document=JsonDocument.Parse(bounded.ToArray());
        if(document.RootElement.ValueKind!=JsonValueKind.Object || !document.RootElement.TryGetProperty("data",out var data) || data.ValueKind!=JsonValueKind.Object || !data.TryGetProperty("emails",out var emails) || emails.ValueKind!=JsonValueKind.Array) throw new HiringProviderException("ContactProviderInvalidResponse");
        var result=new List<HiringProviderContact>();
        foreach(var email in emails.EnumerateArray().Take(10))
        {
            if(email.ValueKind!=JsonValueKind.Object) continue;
            var first=Text(email,"first_name"); var last=Text(email,"last_name");
            var status=email.TryGetProperty("verification",out var verification) && verification.ValueKind==JsonValueKind.Object ? Text(verification,"status") : "unknown";
            var confidence=email.TryGetProperty("confidence",out var score) && score.ValueKind==JsonValueKind.Number && score.TryGetInt32(out var number) ? Math.Clamp(number,0,100) : 0;
            result.Add(new((first+" "+last).Trim(),first,Text(email,"value"),Text(email,"position"),Text(email,"department"),status,confidence,
                NullText(email,"linkedin"),Text(email,"value"),Location(email)));
        }
        return result;
    }
    private static string Text(JsonElement element,string property)=>element.TryGetProperty(property,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string? NullText(JsonElement element,string property)=>Text(element,property) is { Length:>0 } text ? text : null;
    private static string Location(JsonElement email)
    {
        if(!email.TryGetProperty("location",out var location)) return "";
        return location.ValueKind==JsonValueKind.String ? location.GetString() ?? "" : location.ValueKind==JsonValueKind.Object ?
            string.Join(", ",new[] { Text(location,"city"),Text(location,"country") }.Where(x=>x.Length>0)) : "";
    }
}
public sealed class GoogleCandidateMailboxProvider(IHttpClientFactory clients,IOptions<GoogleHiringOptions> settings) : IHiringMailboxProvider
{
    public const string ClientName="HiringGoogle";
    public const string Scopes="openid email https://www.googleapis.com/auth/gmail.send";
    public bool IsAvailable=>settings.Value.IsAvailable;
    public HiringOAuthChallenge Authorize(string state)
    {
        if(!IsAvailable) throw new HiringProviderException("GmailConfigurationRequired");
        var verifier=Base64Url(RandomNumberGenerator.GetBytes(32));
        var query=new Dictionary<string,string> { ["client_id"]=settings.Value.ClientId,["redirect_uri"]=settings.Value.CallbackUri,["response_type"]="code",
            ["scope"]=Scopes,["state"]=state,["access_type"]="offline",["prompt"]="consent",["code_challenge_method"]="S256",
            ["code_challenge"]=Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) };
        return new("https://accounts.google.com/o/oauth2/v2/auth?"+string.Join("&",query.Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(x.Value))),verifier);
    }
    public async Task<HiringOAuthGrant> ExchangeAsync(string code,string verifier,CancellationToken ct)
    {
        using var response=await Token(new Dictionary<string,string> { ["code"]=code,["code_verifier"]=verifier,["redirect_uri"]=settings.Value.CallbackUri,["grant_type"]="authorization_code" },ct);
        using var data=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
        var root=data.RootElement;
        if(!root.TryGetProperty("id_token",out var idToken) || !root.TryGetProperty("refresh_token",out var refresh)) throw new HiringProviderException("GmailOfflineConsentRequired");
        GoogleJsonWebSignature.Payload identity;
        try { identity=await GoogleJsonWebSignature.ValidateAsync(idToken.GetString(),new GoogleJsonWebSignature.ValidationSettings { Audience=[settings.Value.ClientId] }).WaitAsync(ct); }
        catch(Exception ex) when(ex is InvalidJwtException or ArgumentException) { throw new HiringProviderException("GmailIdentityInvalid"); }
        if(!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Subject) || string.IsNullOrWhiteSpace(identity.Email)) throw new HiringProviderException("GmailIdentityInvalid");
        return new(identity.Subject,identity.Email,refresh.GetString() ?? "",root.TryGetProperty("scope",out var scope) ? scope.GetString() ?? "" : "");
    }
    private async Task<HttpResponseMessage> Token(Dictionary<string,string> values,CancellationToken ct)
    {
        if(!IsAvailable) throw new HiringProviderException("GmailConfigurationRequired");
        values["client_id"]=settings.Value.ClientId; values["client_secret"]=settings.Value.ClientSecret;
        using var content=new FormUrlEncodedContent(values);
        var response=await clients.CreateClient(ClientName).PostAsync("https://oauth2.googleapis.com/token",content,ct);
        if(!response.IsSuccessStatusCode)
        {
            var failure=response.StatusCode==HttpStatusCode.TooManyRequests ? "GmailRateLimited" : (int)response.StatusCode>=500 ? "GmailProviderUnavailable" : "GmailConfigurationRequired";
            if(response.StatusCode==HttpStatusCode.BadRequest)
            {
                try
                {
                    using var error=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
                    if(error.RootElement.ValueKind==JsonValueKind.Object && error.RootElement.TryGetProperty("error",out var reason) &&
                        reason.ValueKind==JsonValueKind.String && reason.GetString()=="invalid_grant") failure="ReconnectGmailRequired";
                }
                catch(JsonException) { /* Unknown rejection does not prove that the user's grant was revoked. */ }
            }
            response.Dispose(); throw new HiringProviderException(failure);
        }
        return response;
    }
    public async Task RevokeAsync(string refreshToken,CancellationToken ct)
    {
        using var body=new FormUrlEncodedContent(new Dictionary<string,string> { ["token"]=refreshToken });
        using var response=await clients.CreateClient(ClientName).PostAsync("https://oauth2.googleapis.com/revoke",body,ct);
        if(!response.IsSuccessStatusCode) throw new HiringProviderException("GmailRevocationUnavailable");
    }
    public async Task<HiringMailResult> SendAsync(string refreshToken,HiringMailMessage message,CancellationToken ct)
    {
        string access;
        try
        {
            using var response=await Token(new Dictionary<string,string> { ["refresh_token"]=refreshToken,["grant_type"]="refresh_token" },ct);
            using var token=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
            if(token.RootElement.ValueKind!=JsonValueKind.Object || !token.RootElement.TryGetProperty("access_token",out var value) || value.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new HiringProviderException("ReconnectGmailRequired");
            access=value.GetString()!;
        }
        catch(HiringProviderException ex) { return new("Failed",FailureCode:ex.Code); }
        catch(JsonException) { return new("Failed",FailureCode:"GmailInvalidTokenResponse"); }
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        request.Content=JsonContent.Create(new { raw=Base64Url(Mime(message)) });
        using var sent=await clients.CreateClient(ClientName).SendAsync(request,ct);
        if(!sent.IsSuccessStatusCode) return (int)sent.StatusCode>=500 ? new("Unknown",FailureCode:"GmailOutcomeUnknown") :
            new("Failed",FailureCode:sent.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "ReconnectGmailRequired" : sent.StatusCode==HttpStatusCode.TooManyRequests ? "GmailRateLimited" : "GmailRejected");
        using var result=await JsonDocument.ParseAsync(await sent.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
        return result.RootElement.TryGetProperty("id",out var id) ? new("Sent",id.GetString(),result.RootElement.TryGetProperty("threadId",out var thread) ? thread.GetString() : null) : new("Unknown",FailureCode:"GmailOutcomeUnknown");
    }
    public static byte[] Mime(HiringMailMessage message)
    {
        if(message.From.Any(char.IsControl) || message.To.Any(char.IsControl) || message.Subject.Any(char.IsControl)) throw new ArgumentException("Invalid mail header.");
        var boundary="careerharbor-"+Guid.NewGuid().ToString("N");
        var fileName=new string(Path.GetFileName(message.FileName.Replace('\\','/')).Where(x=>char.IsAsciiLetterOrDigit(x) || x is '.' or '-' or '_').Take(100).ToArray());
        if(fileName.Length==0) fileName="resume.pdf";
        var mimeType=message.ContentType is "application/pdf" or "application/msword" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document" ? message.ContentType : "application/octet-stream";
        var subject=string.Join("\r\n ",message.Subject.EnumerateRunes().Chunk(10).Select(chunk=>"=?UTF-8?B?"+Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(chunk.Select(r=>r.ToString()))))+"?="));
        var text=$"From: {message.From}\r\nTo: {message.To}\r\nDate: {DateTimeOffset.UtcNow.ToString("r",CultureInfo.InvariantCulture)}\r\nSubject: {subject}\r\n"+
            $"Message-ID: <{message.MessageKey}@careerharbor.invalid>\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n"+
            $"--{boundary}\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n{Convert.ToBase64String(Encoding.UTF8.GetBytes(message.Body),Base64FormattingOptions.InsertLineBreaks)}\r\n"+
            $"--{boundary}\r\nContent-Type: {mimeType}\r\nContent-Disposition: attachment; filename=\"{fileName}\"\r\nContent-Transfer-Encoding: base64\r\n\r\n{Convert.ToBase64String(message.Attachment,Base64FormattingOptions.InsertLineBreaks)}\r\n--{boundary}--\r\n";
        return Encoding.UTF8.GetBytes(text);
    }
    private static string Base64Url(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
}
