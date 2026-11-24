using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using JobPortal.Application.Features.HiringOutreach;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed partial class CoresignalHiringProvider(IHttpClientFactory clients,IOptions<CoresignalHiringOptions> options,
    ILogger<CoresignalHiringProvider> logger) : IHiringPersonProvider
{
    public const string ClientName="HiringCoresignal";
    private static readonly string[] Recruiters=["Recruiter","Technical Recruiter","Talent Acquisition","Talent Partner","Recruitment"];
    private static readonly string[] People=["HR Business Partner","Human Resources","People Partner","People Operations"];
    private readonly Dictionary<string,HiringPersonIdentity> observed=new(StringComparer.Ordinal);
    private readonly HashSet<string> enriched=new(StringComparer.Ordinal);
    private DateTime nextRequestAtUtc;
    public string Name=>"Coresignal";
    public bool IsAvailable=>options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ApiKey);
    public bool AllowCandidateDisplay=>options.Value.AllowCandidateDisplay;
    public int MaximumSearchRequests=>Math.Clamp(options.Value.MaximumSearchRequests,1,5);
    public int MaximumEnrichments=>Math.Clamp(options.Value.MaximumEnrichments,1,3);
    [LoggerMessage(Level=LogLevel.Information,Message="Coresignal hiring stage {Stage} raw {Raw} relevant {Relevant}")]
    private static partial void StageCompleted(ILogger logger,string stage,int raw,int relevant);

    public async IAsyncEnumerable<IReadOnlyList<HiringPersonIdentity>> SearchAsync(HiringDiscoveryContext context,[EnumeratorCancellation] CancellationToken ct)
    {
        if(!IsAvailable) yield break;
        var domain=Domain(context.Domain);
        if(domain.Length==0) throw new HiringProviderException("CoresignalInvalidRequest");
        observed.Clear();
        enriched.Clear();
        var city=HiringLocationPolicy.City(context.Location);
        var family=HiringContactPolicy.Family(context.JobTitle,context.Department,context.Description);
        var managers=ManagerTitles(family);
        var stages=new List<(string Name,string Location,string[] Titles)>();
        if(city.Length>0) stages.Add(("LocalRecruiters",city,Recruiters));
        stages.Add(("IndiaRecruiters","India",Recruiters));
        stages.Add(("People","India",People));
        if(managers.Length>0) stages.Add(("Managers","India",managers));
        stages.Add(("RelevantGlobalFallback","",Recruiters.Concat(managers).Concat(LeadTitles(family)).ToArray()));
        foreach(var stage in stages.Take(MaximumSearchRequests))
        {
            var filters=new List<object> { Match("active_experience_company_website.domain_only",domain),new { term=new Dictionary<string,int> { ["is_working"]=1 } } };
            if(stage.Location.Length>0) filters.Add(Match("location_full",stage.Location));
            if(stage.Location.Length>0 && stage.Location!="India") filters.Add(Match("location_country","India"));
            var titleQuery=new { @bool=new { should=stage.Titles.Select(x=>Match("active_experience_title",x)).ToArray(),minimum_should_match=1 } };
            var payload=new { query=new { @bool=new { filter=filters,must=new[] {titleQuery} } } };
            // Official preview returns at most 20 objects. Deliberately request only page 1;
            // neither x-total-results nor a snapshot token triggers additional paid pages.
            using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.coresignal.com/cdapi/v2/employee_multi_source/search/es_dsl/preview?page=1") { Content=JsonContent.Create(payload) };
            using var body=await SendAsync(request,true,ct);
            if(body.RootElement.ValueKind!=JsonValueKind.Array) throw new HiringProviderException("CoresignalInvalidResponse");
            var rawCount=body.RootElement.GetArrayLength();
            var found=new List<HiringPersonIdentity>();
            foreach(var item in body.RootElement.EnumerateArray().Take(Math.Min(rawCount,Math.Clamp(options.Value.MaximumCandidatesPerSearch,1,20))))
            {
                if(item.ValueKind!=JsonValueKind.Object || !item.TryGetProperty("id",out var id) || id.ValueKind!=JsonValueKind.Number || !id.TryGetInt64(out var number) || number<=0) continue;
                // Preview company_website and active_experience_title describe current employment.
                // Query filters alone are never accepted as evidence of employer or location.
                var actualDomain=Domain(Text(item,"company_website"));
                var name=Text(item,"full_name"); var title=Text(item,"active_experience_title");
                if(actualDomain!=domain || name.Length==0 || name.Contains('*') || title.Length==0) continue;
                var person=new HiringPersonIdentity(Name,number.ToString(CultureInfo.InvariantCulture),"",name,title,
                    Text(item,"active_experience_department"),actualDomain,Text(item,"location_full"),stage.Name);
                if(!observed.TryAdd(person.Reference,person)) continue;
                found.Add(person);
            }
            if(logger.IsEnabled(LogLevel.Information)) StageCompleted(logger,stage.Name,rawCount,found.Count);
            yield return found;
        }
    }

    public async Task<HiringProviderContact?> EnrichAsync(HiringPersonIdentity person,HiringDiscoveryContext context,CancellationToken ct)
    {
        if(!IsAvailable) throw new HiringProviderException("CoresignalConfigurationRequired");
        if(person.Provider!=Name || person.Domain!=Domain(context.Domain) || !observed.TryGetValue(person.Reference,out var original) || original!=person)
            throw new HiringProviderException("CoresignalInvalidRequest");
        if(enriched.Count>=MaximumEnrichments || !enriched.Add(person.Reference)) return null;
        // Preview.id is a Multi-source Employee ID. Collect accepts that ID; profile
        // /enrich instead requires social_url. Do not reinterpret it as a member_id.
        // Only documented core identity/employment fields are requested. Collect's
        // published model has NO email/status fields: email finding remains Hunter's job.
        const string fields="?bundle=core_employee_data&fields=id&fields=full_name&fields=is_deleted&fields=is_working&fields=active_experience_company_website&fields=active_experience_title";
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.coresignal.com/cdapi/v2/employee_multi_source/collect/"+Uri.EscapeDataString(person.Reference)+fields);
        using var body=await SendAsync(request,false,ct);
        var root=body.RootElement;
        // Field selection may legitimately return an empty object for a record without
        // stored fields. It supplies no authority to look up an unvalidated identity.
        if(!root.EnumerateObject().Any()) throw new HiringProviderException("CoresignalEmployeeUnavailable");
        if(!root.TryGetProperty("id",out var id) || id.ValueKind!=JsonValueKind.Number || !id.TryGetInt64(out var employeeId) ||
            !root.TryGetProperty("is_deleted",out var deleted) || deleted.ValueKind!=JsonValueKind.Number || !deleted.TryGetInt32(out var isDeleted) ||
            !root.TryGetProperty("is_working",out var working) || working.ValueKind!=JsonValueKind.Number || !working.TryGetInt32(out var isWorking))
            throw new HiringProviderException("CoresignalInvalidResponse");
        if(isDeleted!=0) throw new HiringProviderException("CoresignalPrivacyRestricted");
        if(employeeId.ToString(CultureInfo.InvariantCulture)!=person.Reference || isWorking!=1 ||
            Domain(Text(root,"active_experience_company_website"))!=person.Domain ||
            HiringContactPolicy.Normalize(Text(root,"full_name"))!=HiringContactPolicy.Normalize(person.FullName) ||
            HiringContactPolicy.Normalize(Text(root,"active_experience_title"))!=HiringContactPolicy.Normalize(person.Title))
            throw new HiringProviderException("CoresignalIdentityMismatch");
        // No documented professional email field exists in this response family.
        // Returning no contact permits the existing bounded same-person Hunter finder,
        // now only after fresh identity/current-employer validation has succeeded.
        return null;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request,bool allowArray,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds,1,20)));
        var wait=nextRequestAtUtc-DateTime.UtcNow;
        if(wait>TimeSpan.Zero) await Task.Delay(wait,deadline.Token);
        nextRequestAtUtc=DateTime.UtcNow.AddMilliseconds(250); // Sequential workflow stays below the trial's 5 req/s.
        request.Headers.Add("apikey",options.Value.ApiKey);
        try
        {
            using var response=await clients.CreateClient(ClientName).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(!response.IsSuccessStatusCode) throw new HiringProviderException(response.StatusCode switch {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>"CoresignalConfigurationRequired",
                HttpStatusCode.PaymentRequired=>"CoresignalQuotaExceeded",HttpStatusCode.TooManyRequests=>"CoresignalRateLimited",
                HttpStatusCode.NotFound when request.Method==HttpMethod.Get=>"CoresignalEmployeeNotFound",
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity=>"CoresignalInvalidRequest",_=>"CoresignalUnavailable" });
            return await HiringProviderJson.ReadAsync(response,"CoresignalInvalidResponse",deadline.Token,allowArray);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new HiringProviderException("CoresignalTimeout"); }
    }
    private static object Match(string field,string value)=>new { match_phrase=new Dictionary<string,string> { [field]=value } };
    internal static string Domain(string value)
    {
        if(!Uri.TryCreate(value.Contains("://",StringComparison.Ordinal) ? value : "https://"+value,UriKind.Absolute,out var uri) ||
            uri.Scheme is not ("https" or "http") || uri.HostNameType!=UriHostNameType.Dns || uri.UserInfo.Length>0 || !uri.Host.Contains('.')) return "";
        var host=uri.IdnHost.ToLowerInvariant(); return host.StartsWith("www.",StringComparison.Ordinal) ? host[4..] : host;
    }
    private static string Text(JsonElement item,string field)=>item.ValueKind==JsonValueKind.Object && item.TryGetProperty(field,out var value) && value.ValueKind==JsonValueKind.String ?
        new string((value.GetString() ?? "").Where(x=>!char.IsControl(x)).Take(200).ToArray()) : "";
    private static string[] ManagerTitles(string family)=>family switch {
        "Engineering"=>["Engineering Manager","Software Engineering Manager","Technical Manager","Development Manager","Embedded Systems Manager"],
        "Operations"=>["Trust and Safety Manager","Trust & Safety Manager","Operations Manager","Procurement Manager"],
        "Data"=>["Data Manager","Analytics Manager","Data Science Manager"],"Product"=>["Product Manager","Group Product Manager"],
        "Finance"=>["Finance Manager"],"Design"=>["Design Manager","UX Manager"],
        "Marketing"=>["Marketing Manager"],"Sales"=>["Sales Manager"],_=>[] };
    private static string[] LeadTitles(string family)=>family switch {
        "Engineering"=>["Engineering Lead","Software Engineering Lead"],"Operations"=>["Operations Lead","Procurement Lead"],
        "Data"=>["Data Lead","Analytics Lead"],"Product"=>["Product Lead"],"Finance"=>["Finance Lead"],"Design"=>["Design Lead"],
        "Marketing"=>["Marketing Lead"],"Sales"=>["Sales Lead"],_=>[] };
}
