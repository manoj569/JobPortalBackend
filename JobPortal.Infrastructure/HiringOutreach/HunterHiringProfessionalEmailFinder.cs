using System.Text.Json;
using JobPortal.Application.Features.HiringOutreach;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed class HunterHiringProfessionalEmailFinder(IHttpClientFactory clients,IOptions<HunterHiringOptions> options) : IHiringProfessionalEmailFinder
{
    public bool IsAvailable=>!string.IsNullOrWhiteSpace(options.Value.ApiKey);
    public async Task<HiringProviderContact?> FindAsync(HiringPersonIdentity person,CancellationToken ct)
    {
        if(!IsAvailable) throw new HiringProviderException("HunterConfigurationRequired");
        if(person.Provider!="Coresignal" || person.FullName.Length==0 || person.FullName.Contains('*') || person.Domain.Length==0) return null;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.hunter.io/v2/email-finder/found?domain="+Uri.EscapeDataString(person.Domain)+
            "&full_name="+Uri.EscapeDataString(person.FullName)+"&max_duration=10");
        request.Headers.Add("X-API-KEY",options.Value.ApiKey);
        using var response=await clients.CreateClient(HunterEmployerContactProvider.ClientName).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(!response.IsSuccessStatusCode) throw new HiringProviderException(await HiringProviderJson.FailureAsync(response,"Hunter",deadline.Token));
        using var document=await HiringProviderJson.ReadAsync(response,"HunterInvalidResponse",deadline.Token);
        if(!document.RootElement.TryGetProperty("data",out var data)) throw new HiringProviderException("HunterInvalidResponse");
        if(data.ValueKind==JsonValueKind.Null) return null;
        if(data.ValueKind!=JsonValueKind.Object) throw new HiringProviderException("HunterInvalidResponse");
        var email=Text(data,"email"); var name=(Text(data,"first_name")+" "+Text(data,"last_name")).Trim();
        if(HiringContactPolicy.Normalize(name)!=HiringContactPolicy.Normalize(person.FullName) || !HiringContactPolicy.ProfessionalEmail(email,person.Domain) ||
            Text(data,"provenance")!="found" || !data.TryGetProperty("verification",out var verification) || Text(verification,"status")!="valid") return null;
        return new(person.FullName,Text(data,"first_name"),email,person.Title,person.Department,"valid",95,null,person.Reference,person.Location,"EmailFinder","Coresignal+Hunter");
    }
    private static string Text(JsonElement data,string field)=>data.ValueKind==JsonValueKind.Object && data.TryGetProperty(field,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString() ?? "" : "";
}
