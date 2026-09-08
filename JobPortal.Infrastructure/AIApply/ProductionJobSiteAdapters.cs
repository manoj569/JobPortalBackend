using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace JobPortal.Infrastructure.AIApply;

public abstract class DelegatingAtsAdapter(GenericJobSiteAdapter generic, IOptions<AIApplyOptions> options) : IJobSiteAdapter
{
    protected abstract AIApplySiteSetting Setting(AIApplySiteOptions sites);
    protected abstract IReadOnlyList<string> Domains { get; }
    public abstract SiteAdapterSupportResult Support { get; }
    public virtual JobSiteCapabilities Capabilities => new(true, false, true, true, true, true, true, false, false);
    public int Priority => 500;
    public bool CanHandle(Uri uri) => Setting(options.Value.Sites).Enabled && Domains.Any(x => HostMatches(uri.DnsSafeHost, x));
    public async Task<BrowserApplicationResult> ExecuteAsync(IPage page, BrowserApplicationContext context, BrowserValueSet values, ResumePayload? resume, CancellationToken ct)
    {
        var result = await generic.ExecuteAsync(page, context, values, resume, ct);
        return result with { Adapter = Support };
    }
    protected static bool HostMatches(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase);
}

public sealed class GreenhouseJobSiteAdapter(GenericJobSiteAdapter generic, IOptions<AIApplyOptions> options) : DelegatingAtsAdapter(generic, options)
{
    protected override AIApplySiteSetting Setting(AIApplySiteOptions sites) => sites.Greenhouse; protected override IReadOnlyList<string> Domains => ["greenhouse.io"];
    public override SiteAdapterSupportResult Support => new(JobSiteIdentifier.Greenhouse, JobSiteSupportLevel.PartiallySupported, "Common anonymous Greenhouse forms; production validation is still required.", false, false, nameof(GreenhouseJobSiteAdapter), "1");
}
public sealed class LeverJobSiteAdapter(GenericJobSiteAdapter generic, IOptions<AIApplyOptions> options) : DelegatingAtsAdapter(generic, options)
{
    protected override AIApplySiteSetting Setting(AIApplySiteOptions sites) => sites.Lever; protected override IReadOnlyList<string> Domains => ["lever.co"];
    public override SiteAdapterSupportResult Support => new(JobSiteIdentifier.Lever, JobSiteSupportLevel.PartiallySupported, "Common anonymous Lever forms; production validation is still required.", false, false, nameof(LeverJobSiteAdapter), "1");
}
public sealed class AshbyJobSiteAdapter(GenericJobSiteAdapter generic, IOptions<AIApplyOptions> options) : DelegatingAtsAdapter(generic, options)
{
    protected override AIApplySiteSetting Setting(AIApplySiteOptions sites) => sites.Ashby; protected override IReadOnlyList<string> Domains => ["ashbyhq.com"];
    public override SiteAdapterSupportResult Support => new(JobSiteIdentifier.Ashby, JobSiteSupportLevel.PartiallySupported, "Accessible Ashby forms; changed custom controls pause safely.", false, false, nameof(AshbyJobSiteAdapter), "1");
}
public sealed class SmartRecruitersJobSiteAdapter(GenericJobSiteAdapter generic, IOptions<AIApplyOptions> options) : DelegatingAtsAdapter(generic, options)
{
    protected override AIApplySiteSetting Setting(AIApplySiteOptions sites) => sites.SmartRecruiters; protected override IReadOnlyList<string> Domains => ["smartrecruiters.com"];
    public override SiteAdapterSupportResult Support => new(JobSiteIdentifier.SmartRecruiters, JobSiteSupportLevel.PartiallySupported, "Anonymous flows only; account creation is not automated.", false, false, nameof(SmartRecruitersJobSiteAdapter), "1");
}

public sealed class AccountOrientedJobSiteAdapter(GenericJobSiteAdapter generic, IOptions<AIApplyOptions> options) : IJobSiteAdapter
{
    public int Priority => 400;
    public SiteAdapterSupportResult Support => current;
    public JobSiteCapabilities Capabilities => new(false, true, false, false, false, false, true, true, true);
    private SiteAdapterSupportResult current = Descriptor(JobSiteIdentifier.Workday, JobSiteSupportLevel.PartiallySupported);
    public bool CanHandle(Uri uri)
    {
        var match = Candidates(options.Value.Sites).FirstOrDefault(x => x.Enabled && x.Domains.Any(d => Host(uri.DnsSafeHost, d)));
        if (match.Domains is null) return false; current = Descriptor(match.Site, match.Site == JobSiteIdentifier.Workday ? JobSiteSupportLevel.PartiallySupported : JobSiteSupportLevel.LoginRequired); return true;
    }
    public async Task<BrowserApplicationResult> ExecuteAsync(IPage page, BrowserApplicationContext context, BrowserValueSet values, ResumePayload? resume, CancellationToken ct)
    {
        if (current.Site == JobSiteIdentifier.Workday) return (await generic.ExecuteAsync(page, context, values, resume, ct)) with { Adapter = current };
        return new(false, AIApplyFailureKind.LoginRequired, "external_session_required", [new("Connect an authenticated external-site session.", "Login", Required: true)], Adapter: current);
    }
    private static SiteAdapterSupportResult Descriptor(JobSiteIdentifier site, JobSiteSupportLevel level) => new(site, level, level == JobSiteSupportLevel.LoginRequired ? "An authenticated candidate-established session is required." : "Site structure varies; safe generic handling only.", level == JobSiteSupportLevel.LoginRequired, false, nameof(AccountOrientedJobSiteAdapter), "1");
    private static bool Host(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<(JobSiteIdentifier Site, bool Enabled, string[] Domains)> Candidates(AIApplySiteOptions s) =>
    [ (JobSiteIdentifier.Workday, s.Workday.Enabled, ["myworkdayjobs.com"]), (JobSiteIdentifier.LinkedIn, s.LinkedIn.Enabled, ["linkedin.com"]), (JobSiteIdentifier.Naukri, s.Naukri.Enabled, ["naukri.com"]), (JobSiteIdentifier.Indeed, s.Indeed.Enabled, ["indeed.com"]), (JobSiteIdentifier.Foundit, s.Foundit.Enabled, ["foundit.in", "foundit.com"]), (JobSiteIdentifier.Wellfound, s.Wellfound.Enabled, ["wellfound.com"]) ];
}

public sealed class DisabledExternalJobSiteSessionStore : IExternalJobSiteSessionStore
{
    public Task<ExternalJobSiteSessionMetadata?> GetMetadataAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct) => Task.FromResult<ExternalJobSiteSessionMetadata?>(null);
    public Task<ExternalJobSiteSessionMetadata?> GetMetadataAsync(Guid userId, Guid sessionId, CancellationToken ct) => Task.FromResult<ExternalJobSiteSessionMetadata?>(null);
    public Task<UsableExternalJobSiteSession?> GetActiveAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct) => Task.FromResult<UsableExternalJobSiteSession?>(null);
    public Task<ExternalJobSiteSessionMetadata> SaveAsync(Guid userId, JobSiteIdentifier site, ReadOnlyMemory<byte> storageState, DateTime expiresAtUtc, long? expectedVersion, CancellationToken ct, bool allowRevokedReplacement = false) => throw new InvalidOperationException("Persistent external sessions are disabled.");
    public Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct) => Task.FromResult(false);
    public Task<bool> RequireReauthenticationAsync(Guid userId, Guid sessionId, long expectedVersion, CancellationToken ct) => Task.FromResult(false);
}
public sealed class ConfiguredJobSiteAdapterHealthService(IOptions<AIApplyOptions> options, IAIApplyOperationalStore store, IAIApplyWorkerIdentity worker) : IJobSiteAdapterHealthService
{
    public async Task<JobSiteAdapterHealth> GetHealthAsync(JobSiteIdentifier site, CancellationToken ct) { if (!Enabled(site) && site != JobSiteIdentifier.Generic) return JobSiteAdapterHealth.Disabled; var state = await store.GetSiteAsync(site, ct); return state.CircuitState == AIApplyCircuitState.Open ? JobSiteAdapterHealth.Failing : state.SampleCount > 0 ? JobSiteAdapterHealth.Healthy : JobSiteAdapterHealth.Unknown; }
    public Task<bool> CanExecuteAsync(JobSiteIdentifier site, DateTime now, CancellationToken ct) => store.TryAcquireSiteExecutionAsync(site, worker.Id, now, ct);
    public async Task RecordOutcomeAsync(JobSiteIdentifier site, bool technicalSuccess, DateTime now, CancellationToken ct) { await store.RecordSiteOutcomeAsync(site, worker.Id, technicalSuccess, now, ct); }
    public async Task OpenCircuitAsync(JobSiteIdentifier site, DateTime now, CancellationToken ct) { await store.SetManualCircuitAsync(site, true, now, ct); AIApplyTelemetry.CircuitOpens.Add(1, AIApplyTelemetry.Tags("manual_open", "open", site.ToString())); }
    public async Task CloseCircuitAsync(JobSiteIdentifier site, CancellationToken ct) { await store.SetManualCircuitAsync(site, false, DateTime.UtcNow, ct); AIApplyTelemetry.CircuitCloses.Add(1, AIApplyTelemetry.Tags("manual_close", "closed", site.ToString())); }
    private bool Enabled(JobSiteIdentifier site) { var s = options.Value.Sites; return site switch { JobSiteIdentifier.Greenhouse => s.Greenhouse.Enabled, JobSiteIdentifier.Lever => s.Lever.Enabled, JobSiteIdentifier.Ashby => s.Ashby.Enabled, JobSiteIdentifier.SmartRecruiters => s.SmartRecruiters.Enabled, JobSiteIdentifier.Workday => s.Workday.Enabled, JobSiteIdentifier.LinkedIn => s.LinkedIn.Enabled, JobSiteIdentifier.Naukri => s.Naukri.Enabled, JobSiteIdentifier.Indeed => s.Indeed.Enabled, JobSiteIdentifier.Foundit => s.Foundit.Enabled, JobSiteIdentifier.Wellfound => s.Wellfound.Enabled, _ => true }; }
}
