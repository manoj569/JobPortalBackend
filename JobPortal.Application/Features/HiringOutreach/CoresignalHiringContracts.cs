namespace JobPortal.Application.Features.HiringOutreach;

public sealed class CoresignalHiringOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public bool AllowCandidateDisplay { get; set; }
    public bool AllowSharedPersistence { get; set; }
    public int MaximumSearchRequests { get; set; } = 5;
    public int MaximumCandidatesPerSearch { get; set; } = 10;
    public int MaximumEnrichments { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 10;
    public bool IsValid()=>!Enabled || !string.IsNullOrWhiteSpace(ApiKey) && MaximumSearchRequests is >=1 and <=5 &&
        MaximumCandidatesPerSearch is >=1 and <=20 && MaximumEnrichments is >=1 and <=3 && TimeoutSeconds is >=1 and <=20;
    public static bool IsDerived(string provider)=>provider.Contains("Coresignal",StringComparison.OrdinalIgnoreCase);
    public bool CanDisplay(string provider)=>!IsDerived(provider) || Enabled && AllowCandidateDisplay;
    public bool CanShare(string provider)=>!IsDerived(provider) || Enabled && AllowCandidateDisplay && AllowSharedPersistence;
}

public sealed record HiringProviderProbeRequest(string CompanyName,string CompanyDomain,string JobTitle,string Location,bool ConfirmProviderCreditUse=false);
// Deliberately excludes names, IDs, profile URLs and email. Diagnostic data is not persisted.
public sealed record HiringProviderProbeContact(string Title,string Location,string CompanyDomain,string ContactType,int MatchScore,bool ContactAvailable);
public sealed record HiringProviderProbeResult(string Provider,string State,int IdentityCount,int RelevantCount,int ContactCount,
    IReadOnlyList<HiringProviderProbeContact> Matches,IReadOnlyList<string> Failures);
public interface IHiringProviderDiagnostics
{
    Task<HiringProviderProbeResult> ProbeCoresignalAsync(Guid administrator,HiringProviderProbeRequest request,CancellationToken ct);
}
