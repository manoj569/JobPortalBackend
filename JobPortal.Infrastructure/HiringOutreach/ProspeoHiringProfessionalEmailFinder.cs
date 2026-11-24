using System.Net.Http.Json;
using System.Text.Json;
using JobPortal.Application.Features.HiringOutreach;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed class ProspeoHiringProfessionalEmailFinder(IHttpClientFactory clients,IOptions<ProspeoHiringOptions> options) : IHiringProfessionalEmailFinder
{
    public const string ClientName="HiringProspeo";
    public string Name=>"Prospeo";
    public bool IsAvailable=>options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ApiKey);
    public bool AllowCandidateDisplay=>options.Value.AllowCandidateDisplay;
    public int MaximumRequests=>Math.Clamp(options.Value.MaximumRequests,1,2);
    private readonly HashSet<string> attempted=new(StringComparer.Ordinal);
    public async Task<HiringProviderContact?> FindAsync(HiringPersonIdentity person,CancellationToken ct)
    {
        if(!IsAvailable || HiringEmailProviderData.SplitName(person) is not { } name) return null;
        if(attempted.Count>=MaximumRequests || !attempted.Add(person.Domain+"|"+person.Reference+"|"+person.FullName)) return null;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds,1,20)));
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.prospeo.io/enrich-person") {
            Content=JsonContent.Create(new { only_verified_email=true,enrich_mobile=false,data=new { first_name=name.First,last_name=name.Last,company_website=person.Domain } }) };
        request.Headers.Add("X-KEY",options.Value.ApiKey);
        try
        {
            using var response=await clients.CreateClient(ClientName).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            // Prospeo documents NO_MATCH and other machine errors in HTTP 400 bodies.
            if(!response.IsSuccessStatusCode && response.StatusCode!=System.Net.HttpStatusCode.BadRequest)
                throw new HiringProviderException(HiringEmailProviderData.Failure(response.StatusCode,Name));
            using var body=await HiringProviderJson.ReadAsync(response,"ProspeoInvalidResponse",deadline.Token); var root=body.RootElement;
            if(HiringEmailProviderData.Flag(root,"error",true))
            {
                var code=HiringEmailProviderData.Text(root,"error_code");
                if(code=="NO_MATCH") return null;
                throw new HiringProviderException(code switch { "INVALID_API_KEY"=>"ProspeoConfigurationRequired","INSUFFICIENT_CREDITS"=>"ProspeoQuotaExceeded",
                    "INVALID_DATAPOINTS" or "INVALID_REQUEST"=>"ProspeoInvalidRequest",_=>"ProspeoUnavailable" });
            }
            if(!response.IsSuccessStatusCode || !HiringEmailProviderData.Flag(root,"error",false) || !root.TryGetProperty("person",out var p) || p.ValueKind!=JsonValueKind.Object ||
                !root.TryGetProperty("company",out var company) || company.ValueKind!=JsonValueKind.Object) throw new HiringProviderException("ProspeoInvalidResponse");
            if(!p.TryGetProperty("email",out var email) || email.ValueKind==JsonValueKind.Null) return null;
            var full=HiringEmailProviderData.Text(p,"full_name");
            if(full.Length==0) full=(HiringEmailProviderData.Text(p,"first_name")+" "+HiringEmailProviderData.Text(p,"last_name")).Trim();
            var domain=HiringEmailProviderData.Text(company,"domain"); if(domain.Length==0) domain=HiringEmailProviderData.Text(company,"website");
            var address=HiringEmailProviderData.Text(email,"email");
            if(HiringContactPolicy.Normalize(full)!=HiringContactPolicy.Normalize(person.FullName) || CoresignalHiringProvider.Domain(domain)!=person.Domain ||
                HiringEmailProviderData.Text(email,"status")!="VERIFIED" || !HiringEmailProviderData.Flag(email,"revealed",true) || !HiringContactPolicy.ProfessionalEmail(address,person.Domain)) return null;
            return HiringEmailProviderData.Contact(person,address,Name);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new HiringProviderException("ProspeoTimeout"); }
    }
}
