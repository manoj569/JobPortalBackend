using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Infrastructure.AIApply;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace JobPortal.API.Health;

public sealed class AIApplyWorkerHealthCheck(IOptions<AIApplyOptions> options, IAIApplyWorkerState worker, TimeProvider clock) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return Task.FromResult(HealthCheckResult.Healthy("AI Apply is intentionally disabled."));
        var snapshot = worker.Snapshot();
        var staleAfter = TimeSpan.FromSeconds(Math.Max(30, Math.Clamp(options.Value.QueuePollingIntervalSeconds, 2, 300) * 4));
        return Task.FromResult(snapshot.LastHeartbeatAtUtc != default && clock.GetUtcNow().UtcDateTime - snapshot.LastHeartbeatAtUtc <= staleAfter
            ? HealthCheckResult.Healthy("AI Apply worker heartbeat is current.")
            : HealthCheckResult.Unhealthy("AI Apply worker heartbeat is stale."));
    }
}

public sealed class AIApplyBrowserHealthCheck(IOptions<AIApplyOptions> options, IPlaywrightBrowserRuntime browser) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled || !options.Value.Browser.Enabled) return HealthCheckResult.Healthy("AI Apply browser is intentionally disabled.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await browser.IsAvailableAsync(timeout.Token)
                ? HealthCheckResult.Healthy("Playwright Chromium is available.")
                : HealthCheckResult.Unhealthy("Playwright Chromium is unavailable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return HealthCheckResult.Unhealthy("Playwright Chromium health check timed out."); }
        catch { return HealthCheckResult.Unhealthy("Playwright Chromium is unavailable."); }
    }
}

public sealed class AIApplyExternalSessionHealthCheck(IOptions<AIApplyOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value.ExternalSessions;
        if (!settings.Enabled)
            return Task.FromResult(HealthCheckResult.Healthy("Persistent external sessions are intentionally disabled."));
        if (string.IsNullOrWhiteSpace(settings.DataProtectionKeysPath) || !Directory.Exists(settings.DataProtectionKeysPath))
            return Task.FromResult(HealthCheckResult.Unhealthy("Persistent external-session key storage is unavailable."));
        return Task.FromResult(HealthCheckResult.Healthy("Persistent external-session infrastructure is configured."));
    }
}

public sealed class AIApplyCaptureTransportHealthCheck(IOptions<AIApplyOptions> options, IExternalSessionCaptureTransport transport) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.ExternalSessions.Capture.Transport.Enabled)
            return Task.FromResult(HealthCheckResult.Healthy("Interactive capture transport is intentionally disabled."));
        return Task.FromResult(transport.IsAvailable
            ? HealthCheckResult.Healthy("Interactive capture transport is initialized.")
            : HealthCheckResult.Unhealthy("Interactive capture transport is unavailable."));
    }
}
