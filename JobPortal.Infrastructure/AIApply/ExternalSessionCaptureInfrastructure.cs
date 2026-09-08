using System.Collections.Concurrent;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.AIApply;

public sealed class DisabledExternalSessionCaptureTransport : IExternalSessionCaptureTransport
{
    public bool IsAvailable => false;
    public string InteractionMode => "Unavailable";
    public Task<IExternalSessionCaptureBrowser> StartAsync(ExternalSessionSiteDescriptor site, CancellationToken ct) =>
        throw new InvalidOperationException("A production interactive browser transport is not configured.");
}

public sealed class ConfiguredExternalSessionSiteRegistry(
    IOptions<AIApplyOptions> options,
    ExternalNavigationPolicy navigation,
    IJobSiteAdapterHealthService health) : IExternalSessionSiteRegistry
{
    public async Task<ExternalSessionSiteDescriptor?> GetAsync(JobSiteIdentifier site, CancellationToken ct)
    {
        var setting = Setting(site);
        if (setting is null || !setting.Enabled || !setting.SupportsPersistentSession ||
            !setting.PersistentSessionValidatedForProduction || string.IsNullOrWhiteSpace(setting.LoginEntryUrl) ||
            await health.GetHealthAsync(site, ct) is JobSiteAdapterHealth.Disabled or JobSiteAdapterHealth.Failing ||
            !Uri.TryCreate(setting.LoginEntryUrl, UriKind.Absolute, out var uri) ||
            JobSiteDomainCatalog.Identify(uri.DnsSafeHost) != site || !await navigation.IsAllowedAsync(uri, ct)) return null;
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { uri.DnsSafeHost };
        foreach (var host in setting.AllowedAuthenticationHosts)
        {
            if (string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host.Trim()) != UriHostNameType.Dns) return null;
            hosts.Add(host.Trim().TrimEnd('.'));
        }
        return new(site, Display(site), uri, hosts);
    }

    private AIApplySiteSetting? Setting(JobSiteIdentifier site) => site switch
    {
        JobSiteIdentifier.Workday => options.Value.Sites.Workday,
        JobSiteIdentifier.LinkedIn => options.Value.Sites.LinkedIn,
        JobSiteIdentifier.Naukri => options.Value.Sites.Naukri,
        JobSiteIdentifier.Indeed => options.Value.Sites.Indeed,
        JobSiteIdentifier.Foundit => options.Value.Sites.Foundit,
        JobSiteIdentifier.Wellfound => options.Value.Sites.Wellfound,
        _ => null
    };
    internal static string Display(JobSiteIdentifier site) => site switch
    {
        JobSiteIdentifier.LinkedIn => "LinkedIn", JobSiteIdentifier.Naukri => "Naukri",
        JobSiteIdentifier.Indeed => "Indeed", JobSiteIdentifier.Foundit => "Foundit",
        JobSiteIdentifier.Wellfound => "Wellfound", JobSiteIdentifier.Workday => "Workday", _ => "External site"
    };
}

public abstract class ConservativeExternalSessionValidator(
    JobSiteIdentifier site, ExternalNavigationPolicy navigation) : IExternalJobSiteSessionValidator
{
    public JobSiteIdentifier Site => site;
    public async Task<ExternalSessionValidationResult> ValidateAsync(IExternalSessionCaptureBrowser browser, CancellationToken ct)
    {
        var snapshot = await browser.InspectAsync(ct);
        if (JobSiteDomainCatalog.Identify(snapshot.CurrentUri.DnsSafeHost) != site || !await navigation.IsAllowedAsync(snapshot.CurrentUri, ct))
            return new(ExternalSessionValidationStatus.ValidationFailed, "validation_host_rejected");
        if (snapshot.HumanVerificationSignal) return new(ExternalSessionValidationStatus.RequiresHumanVerification, "human_verification_required");
        if (snapshot.LoginSignal) return new(ExternalSessionValidationStatus.RequiresLogin, "login_required");
        return snapshot.AuthenticatedSignal
            ? new(ExternalSessionValidationStatus.Valid, "authenticated_signal_confirmed")
            : new(ExternalSessionValidationStatus.ValidationFailed, "authenticated_state_unconfirmed");
    }
}
public sealed class WorkdayExternalSessionValidator(ExternalNavigationPolicy n) : ConservativeExternalSessionValidator(JobSiteIdentifier.Workday, n);
public sealed class LinkedInExternalSessionValidator(ExternalNavigationPolicy n) : ConservativeExternalSessionValidator(JobSiteIdentifier.LinkedIn, n);
public sealed class NaukriExternalSessionValidator(ExternalNavigationPolicy n) : ConservativeExternalSessionValidator(JobSiteIdentifier.Naukri, n);
public sealed class IndeedExternalSessionValidator(ExternalNavigationPolicy n) : ConservativeExternalSessionValidator(JobSiteIdentifier.Indeed, n);
public sealed class FounditExternalSessionValidator(ExternalNavigationPolicy n) : ConservativeExternalSessionValidator(JobSiteIdentifier.Foundit, n);
public sealed class WellfoundExternalSessionValidator(ExternalNavigationPolicy n) : ConservativeExternalSessionValidator(JobSiteIdentifier.Wellfound, n);

public sealed class ExternalSessionCaptureManager(IExternalSessionCaptureTransport transport, IOptions<AIApplyOptions> options, TimeProvider clock) : IExternalSessionCaptureCleanup, IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Capture> captures = new();
    private readonly object capacity = new();

    public async Task<Capture> StartAsync(Guid userId, ExternalSessionSiteDescriptor site, long? baselineVersion, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var capture = new Capture(Guid.NewGuid(), userId, site, now, now.AddMinutes(options.Value.ExternalSessions.Capture.MaximumDurationMinutes), baselineVersion, InstanceId());
        lock (capacity)
        {
            var active = captures.Values.Count(x => !x.Terminal && x.ExpiresAtUtc > now);
            var owned = captures.Values.Count(x => x.UserId == userId && !x.Terminal && x.ExpiresAtUtc > now);
            if (active >= options.Value.ExternalSessions.Capture.MaximumConcurrentGlobal || owned >= options.Value.ExternalSessions.Capture.MaximumConcurrentPerUser)
                throw new ConflictException("An external login capture is already active.", "external_session_capture_limit");
            if (!captures.TryAdd(capture.Id, capture)) throw new ConflictException("Unable to allocate capture.", "external_session_capture_unavailable");
        }
        try
        {
            capture.Browser = await transport.StartAsync(site, ct);
            capture.Status = ExternalSessionCaptureStatus.AwaitingCandidate; capture.LastActivityAtUtc = now;
            return capture;
        }
        catch { captures.TryRemove(capture.Id, out _); if (capture.Browser is not null) await capture.Browser.DisposeAsync(); throw; }
    }

    public async Task<Capture?> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct)
    {
        if (!captures.TryGetValue(id, out var capture) || capture.UserId != userId) return null;
        await ExpireAsync(capture, ct); capture.LastActivityAtUtc = clock.GetUtcNow().UtcDateTime; return capture;
    }

    public async Task<Capture> AttachAsync(Guid userId, Guid id, string connectionId, CancellationToken ct)
    {
        var capture = await GetOwnedAsync(userId, id, ct) ?? throw new NotFoundException("External login capture was not found.");
        if (capture.Status != ExternalSessionCaptureStatus.AwaitingCandidate || capture.Browser is null)
            throw new ConflictException("Capture is not available for interaction.", "external_session_capture_state");
        lock (capacity)
        {
            var total = captures.Values.Sum(x => x.Connections.Count);
            if (!capture.Connections.Contains(connectionId) && total >= options.Value.ExternalSessions.Capture.Transport.MaximumConcurrentConnections)
                throw new ConflictException("Interactive browser capacity is currently full.", "external_session_transport_limit");
            capture.Connections.Add(connectionId);
        }
        return capture;
    }

    public Task DetachAsync(Guid userId, Guid id, string connectionId)
    {
        if (captures.TryGetValue(id, out var capture) && capture.UserId == userId)
            lock (capacity) capture.Connections.Remove(connectionId);
        return Task.CompletedTask;
    }
    public Task DetachConnectionAsync(Guid userId, string connectionId)
    {
        lock (capacity)
            foreach (var capture in captures.Values.Where(x => x.UserId == userId)) capture.Connections.Remove(connectionId);
        return Task.CompletedTask;
    }

    public async Task<T> InteractAsync<T>(Guid userId, Guid id, string connectionId, string category,
        Func<IExternalSessionCaptureBrowser, Task<T>> action, CancellationToken ct)
    {
        var capture = await GetOwnedAsync(userId, id, ct) ?? throw new NotFoundException("External login capture was not found.");
        await capture.Gate.WaitAsync(ct);
        try
        {
            bool connected; lock (capacity) connected = capture.Connections.Contains(connectionId);
            if (capture.Status != ExternalSessionCaptureStatus.AwaitingCandidate || capture.Browser is null || !connected)
                throw new ConflictException("Capture interaction is unavailable.", "external_session_transport_unavailable");
            var now = clock.GetUtcNow().UtcDateTime;
            var limit = category == "frame" ? options.Value.ExternalSessions.Capture.Transport.MaxFramesPerSecond : options.Value.ExternalSessions.Capture.Transport.MaximumInputEventsPerSecond;
            while (capture.Events.Count > 0 && capture.Events.Peek().At <= now.AddSeconds(-1)) capture.Events.Dequeue();
            if (capture.Events.Count(x => x.Category == category) >= limit)
                throw new ConflictException("Interactive browser event rate exceeded.", "external_session_transport_rate_limit");
            capture.Events.Enqueue((now, category)); capture.LastActivityAtUtc = now;
            return await action(capture.Browser);
        }
        finally { capture.Gate.Release(); }
    }

    public async Task<bool> BeginValidationAsync(Capture capture, CancellationToken ct)
    {
        await capture.Gate.WaitAsync(ct);
        try
        {
            await ExpireCoreAsync(capture);
            if (capture.Status == ExternalSessionCaptureStatus.Completed) { capture.Gate.Release(); return false; }
            if (capture.Status != ExternalSessionCaptureStatus.AwaitingCandidate) throw new ConflictException("Capture cannot be completed in its current state.", "external_session_capture_state");
            capture.Status = ExternalSessionCaptureStatus.Validating; capture.LastActivityAtUtc = clock.GetUtcNow().UtcDateTime; return true;
        }
        catch { capture.Gate.Release(); throw; }
    }

    public async Task FinishAsync(Capture capture, ExternalSessionCaptureStatus status)
    {
        try { capture.Status = status; capture.LastActivityAtUtc = clock.GetUtcNow().UtcDateTime; await DisposeBrowserAsync(capture); }
        finally { capture.Gate.Release(); }
    }

    public async Task<bool> CancelAsync(Capture capture, CancellationToken ct)
    {
        await capture.Gate.WaitAsync(ct);
        try
        {
            if (capture.Status == ExternalSessionCaptureStatus.Cancelled) return true;
            if (capture.Status == ExternalSessionCaptureStatus.Completed) return false;
            capture.Status = ExternalSessionCaptureStatus.Cancelled; capture.LastActivityAtUtc = clock.GetUtcNow().UtcDateTime;
            await DisposeBrowserAsync(capture); return true;
        }
        finally { capture.Gate.Release(); }
    }

    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        var count = 0;
        foreach (var capture in captures.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (await ExpireAsync(capture, ct)) count++;
            if (capture.Terminal && captures.TryRemove(capture.Id, out _)) count++;
        }
        return count;
    }
    private async Task<bool> ExpireAsync(Capture capture, CancellationToken ct) { await capture.Gate.WaitAsync(ct); try { return await ExpireCoreAsync(capture); } finally { capture.Gate.Release(); } }
    private async Task<bool> ExpireCoreAsync(Capture capture)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (capture.Terminal || now < capture.ExpiresAtUtc && now < capture.LastActivityAtUtc.AddMinutes(options.Value.ExternalSessions.Capture.IdleTimeoutMinutes)) return false;
        capture.Status = ExternalSessionCaptureStatus.Expired; await DisposeBrowserAsync(capture); return true;
    }
    private static async Task DisposeBrowserAsync(Capture capture) { if (capture.Browser is null) return; var browser = capture.Browser; capture.Browser = null; await browser.DisposeAsync(); }
    private static string InstanceId() =>
        new[] { Environment.GetEnvironmentVariable("RENDER_INSTANCE_ID"), Environment.GetEnvironmentVariable("HOSTNAME"), Environment.MachineName }
            .First(value => !string.IsNullOrWhiteSpace(value))!;
    public async ValueTask DisposeAsync() { foreach (var capture in captures.Values) await DisposeBrowserAsync(capture); captures.Clear(); }

    public sealed class Capture(Guid id, Guid userId, ExternalSessionSiteDescriptor site, DateTime created, DateTime expires, long? baselineVersion, string ownerInstanceId)
    {
        public Guid Id { get; } = id; public Guid UserId { get; } = userId; public ExternalSessionSiteDescriptor Site { get; } = site;
        internal string OwnerInstanceId { get; } = ownerInstanceId;
        public DateTime CreatedAtUtc { get; } = created; public DateTime ExpiresAtUtc { get; } = expires; public DateTime LastActivityAtUtc { get; set; } = created;
        public long? BaselineVersion { get; } = baselineVersion; public ExternalSessionCaptureStatus Status { get; set; } = ExternalSessionCaptureStatus.Starting;
        public IExternalSessionCaptureBrowser? Browser { get; set; } public SemaphoreSlim Gate { get; } = new(1, 1);
        public HashSet<string> Connections { get; } = new(StringComparer.Ordinal); public Queue<(DateTime At, string Category)> Events { get; } = new();
        public bool Terminal => Status is ExternalSessionCaptureStatus.Completed or ExternalSessionCaptureStatus.Failed or ExternalSessionCaptureStatus.Expired or ExternalSessionCaptureStatus.Cancelled;
    }
}
