using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Domain.Enums;
using Microsoft.Playwright;

namespace JobPortal.Infrastructure.AIApply;

public interface IJobSiteAdapter
{
    int Priority { get; }
    SiteAdapterSupportResult Support { get; }
    JobSiteCapabilities Capabilities { get; }
    bool CanHandle(Uri uri);
    Task<BrowserApplicationResult> ExecuteAsync(IPage page, BrowserApplicationContext context,
        BrowserValueSet values, ResumePayload? resume, CancellationToken ct);
}

public interface IJobSiteAdapterResolver { IJobSiteAdapter Resolve(Uri uri); }

public sealed class JobSiteAdapterResolver(IEnumerable<IJobSiteAdapter> adapters) : IJobSiteAdapterResolver
{
    public IJobSiteAdapter Resolve(Uri uri) => adapters.Where(x => x.CanHandle(uri)).OrderByDescending(x => x.Priority).ThenBy(x => x.Support.AdapterName, StringComparer.Ordinal).First();
}

public static class JobSiteDomainCatalog
{
    private static readonly (JobSiteIdentifier Site, string[] Domains)[] Known =
    [
        (JobSiteIdentifier.Greenhouse, ["greenhouse.io"]), (JobSiteIdentifier.Lever, ["lever.co"]),
        (JobSiteIdentifier.Ashby, ["ashbyhq.com"]), (JobSiteIdentifier.SmartRecruiters, ["smartrecruiters.com"]),
        (JobSiteIdentifier.Workday, ["myworkdayjobs.com"]), (JobSiteIdentifier.LinkedIn, ["linkedin.com"]),
        (JobSiteIdentifier.Naukri, ["naukri.com"]), (JobSiteIdentifier.Indeed, ["indeed.com"]),
        (JobSiteIdentifier.Foundit, ["foundit.in", "foundit.com"]), (JobSiteIdentifier.Wellfound, ["wellfound.com"])
    ];
    public static JobSiteIdentifier Identify(string host) => Known.FirstOrDefault(x => x.Domains.Any(domain => Matches(host, domain))).Site;
    public static bool Matches(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase);
}

public sealed record BrowserValueSet(IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, string> VerifiedAnswers);
public sealed record ResumePayload(string FileName, string ContentType, byte[] Content);
