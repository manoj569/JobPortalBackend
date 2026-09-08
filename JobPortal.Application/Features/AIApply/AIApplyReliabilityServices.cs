using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.AIApply;

public sealed class AIApplyRetryPolicy(IOptions<AIApplyOptions> options) : IAIApplyRetryPolicy
{
    public AIApplyRetryDecision Decide(AIApplyFailureKind kind, string code, int attempt, bool submissionAttempted, DateTime now)
    {
        if (submissionAttempted || kind == AIApplyFailureKind.SubmissionUnconfirmed) return new(false, null, AIApplyFailureClassification.SubmissionUnconfirmed, "manual_review_required", false);
        var classification = kind switch { AIApplyFailureKind.Timeout => AIApplyFailureClassification.Transient, AIApplyFailureKind.WebsiteError => AIApplyFailureClassification.SiteUnavailable, AIApplyFailureKind.OperationalLimitExceeded => AIApplyFailureClassification.OperationalLimitExceeded, AIApplyFailureKind.HumanVerificationRequired => AIApplyFailureClassification.HumanVerificationRequired, AIApplyFailureKind.LoginRequired => AIApplyFailureClassification.AuthenticationRequired, AIApplyFailureKind.MissingUserInformation or AIApplyFailureKind.RequiredUserDeclaration => AIApplyFailureClassification.UserActionRequired, AIApplyFailureKind.UnsupportedApplicationFlow => AIApplyFailureClassification.UnsupportedFlow, AIApplyFailureKind.Duplicate or AIApplyFailureKind.JobExpired or AIApplyFailureKind.ApplicationUnavailable => AIApplyFailureClassification.Permanent, _ => AIApplyFailureClassification.InternalFailure };
        var transient = classification is AIApplyFailureClassification.Transient or AIApplyFailureClassification.SiteUnavailable or AIApplyFailureClassification.InternalFailure;
        var maximum = Math.Clamp(options.Value.MaxRetryAttempts, 0, 10); if (!transient) return new(false, null, classification, "not_retryable", false);
        if (attempt >= maximum) return new(false, null, classification, "retry_limit_exhausted", true);
        var raw = Math.Min(Math.Clamp(options.Value.RetryMaximumDelaySeconds, 1, 86400), Math.Clamp(options.Value.RetryBaseDelaySeconds, 1, 3600) * Math.Pow(2, attempt));
        var jitter = raw * (double)Math.Clamp(options.Value.RetryJitterRatio, 0, .5m) * ((Math.Abs(HashCode.Combine(code, attempt)) % 2001 - 1000) / 1000d);
        return new(true, now.AddSeconds(Math.Max(1, raw + jitter)), classification, "bounded_exponential_backoff", false);
    }
}

public sealed class AIApplyWorkerIdentity : IAIApplyWorkerIdentity
{
    public string Id { get; } = $"{Environment.MachineName[..Math.Min(32, Environment.MachineName.Length)]}-{Environment.ProcessId}-{Guid.NewGuid():N}"[..Math.Min(80, $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}".Length)];
}
public sealed class AIApplyWorkerState(IAIApplyWorkerIdentity identity, TimeProvider clock) : IAIApplyWorkerState
{
    private readonly object sync = new(); private DateTime started; private DateTime heartbeat; private int active;
    public void Started() { lock (sync) started = heartbeat = clock.GetUtcNow().UtcDateTime; }
    public void Heartbeat(int activeExecutions) { lock (sync) { heartbeat = clock.GetUtcNow().UtcDateTime; active = activeExecutions; } }
    public AIApplyWorkerSnapshot Snapshot() { lock (sync) return new(identity.Id, started, heartbeat, active, heartbeat == default ? "NotStarted" : "Running"); }
}
public sealed class ApplicationExecutionCheckpoint(IAIApplyRepository repository, TimeProvider clock) : IApplicationExecutionCheckpoint
{
    public async Task MarkSubmissionAttemptedAsync(Guid applicationId, CancellationToken ct) { if (!await repository.MarkSubmissionAttemptedAsync(applicationId, clock.GetUtcNow().UtcDateTime, ct)) throw new InvalidOperationException("Application execution lease is no longer active."); }
    public async Task MarkSubmissionConfirmedAsync(Guid applicationId, CancellationToken ct) { if (!await repository.MarkSubmissionConfirmedAsync(applicationId, clock.GetUtcNow().UtcDateTime, ct)) throw new InvalidOperationException("Application execution lease is no longer active."); }
}

public sealed class AIApplyOptionsValidator(bool isDevelopment) : IValidateOptions<AIApplyOptions>
{
    public ValidateOptionsResult Validate(string? name, AIApplyOptions value)
    {
        if (!isDevelopment && value.Browser.AllowLoopbackForTests)
            return ValidateOptionsResult.Fail("AIApply:Browser:AllowLoopbackForTests must be false outside Development.");
        var failures = new List<string>();
        if (value.ExternalSessions.MaximumLifetimeDays is < 1 or > 30)
            failures.Add("AIApply:ExternalSessions:MaximumLifetimeDays must be between 1 and 30.");
        if (value.ExternalSessions.MaximumStorageStateBytes is < 4096 or > 1_048_576)
            failures.Add("AIApply:ExternalSessions:MaximumStorageStateBytes must be between 4096 and 1048576.");
        if (value.ExternalSessions.Enabled && (!isDevelopment || value.ExternalSessions.RequirePersistentDataProtectionKeys) &&
            string.IsNullOrWhiteSpace(value.ExternalSessions.DataProtectionKeysPath))
            failures.Add("AIApply:ExternalSessions:DataProtectionKeysPath is required when persistent external sessions are enabled.");
        else if (value.ExternalSessions.Enabled && !string.IsNullOrWhiteSpace(value.ExternalSessions.DataProtectionKeysPath) &&
            !Path.IsPathFullyQualified(value.ExternalSessions.DataProtectionKeysPath))
            failures.Add("AIApply:ExternalSessions:DataProtectionKeysPath must be an absolute path.");
        if (value.ExternalSessions.Enabled && !isDevelopment && !value.ExternalSessions.RequirePersistentDataProtectionKeys)
            failures.Add("AIApply:ExternalSessions:RequirePersistentDataProtectionKeys must be true outside Development.");
        if (string.IsNullOrWhiteSpace(value.ExternalSessions.DataProtectionCertificatePath) !=
            string.IsNullOrWhiteSpace(value.ExternalSessions.DataProtectionCertificatePassword))
            failures.Add("AI Apply Data Protection certificate path and password must be configured together.");
        var capture = value.ExternalSessions.Capture;
        if (capture.MaximumDurationMinutes is < 1 or > 30 || capture.IdleTimeoutMinutes is < 1 or > 15 ||
            capture.IdleTimeoutMinutes > capture.MaximumDurationMinutes || capture.MaximumConcurrentPerUser is < 1 or > 2 ||
            capture.MaximumConcurrentGlobal is < 1 or > 50 || capture.MaximumConcurrentPerUser > capture.MaximumConcurrentGlobal)
            failures.Add("AI Apply external-session capture limits are invalid.");
        if (capture.Enabled && !value.ExternalSessions.Enabled)
            failures.Add("AI Apply external-session capture cannot be enabled while persistent external sessions are disabled.");
        var transport = capture.Transport;
        if (transport.MaxFramesPerSecond is < 1 or > 5 || transport.MaximumFrameBytes is < 65536 or > 1_048_576 ||
            transport.MaximumViewportWidth is < 640 or > 1920 || transport.MaximumViewportHeight is < 480 or > 1440 ||
            transport.MaximumConcurrentConnections is < 1 or > 50 || transport.MaximumInputEventsPerSecond is < 1 or > 60 ||
            transport.MaximumTextInputCharacters is < 1 or > 1024)
            failures.Add("AI Apply external-session transport limits are invalid.");
        if (transport.Enabled && (!value.Enabled || !value.Browser.Enabled || !capture.Enabled || !transport.StickyAffinityConfigured ||
            !transport.Mode.Equals("SignalRRemoteBrowser", StringComparison.Ordinal)))
            failures.Add("AI Apply external-session transport requires AI Apply browser capture, SignalRRemoteBrowser mode, and configured sticky affinity.");
        if (!value.Enabled)
            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        if (value.MaxConcurrentApplications is < 1 or > 20) failures.Add("AIApply:MaxConcurrentApplications must be between 1 and 20.");
        if (value.PerUserMaxConcurrentApplications is < 1 or > 5 || value.PerUserMaxConcurrentApplications > value.MaxConcurrentApplications) failures.Add("AIApply:PerUserMaxConcurrentApplications must be between 1 and 5 and not exceed total concurrency.");
        if (value.ApplicationTimeoutMinutes is < 1 or > 120) failures.Add("AIApply:ApplicationTimeoutMinutes must be between 1 and 120.");
        if (value.LeaseSeconds < 30 || value.LeaseRenewalSeconds < 5 || value.LeaseRenewalSeconds >= value.LeaseSeconds) failures.Add("AI Apply lease settings are invalid.");
        if (value.MaxRetryAttempts is < 0 or > 10 || value.RetryBaseDelaySeconds < 1 || value.RetryMaximumDelaySeconds < value.RetryBaseDelaySeconds || value.RetryJitterRatio is < 0 or > .5m) failures.Add("AI Apply retry settings are invalid.");
        if (value.RecoveryBatchSize is < 1 or > 100) failures.Add("AIApply:RecoveryBatchSize must be between 1 and 100.");
        if (value.Reliability.SiteHealthMinimumSamples is < 2 or > 100 || value.Reliability.SiteFailureThreshold is < .1m or > 1m || value.Reliability.SiteObservationMinutes < 1 || value.Reliability.CircuitCooldownSeconds < 10) failures.Add("AI Apply site reliability settings are invalid.");
        var telemetry = value.Observability;
        if (telemetry.WorkerStaleSeconds < 30 || telemetry.WorkerRetentionDays is < 1 or > 90 || telemetry.HeartbeatIntervalSeconds is < 5 or > 300 || telemetry.HalfOpenProbeLeaseSeconds is < 10 or > 300 || telemetry.AlertEvaluationWindowMinutes is < 1 or > 1440 || telemetry.QueueDepthWarningThreshold < 1 || telemetry.OldestQueueAgeWarningMinutes < 1 || telemetry.DeadLetterWarningThreshold < 1 || telemetry.RetryRateWarningPercent is < 0 or > 100 || telemetry.SubmissionUnconfirmedWarningPercent is < 0 or > 100 || telemetry.NeedsReviewWarningPercent is < 0 or > 100 || telemetry.BrowserFailureWarningPercent is < 0 or > 100 || telemetry.CostPerApplicationWarning <= 0)
            failures.Add("AI Apply observability settings are invalid.");
        if (value.Browser.SubmissionConfidenceThreshold is <= 0 or > 1 || value.Browser.MaximumSteps is < 1 or > 50 || value.Browser.NavigationTimeoutSeconds < 2 || value.Browser.ActionTimeoutSeconds < 1 || value.Browser.SubmissionDetectionTimeoutSeconds < 1) failures.Add("AI Apply browser settings are invalid.");
        if (value.MaximumBrowserSecondsPerApplication <= 0 || value.MaximumAIRequestsPerApplication < 1 || value.MaximumEstimatedCostPerApplication <= 0) failures.Add("AI Apply operational cost limits must be finite positive values.");
        if (value.AI.Enabled && (string.IsNullOrWhiteSpace(value.AI.Provider) || value.AI.Provider.Equals("None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value.AI.Model))) failures.Add("AI Apply AI provider and model are required when AI integration is enabled.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
