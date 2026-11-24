using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobPortal.Application.Features.HiringOutreach;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed class SnovHiringProfessionalEmailFinder(IHttpClientFactory clients,IOptions<SnovHiringOptions> options) : IHiringProfessionalEmailFinder
{
    public const string ClientName="HiringSnov";
    public string Name=>"Snov";
    public bool IsAvailable=>options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ClientId) && !string.IsNullOrWhiteSpace(options.Value.ClientSecret);
    public bool AllowCandidateDisplay=>options.Value.AllowCandidateDisplay;
    public int MaximumRequests=>Math.Clamp(options.Value.MaximumRequests,1,2);
    private readonly HashSet<string> attempted=new(StringComparer.Ordinal);
    private string token="";
    private DateTime tokenExpiresAtUtc;
    public async Task<HiringProviderContact?> FindAsync(HiringPersonIdentity person,CancellationToken ct)
    {
        if(!IsAvailable || HiringEmailProviderData.SplitName(person) is not { } name) return null;
        if(attempted.Count>=MaximumRequests || !attempted.Add(person.Domain+"|"+person.Reference+"|"+person.FullName)) return null;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds,1,20)));
        try
        {
            if(token.Length==0 || tokenExpiresAtUtc<=DateTime.UtcNow)
            {
                using var auth=new HttpRequestMessage(HttpMethod.Post,"https://api.snov.io/v1/oauth/access_token") {
                    Content=new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"]="client_credentials",["client_id"]=options.Value.ClientId,["client_secret"]=options.Value.ClientSecret }) };
                using var granted=await SendAsync(auth,deadline.Token); token=HiringEmailProviderData.Text(granted.RootElement,"access_token");
                if(token.Length==0 || !granted.RootElement.TryGetProperty("expires_in",out var expires) || expires.ValueKind!=JsonValueKind.Number || !expires.TryGetInt32(out var seconds) || seconds<=0)
                    throw new HiringProviderException("SnovConfigurationRequired");
                tokenExpiresAtUtc=DateTime.UtcNow.AddSeconds(Math.Min(seconds,300)-1);
            }
            using var start=new HttpRequestMessage(HttpMethod.Post,"https://api.snov.io/v2/emails-by-domain-by-name/start") {
                Content=JsonContent.Create(new { rows=new[] { new { first_name=name.First,last_name=name.Last,domain=person.Domain } } }) };
            using var started=await SendAsync(start,deadline.Token);
            if(!started.RootElement.TryGetProperty("data",out var data)) throw new HiringProviderException("SnovInvalidResponse");
            var hash=HiringEmailProviderData.Text(data,"task_hash"); if(hash.Length is 0 or >200) throw new HiringProviderException("SnovInvalidResponse");
            for(var poll=0;poll<3;poll++)
            {
                if(poll>0) await Task.Delay(TimeSpan.FromMilliseconds(500),deadline.Token);
                using var query=new HttpRequestMessage(HttpMethod.Get,"https://api.snov.io/v2/emails-by-domain-by-name/result?task_hash="+Uri.EscapeDataString(hash));
                using var result=await SendAsync(query,deadline.Token); var root=result.RootElement; var status=HiringEmailProviderData.Text(root,"status");
                if(status=="in_progress") continue;
                if(status!="completed" || !root.TryGetProperty("meta",out var meta) || HiringEmailProviderData.Text(meta,"task_hash")!=hash ||
                    !root.TryGetProperty("data",out var rows) || rows.ValueKind!=JsonValueKind.Array) throw new HiringProviderException("SnovInvalidResponse");
                foreach(var row in rows.EnumerateArray().Take(10))
                {
                    if(row.ValueKind!=JsonValueKind.Object) throw new HiringProviderException("SnovInvalidResponse");
                    if(HiringContactPolicy.Normalize(HiringEmailProviderData.Text(row,"people"))!=HiringContactPolicy.Normalize(person.FullName)) continue;
                    if(!row.TryGetProperty("result",out var emails) || emails.ValueKind!=JsonValueKind.Array) continue;
                    foreach(var email in emails.EnumerateArray().Take(10))
                    {
                        var address=HiringEmailProviderData.Text(email,"email");
                        if(HiringEmailProviderData.Text(email,"smtp_status")=="valid" && HiringEmailProviderData.Text(email,"unknown_status_reason").Length==0 &&
                            HiringEmailProviderData.Flag(email,"is_valid_format",true) && HiringEmailProviderData.Flag(email,"is_disposable",false) &&
                            HiringEmailProviderData.Flag(email,"is_webmail",false) && HiringEmailProviderData.Flag(email,"is_gibberish",false) && HiringContactPolicy.ProfessionalEmail(address,person.Domain))
                            return HiringEmailProviderData.Contact(person,address,Name);
                    }
                }
                return null;
            }
            throw new HiringProviderException("SnovOutcomeUnknown"); // Do not restart a paid task that may still complete.
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new HiringProviderException("SnovTimeout"); }
    }
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(!request.RequestUri!.AbsolutePath.EndsWith("/access_token",StringComparison.Ordinal)) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await clients.CreateClient(ClientName).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode) throw new HiringProviderException(HiringEmailProviderData.Failure(response.StatusCode,Name));
        var document=await HiringProviderJson.ReadAsync(response,"SnovInvalidResponse",ct);
        if(HiringEmailProviderData.Text(document.RootElement,"status")=="not_enough_credits") { document.Dispose(); throw new HiringProviderException("SnovQuotaExceeded"); }
        return document;
    }
}
