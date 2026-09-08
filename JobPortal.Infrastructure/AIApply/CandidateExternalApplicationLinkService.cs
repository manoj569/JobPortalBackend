using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.AIApply;

public sealed class CandidateExternalApplicationLinkService(
    ExternalNavigationPolicy navigation,
    IJobSiteAdapterResolver adapters,
    IJobSiteAdapterHealthService health) : ICandidateExternalApplicationLinkService
{
    public async Task<CandidateExternalApplicationDestination?> ResolveAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || url != url.Trim() ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !await navigation.IsAllowedAsync(uri, ct))
            return null;

        var adapter = adapters.Resolve(uri);
        var identifiedSite = JobSiteDomainCatalog.Identify(uri.DnsSafeHost);
        var site = adapter.Support.Site == JobSiteIdentifier.Generic && identifiedSite != JobSiteIdentifier.Generic ? identifiedSite : adapter.Support.Site;
        if (identifiedSite != JobSiteIdentifier.Generic && adapter.Support.Site != JobSiteIdentifier.Generic && adapter.Support.Site != identifiedSite) return null;
        var displayName = site == JobSiteIdentifier.Generic ? "External application site" : site.ToString();
        var operational = await health.GetHealthAsync(site, ct) is not JobSiteAdapterHealth.Failing and not JobSiteAdapterHealth.Disabled;
        var restart = adapter.Support.Site != JobSiteIdentifier.Generic && adapter.Capabilities.SupportsRestartFromUrl || identifiedSite == JobSiteIdentifier.Generic && adapter.Capabilities.SupportsRestartFromUrl;
        return new(site, displayName, uri.AbsoluteUri, restart, operational);
    }
}
