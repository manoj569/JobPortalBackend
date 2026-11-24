namespace JobPortal.Application.Features.HiringOutreach;

public abstract class HiringEmailProviderOptions
{
    public bool Enabled { get; set; }

    public bool AllowCandidateDisplay { get; set; }

    public bool AllowSharedPersistence { get; set; }

    public int MaximumRequests { get; set; } = 4;

    public int TimeoutSeconds { get; set; } = 10;

    protected bool ValidLimits =>
        MaximumRequests is >= 1 and <= 4 &&
        TimeoutSeconds is >= 1 and <= 20;

    public bool CanDisplay(string provenance, string name) =>
        !provenance
            .Split('+')
            .Contains(name, StringComparer.OrdinalIgnoreCase) ||
        Enabled && AllowCandidateDisplay;

    public bool CanShare(string provenance, string name) =>
        !provenance
            .Split('+')
            .Contains(name, StringComparer.OrdinalIgnoreCase) ||
        Enabled &&
        AllowCandidateDisplay &&
        AllowSharedPersistence;
}

public sealed class ProspeoHiringOptions : HiringEmailProviderOptions
{
    public string ApiKey { get; set; } = "";

    public bool IsValid() =>
        !Enabled ||
        ValidLimits &&
        !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class SnovHiringOptions : HiringEmailProviderOptions
{
    public SnovHiringOptions()
    {
        TimeoutSeconds = 15;
    }

    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    public bool IsValid() =>
        !Enabled ||
        ValidLimits &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}