using JobPortal.Application.Features.HiringOutreach;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed class ApolloHiringOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public int MaximumSearchRequests { get; set; } = 5;
    public int MaximumEnrichments { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 15;
    public bool IsValid()=>!Enabled || !string.IsNullOrWhiteSpace(ApiKey) && MaximumSearchRequests is >=1 and <=5 &&
        MaximumEnrichments is >=1 and <=5 && TimeoutSeconds is >=5 and <=30;
}

public sealed partial class HiringProviderOrchestrator(IHiringPersonDiscoveryProvider people,IHiringContactEnrichmentProvider enrichment,
    IHiringEmailVerificationProvider verification,IHiringFallbackContactProvider hunter,IOptions<ApolloHiringOptions> options,
    ILogger<HiringProviderOrchestrator> logger) : IHiringContactProvider
{
    public string Name=>"Apollo/Hunter";
    public bool IsAvailable=>people.IsAvailable || hunter.IsAvailable;
    [LoggerMessage(Level=LogLevel.Information,Message="Hiring provider phase {Phase} candidates {Candidates} usable {Usable} failure {Failure}")]
    private static partial void Diagnostic(ILogger logger,string phase,int candidates,int usable,string failure);
    public async Task<IReadOnlyList<HiringProviderContact>> DiscoverAsync(HiringDiscoveryContext context,CancellationToken ct)=>
        (await DiscoverResultAsync(context,ct)).Contacts;
    public async Task<HiringDiscoveryResult> DiscoverResultAsync(HiringDiscoveryContext context,CancellationToken ct)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(150));
        var token=budget.Token; var contacts=new List<HiringProviderContact>(); var failures=new List<string>();
        var attempted=new HashSet<string>(StringComparer.Ordinal); var enrichments=0; var verifications=0; var uncertain=false;
        var enrichmentLimit=Math.Clamp(options.Value.MaximumEnrichments,1,5);
        var searches=0;
        if(people.IsAvailable)
        {
            try
            {
                await foreach(var batch in people.SearchAsync(context,token))
                {
                    searches++;
                    var ranked=batch.Take(20).Select(x=>(Person:x,Score:HiringContactPolicy.Score(ForScoring(x),context)))
                        .Where(x=>x.Score>0 && HiringContactPolicy.Type(x.Person.Title)!="TeamMember").OrderByDescending(x=>x.Score).ThenBy(x=>x.Person.Reference,StringComparer.Ordinal).ToArray();
                    Diagnostic(logger,"ApolloSearch",batch.Count,ranked.Length,"");
                    foreach(var candidate in ranked)
                    {
                        if(enrichments>=enrichmentLimit) break;
                        // Never merge obfuscated or similar names. A provider identity is exact;
                        // other future sources can supply a complete compound identity explicitly.
                        var key=IdentityKey(candidate.Person);
                        if(key is null) continue;
                        if(!attempted.Add(key)) continue;
                        enrichments++;
                        try
                        {
                            var contact=await enrichment.EnrichAsync(candidate.Person,context,token);
                            if(contact is null) continue;
                            if(contact.Verification!="valid" && verification.IsAvailable && verifications<Math.Clamp(verification.MaximumRequests,0,2))
                            { verifications++; contact=await verification.VerifyAsync(contact,token); }
                            if(contact is not null) contacts.AddRange(HiringContactPolicy.Select(new[] {contact},context,new()));
                            Diagnostic(logger,"ApolloEnrichment",enrichments,contacts.Count,"");
                        }
                        catch(HiringProviderException ex)
                        { failures.Add(ex.Code); uncertain|=!HiringProviderFailures.ConfirmedRejection(ex.Code); Diagnostic(logger,"ApolloEnrichment",enrichments,contacts.Count,ex.Code); }
                        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
                        { failures.Add("ApolloOutcomeUnknown"); uncertain=true; Diagnostic(logger,"ApolloEnrichment",enrichments,contacts.Count,"ApolloOutcomeUnknown"); }
                        if(Sufficient(contacts,context) || token.IsCancellationRequested) break;
                    }
                    if(Sufficient(contacts,context) || enrichments>=enrichmentLimit || searches>=Math.Clamp(options.Value.MaximumSearchRequests,1,5) || token.IsCancellationRequested) break;
                }
            }
            catch(HiringProviderException ex)
            { failures.Add(ex.Code); uncertain|=!HiringProviderFailures.ConfirmedRejection(ex.Code); Diagnostic(logger,"ApolloSearch",0,contacts.Count,ex.Code); }
            catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            { failures.Add("ApolloOutcomeUnknown"); uncertain=true; Diagnostic(logger,"ApolloSearch",searches,contacts.Count,"ApolloOutcomeUnknown"); }
        }
        if(!Sufficient(contacts,context) && hunter.IsAvailable && !token.IsCancellationRequested)
        {
            try
            {
                var needed=Math.Max(1,2-StrongCount(contacts,context));
                var fallback=await hunter.DiscoverResultAsync(context with { DesiredStrongContacts=needed },token); contacts.AddRange(fallback.Contacts);
                uncertain|=fallback.Uncertain; if(fallback.Failures is not null) failures.AddRange(fallback.Failures);
                foreach(var contact in fallback.Contacts.Where(x=>x.Verification!="valid" && HiringContactPolicy.ProfessionalEmail(x.Email,context.Domain) && HiringContactPolicy.Score(x,context)>0)
                    .OrderByDescending(x=>HiringContactPolicy.Score(x,context)).Take(Math.Max(0,Math.Clamp(verification.MaximumRequests,0,2)-verifications)))
                {
                    if(!verification.IsAvailable) break;
                    verifications++;
                    try { var verified=await verification.VerifyAsync(contact,token); if(verified is not null) contacts.Add(verified); }
                    catch(HiringProviderException ex) { failures.Add(ex.Code); uncertain|=!HiringProviderFailures.ConfirmedRejection(ex.Code); }
                    catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException) { failures.Add("HunterOutcomeUnknown"); uncertain=true; Diagnostic(logger,"HunterVerification",verifications,contacts.Count,"HunterOutcomeUnknown"); }
                }
                if(logger.IsEnabled(LogLevel.Information))
                { var count=HiringContactPolicy.Select(contacts,context,new()).Count; Diagnostic(logger,"HunterFallback",fallback.Contacts.Count,count,""); }
            }
            catch(HiringProviderException ex) { failures.Add(ex.Code); uncertain|=!HiringProviderFailures.ConfirmedRejection(ex.Code); Diagnostic(logger,"HunterFallback",0,contacts.Count,ex.Code); }
            catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            { failures.Add("HunterOutcomeUnknown"); uncertain=true; Diagnostic(logger,"HunterFallback",0,contacts.Count,"HunterOutcomeUnknown"); }
        }
        if(!IsAvailable) failures.Add("ContactProviderUnavailable");
        var selected=HiringContactPolicy.Select(contacts,context,new());
        // Ordinary employees cannot fill an already sufficient hiring-team result.
        if(selected.Count(x=>HiringContactPolicy.Strong(x,context))>=2)
            selected=selected.Where(x=>HiringContactPolicy.Type(x.Title)!="TeamMember").ToArray();
        Diagnostic(logger,"Final",contacts.Count,selected.Count,uncertain ? "Uncertain" : "");
        return new(selected,uncertain,failures);
    }
    private static bool Sufficient(List<HiringProviderContact> contacts,HiringDiscoveryContext context)=>
        StrongCount(contacts,context)==2;
    private static int StrongCount(List<HiringProviderContact> contacts,HiringDiscoveryContext context)=>
        contacts.Where(x=>x.Verification=="valid" && HiringContactPolicy.Strong(x,context)).DistinctBy(x=>x.Email.Trim(),StringComparer.OrdinalIgnoreCase).Take(2).Count();
    private static HiringProviderContact ForScoring(HiringPersonIdentity p)=>new(p.FullName,p.FirstName,"",p.Title,p.Department,"unknown",0,null,p.Reference,p.Location,"PeopleSearch",p.Provider);
    private static string? IdentityKey(HiringPersonIdentity p)
    {
        if(p.Reference.Length>0) return p.Provider+"|"+p.Reference;
        if(string.IsNullOrWhiteSpace(p.FullName) || p.FullName.Contains('*') || p.Title.Length==0 || p.Location.Length==0 || p.Domain.Length==0) return null;
        return p.Provider+"|"+p.Domain+"|"+HiringContactPolicy.Normalize(p.FullName)+"|"+HiringContactPolicy.Normalize(p.Title)+"|"+HiringLocationPolicy.Normalize(p.Location);
    }
}
