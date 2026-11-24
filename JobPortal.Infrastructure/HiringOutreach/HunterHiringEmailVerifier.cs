using System.Net;
using System.Text.Json;
using JobPortal.Application.Features.HiringOutreach;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed class HunterHiringEmailVerifier(IHttpClientFactory clients,IOptions<HunterHiringOptions> options) : IHiringEmailVerificationProvider
{
    public bool IsAvailable=>!string.IsNullOrWhiteSpace(options.Value.ApiKey) && options.Value.MaximumVerifications>0;
    public int MaximumRequests=>options.Value.MaximumVerifications;
    public async Task<HiringProviderContact?> VerifyAsync(HiringProviderContact contact,CancellationToken ct)
    {
        if(!IsAvailable) throw new HiringProviderException("HunterConfigurationRequired");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        // Official GET endpoint. This named client removes URL/request loggers; never log this URI.
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.hunter.io/v2/email-verifier?email="+Uri.EscapeDataString(contact.Email));
        request.Headers.Add("X-API-KEY",options.Value.ApiKey);
        using var response=await clients.CreateClient(HunterEmployerContactProvider.ClientName).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(!response.IsSuccessStatusCode) throw new HiringProviderException(await HiringProviderJson.FailureAsync(response,"Hunter",deadline.Token));
        using var document=await HiringProviderJson.ReadAsync(response,"HunterInvalidResponse",deadline.Token);
        if(!document.RootElement.TryGetProperty("data",out var data) || data.ValueKind!=JsonValueKind.Object) throw new HiringProviderException("HunterInvalidResponse");
        return data.TryGetProperty("status",out var status) && status.ValueKind==JsonValueKind.String && status.GetString()=="valid" ?
            contact with { Verification="valid",Confidence=95,Source="Verification",Provider=contact.Provider=="Hunter" ? "Hunter" : contact.Provider+"+Hunter" } : null;
    }
}
