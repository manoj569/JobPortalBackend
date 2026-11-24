using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using JobPortal.Application.Features.HiringOutreach;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed partial class ApolloHiringProvider(IHttpClientFactory clients,IOptions<ApolloHiringOptions> options,ILogger<ApolloHiringProvider>? logger=null) : IHiringPersonProvider
{
    public string Name=>"Apollo";
    public bool AllowCandidateDisplay=>true;
    public int MaximumSearchRequests=>options.Value.MaximumSearchRequests;
    public int MaximumEnrichments=>options.Value.MaximumEnrichments;
    public const string ClientName="HiringApollo";
    private static readonly string[] Recruiting=["Recruiter","Technical Recruiter","Talent Acquisition","Talent Partner","Recruitment"];
    private static readonly string[] People=["HR Business Partner","Human Resources","People Partner","People Operations"];
    public bool IsAvailable=>options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ApiKey);
    [LoggerMessage(Level=LogLevel.Information,Message="Apollo hiring search stage {Stage} raw {Count}")]
    private static partial void StageCompleted(ILogger logger,string stage,int count);
    public async IAsyncEnumerable<IReadOnlyList<HiringPersonIdentity>> SearchAsync(HiringDiscoveryContext context,[EnumeratorCancellation] CancellationToken ct)
    {
        if(!IsAvailable) yield break;
        var city=HiringLocationPolicy.City(context.Location);
        var managers=ManagerTitles(HiringContactPolicy.Family(context.JobTitle,context.Department,context.Description));
        var stages=new List<(string Stage,string Location,string[] Titles)>();
        if(city.Length>0) stages.Add(("LocalRecruiting",city+", India",Recruiting));
        stages.Add(("IndiaRecruiting","India",Recruiting)); stages.Add(("People","India",People));
        if(managers.Length>0) stages.Add(("Managers","India",managers));
        stages.Add(("GlobalFallback","",Recruiting.Concat(managers).ToArray()));
        foreach(var stage in stages.Take(Math.Clamp(options.Value.MaximumSearchRequests,1,5)))
        {
            var query=new List<KeyValuePair<string,string>> { new("q_organization_domains_list[]",context.Domain),new("per_page","20"),new("page","1") };
            foreach(var title in stage.Titles) query.Add(new("person_titles[]",title));
            if(stage.Location.Length>0) query.Add(new("person_locations[]",stage.Location));
            using var data=await Post("mixed_people/api_search",query,ct);
            if(!data.RootElement.TryGetProperty("people",out var people) || people.ValueKind!=JsonValueKind.Array) throw new HiringProviderException("ApolloInvalidResponse");
            var result=new List<HiringPersonIdentity>();
            foreach(var person in people.EnumerateArray().Take(20))
            {
                var id=Text(person,"id"); if(id.Length is <1 or >200) continue;
                // Do not turn last_name_obfuscated or a search-location filter into person data.
                result.Add(new("Apollo",id,Text(person,"first_name"),FullName(person),Text(person,"title"),"",context.Domain,Location(person),stage.Stage));
            }
            if(logger is not null) StageCompleted(logger,stage.Stage,result.Count);
            yield return result.DistinctBy(x=>x.Reference,StringComparer.Ordinal).ToArray();
        }
    }
    public async Task<HiringProviderContact?> EnrichAsync(HiringPersonIdentity person,HiringDiscoveryContext context,CancellationToken ct)
    {
        if(!IsAvailable) throw new HiringProviderException("ApolloConfigurationRequired");
        if(person.Provider!="Apollo" || person.Domain!=context.Domain || person.Reference.Length is <1 or >200) throw new HiringProviderException("ApolloInvalidRequest");
        var query=new[] { new KeyValuePair<string,string>("id",person.Reference),new("reveal_personal_emails","false"),new("reveal_phone_number","false"),new("run_waterfall_email","false"),new("run_waterfall_phone","false") };
        using var document=await Post("people/match",query,ct);
        if(!document.RootElement.TryGetProperty("person",out var data)) throw new HiringProviderException("ApolloInvalidResponse");
        if(data.ValueKind==JsonValueKind.Null) return null;
        if(data.ValueKind!=JsonValueKind.Object) throw new HiringProviderException("ApolloInvalidResponse");
        if(Text(data,"match_confidence") is "low" or "none") return null;
        if(Text(data,"id")!=person.Reference || !data.TryGetProperty("organization",out var org) || !Text(org,"primary_domain").Trim().Equals(context.Domain,StringComparison.OrdinalIgnoreCase)) return null;
        var email=Text(data,"email"); var name=FullName(data);
        if(name.Length==0 || !HiringContactPolicy.ProfessionalEmail(email,context.Domain)) return null;
        var verified=Text(data,"email_status")=="verified";
        if(data.TryGetProperty("contact",out var contact) && contact.ValueKind==JsonValueKind.Object)
        {
            verified|=Text(contact,"email").Equals(email,StringComparison.OrdinalIgnoreCase) && Text(contact,"email_status")=="verified";
            if(contact.TryGetProperty("contact_emails",out var emails) && emails.ValueKind==JsonValueKind.Array)
                verified|=emails.EnumerateArray().Any(x=>Text(x,"email").Equals(email,StringComparison.OrdinalIgnoreCase) && Text(x,"email_status")=="verified");
        }
        return new(name,Text(data,"first_name"),email,Text(data,"title"),"",verified ? "valid" : "unknown",verified ? 95 : 0,
            NullText(data,"linkedin_url"),person.Reference,Location(data),"PersonEnrichment","Apollo");
    }
    private async Task<JsonDocument> Post(string endpoint,IEnumerable<KeyValuePair<string,string>> query,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds,5,30)));
        var url="https://api.apollo.io/api/v1/"+endpoint+"?"+string.Join("&",query.Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(x.Value)));
        using var request=new HttpRequestMessage(HttpMethod.Post,url) { Content=JsonContent.Create(new { }) }; request.Headers.Add("x-api-key",options.Value.ApiKey);
        using var response=await clients.CreateClient(ClientName).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(!response.IsSuccessStatusCode) throw new HiringProviderException(await HiringProviderJson.FailureAsync(response,"Apollo",deadline.Token));
        return await HiringProviderJson.ReadAsync(response,"ApolloInvalidResponse",deadline.Token);
    }
    private static string[] ManagerTitles(string family)=>family switch {
        "Engineering"=>["Engineering Manager","Software Engineering Manager","Technical Manager","Development Manager","Engineering Lead"],
        "Operations"=>["Trust and Safety Manager","Operations Manager","Operations Lead"],"Data"=>["Data Manager","Analytics Manager","Data Science Manager"],
        "Product"=>["Product Manager","Product Lead"],"Finance"=>["Finance Manager","Finance Lead"],"Design"=>["Design Manager","Design Lead"],
        "Marketing"=>["Marketing Manager","Marketing Lead"],"Sales"=>["Sales Manager","Sales Lead"],_=>[] };
    private static string FullName(JsonElement p)
    {
        var full=Text(p,"name"); if(full.Length>0 && !full.Contains('*')) return full;
        var last=Text(p,"last_name"); return last.Length>0 && !last.Contains('*') ? (Text(p,"first_name")+" "+last).Trim() : "";
    }
    private static string Location(JsonElement p)=>string.Join(", ",new[] { Text(p,"city"),Text(p,"state"),Text(p,"country") }.Where(x=>x.Length>0));
    private static string Text(JsonElement p,string name)=>p.ValueKind==JsonValueKind.Object && p.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string? NullText(JsonElement p,string name)=>Text(p,name) is { Length:>0 } value ? value : null;
}

internal static class HiringProviderJson
{
    public static async Task<JsonDocument> ReadAsync(HttpResponseMessage response,string failure,CancellationToken ct,bool allowArray=false)
    {
        if(response.Content.Headers.ContentLength>512*1024) throw new HiringProviderException(failure);
        await using var stream=await response.Content.ReadAsStreamAsync(ct);
        using var bounded=new MemoryStream(); var buffer=new byte[8192]; int read;
        while((read=await stream.ReadAsync(buffer,ct))>0)
        { if(bounded.Length+read>512*1024) throw new HiringProviderException(failure); await bounded.WriteAsync(buffer.AsMemory(0,read),ct); }
        try
        {
            var document=JsonDocument.Parse(bounded.ToArray());
            if(document.RootElement.ValueKind==JsonValueKind.Object || allowArray && document.RootElement.ValueKind==JsonValueKind.Array) return document;
            document.Dispose(); throw new HiringProviderException(failure);
        }
        catch(JsonException) { throw new HiringProviderException(failure); }
    }
    public static async Task<string> FailureAsync(HttpResponseMessage response,string provider,CancellationToken ct)
    {
        // Only machine codes influence classification. Raw error text and request values are never returned/logged.
        if(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
        {
            try
            {
                using var body=await ReadAsync(response,"ProviderErrorMetadataUnavailable",ct);
                var root=body.RootElement; var code="";
                if(root.TryGetProperty("error_details",out var details) && details.ValueKind==JsonValueKind.Object && details.TryGetProperty("code",out var value) && value.ValueKind==JsonValueKind.String) code=value.GetString() ?? "";
                if(root.TryGetProperty("errors",out var errors) && errors.ValueKind==JsonValueKind.Array)
                    foreach(var item in errors.EnumerateArray().Take(5)) if(item.TryGetProperty("id",out var id) && id.ValueKind==JsonValueKind.String) code+=" "+id.GetString();
                if(code.Contains("CREDIT",StringComparison.OrdinalIgnoreCase) || code.Contains("QUOTA",StringComparison.OrdinalIgnoreCase)) return provider+"QuotaExceeded";
                if(code.Contains("RATE",StringComparison.OrdinalIgnoreCase) || code.Contains("TOO_MANY",StringComparison.OrdinalIgnoreCase)) return provider+"RateLimited";
            }
            catch(Exception ex) when(ex is HiringProviderException or JsonException or HttpRequestException or OperationCanceledException or InvalidOperationException) { }
        }
        return provider+(response.StatusCode switch { HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>"ConfigurationRequired",
            HttpStatusCode.TooManyRequests=>"RateLimited",HttpStatusCode.PaymentRequired=>"QuotaExceeded",HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity=>"InvalidRequest",_=>"ProviderUnavailable" });
    }
}
