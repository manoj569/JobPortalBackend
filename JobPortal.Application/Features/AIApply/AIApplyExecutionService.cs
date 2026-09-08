using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.AIApply;

public sealed class AIApplyOptions
{
    public const string SectionName = "AIApply";
    public bool Enabled { get; set; }
    public string StandardPlanName { get; set; } = "AI Apply";
    public string ProPlanName { get; set; } = "AI Apply Pro";
    public decimal StandardMonthlyAmount { get; set; } = 999;
    public decimal ProMonthlyAmount { get; set; } = 1499;
    public string CurrencyCode { get; set; } = "INR";
    public int PlanDurationDays { get; set; } = 30;
    public int MaxConcurrentApplications { get; set; } = 2;
    public int PerUserMaxConcurrentApplications { get; set; } = 1;
    public int ApplicationTimeoutMinutes { get; set; } = 15;
    public int MaxRetryAttempts { get; set; } = 2;
    public int QueuePollingIntervalSeconds { get; set; } = 15;
    public decimal AIInputTokenCostPerMillion { get; set; }
    public decimal AIOutputTokenCostPerMillion { get; set; }
    public decimal BrowserCostPerSecond { get; set; }
    public int StandardQueuePriority { get; set; } = 100;
    public int ProQueuePriority { get; set; } = 200;
    public int LeaseSeconds { get; set; } = 180;
    public int LeaseRenewalSeconds { get; set; } = 45;
    public int RetryBaseDelaySeconds { get; set; } = 30;
    public int RetryMaximumDelaySeconds { get; set; } = 900;
    public int RecoveryBatchSize { get; set; } = 25;
    public decimal RetryJitterRatio { get; set; } = .2m;
    public decimal MaximumBrowserSecondsPerApplication { get; set; } = 900;
    public int MaximumAIRequestsPerApplication { get; set; } = 10;
    public decimal MaximumEstimatedCostPerApplication { get; set; } = 100;
    public AIApplyReliabilityOptions Reliability { get; set; } = new();
    public AIApplyBrowserOptions Browser { get; set; } = new();
    public AIApplyAIOptions AI { get; set; } = new();
    public AIApplySiteOptions Sites { get; set; } = new();
    public AIApplyExternalSessionOptions ExternalSessions { get; set; } = new();
    public AIApplyObservabilityOptions Observability { get; set; } = new();
}

public sealed class AIApplyAIOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "None";
    public string Model { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
    public bool SemanticMatchingEnabled { get; set; }
    public bool AnswerGenerationEnabled { get; set; }
    public decimal SemanticMatchThreshold { get; set; } = .90m;
    public decimal AutoUseThreshold { get; set; } = .95m;
    public decimal GeneratedAnswerThreshold { get; set; } = .95m;
    public int MaxCandidateAnswers { get; set; } = 20;
    public int MaxGeneratedAnswerCharacters { get; set; } = 1500;
    public int RequestTimeoutSeconds { get; set; } = 15;
}

public sealed class AIApplyBrowserOptions
{
    public bool Enabled { get; set; }
    public bool Headless { get; set; } = true;
    public int NavigationTimeoutSeconds { get; set; } = 30;
    public int ActionTimeoutSeconds { get; set; } = 10;
    public int SubmissionDetectionTimeoutSeconds { get; set; } = 10;
    public int MaximumSteps { get; set; } = 8;
    public decimal SubmissionConfidenceThreshold { get; set; } = 0.8m;
    public bool CaptureFailureScreenshot { get; set; }
    public bool AllowLoopbackForTests { get; set; }
}

public sealed class AIApplySiteOptions
{
    public AIApplySiteSetting Greenhouse { get; set; } = new(); public AIApplySiteSetting Lever { get; set; } = new();
    public AIApplySiteSetting Ashby { get; set; } = new(); public AIApplySiteSetting SmartRecruiters { get; set; } = new();
    public AIApplySiteSetting Workday { get; set; } = new(); public AIApplySiteSetting LinkedIn { get; set; } = new();
    public AIApplySiteSetting Naukri { get; set; } = new(); public AIApplySiteSetting Indeed { get; set; } = new();
    public AIApplySiteSetting Foundit { get; set; } = new(); public AIApplySiteSetting Wellfound { get; set; } = new();
}
public sealed class AIApplySiteSetting { public bool Enabled { get; set; } public bool SupportsPersistentSession { get; set; } public bool PersistentSessionValidatedForProduction { get; set; } public string? LoginEntryUrl { get; set; } public string[] AllowedAuthenticationHosts { get; set; } = []; public int? NavigationTimeoutSeconds { get; set; } public int? ActionTimeoutSeconds { get; set; } public int? MaximumSteps { get; set; } }
public sealed class AIApplyExternalSessionOptions
{
    public bool Enabled { get; set; }
    public int MaximumLifetimeDays { get; set; } = 30;
    public int MaximumStorageStateBytes { get; set; } = 262_144;
    public bool RequirePersistentDataProtectionKeys { get; set; } = true;
    public string? DataProtectionKeysPath { get; set; }
    public string? DataProtectionCertificatePath { get; set; }
    public string? DataProtectionCertificatePassword { get; set; }
    public AIApplyExternalSessionCaptureOptions Capture { get; set; } = new();
}
public sealed class AIApplyExternalSessionCaptureOptions
{
    public bool Enabled { get; set; }
    public int MaximumDurationMinutes { get; set; } = 10;
    public int IdleTimeoutMinutes { get; set; } = 5;
    public int MaximumConcurrentPerUser { get; set; } = 1;
    public int MaximumConcurrentGlobal { get; set; } = 10;
    public AIApplyExternalSessionTransportOptions Transport { get; set; } = new();
}
public sealed class AIApplyExternalSessionTransportOptions
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "SignalRRemoteBrowser";
    public bool StickyAffinityConfigured { get; set; }
    public int MaxFramesPerSecond { get; set; } = 3;
    public int MaximumFrameBytes { get; set; } = 524_288;
    public int MaximumViewportWidth { get; set; } = 1600;
    public int MaximumViewportHeight { get; set; } = 1200;
    public int MaximumConcurrentConnections { get; set; } = 10;
    public int MaximumInputEventsPerSecond { get; set; } = 30;
    public int MaximumTextInputCharacters { get; set; } = 512;
}
public sealed class AIApplyReliabilityOptions { public int SiteHealthMinimumSamples { get; set; } = 5; public decimal SiteFailureThreshold { get; set; } = .6m; public int SiteObservationMinutes { get; set; } = 15; public int CircuitCooldownSeconds { get; set; } = 300; }
public sealed class AIApplyObservabilityOptions
{
    public bool Enabled { get; set; } = true;
    public int WorkerStaleSeconds { get; set; } = 90;
    public int WorkerRetentionDays { get; set; } = 7;
    public int HeartbeatIntervalSeconds { get; set; } = 15;
    public int HalfOpenProbeLeaseSeconds { get; set; } = 60;
    public int AlertEvaluationWindowMinutes { get; set; } = 15;
    public int QueueDepthWarningThreshold { get; set; } = 100;
    public int OldestQueueAgeWarningMinutes { get; set; } = 30;
    public int DeadLetterWarningThreshold { get; set; } = 10;
    public decimal RetryRateWarningPercent { get; set; } = 25;
    public decimal SubmissionUnconfirmedWarningPercent { get; set; } = 10;
    public decimal NeedsReviewWarningPercent { get; set; } = 10;
    public decimal BrowserFailureWarningPercent { get; set; } = 20;
    public decimal CostPerApplicationWarning { get; set; } = 100;
}

public sealed class AIApplyExecutionService(IAIApplyRepository repository,
    IAIApplyAuthorizationService authorization, IJobApplicationBrowserAgent browser,
    IApplicationQuestionMatcher questionMatcher, IApplicationAnswerResolver answerResolver, IOptions<AIApplyOptions> options,
    IAIApplyRetryPolicy retryPolicy, IAIApplyWorkerIdentity workerIdentity, TimeProvider clock) : IAIApplyExecutionService
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        using var activity = AIApplyTelemetry.ActivitySource.StartActivity("aiapply.queue.process", System.Diagnostics.ActivityKind.Internal);
        var now = clock.GetUtcNow().UtcDateTime;
        var recovered = await repository.RecoverExpiredLeasesAsync(now, Math.Clamp(options.Value.RecoveryBatchSize, 1, 100), ct);
        if (recovered > 0) AIApplyTelemetry.StaleLeasesRecovered.Add(recovered, AIApplyTelemetry.Tags("lease_recovery", "recovered"));
        var executionTimeoutSeconds = Math.Clamp(options.Value.ApplicationTimeoutMinutes, 1, 120) * 60;
        var leaseSeconds = Math.Max(Math.Clamp(options.Value.LeaseSeconds, 30, 7200), executionTimeoutSeconds + 30);
        var application = await repository.ClaimNextAsync(now,
            Math.Clamp(options.Value.PerUserMaxConcurrentApplications, 1, 5), workerIdentity.Id, now.AddSeconds(leaseSeconds), ct);
        if (application is null) { activity?.SetTag("result", "empty"); return false; }
        AIApplyTelemetry.QueueClaims.Add(1, AIApplyTelemetry.Tags("claim", "claimed"));
        AIApplyTelemetry.QueueWaitSeconds.Record(Math.Max(0, (now - application.CreatedAtUtc).TotalSeconds), AIApplyTelemetry.Tags("claim", "claimed"));
        AIApplyTelemetry.ApplicationsStarted.Add(1, AIApplyTelemetry.Tags("execution", "started"));
        activity?.SetTag("operation", "application_execution");
        var access = await authorization.GetAccessAsync(application.UserId, ct);
        if (!access.CanUseAIApply)
        {
            application.FailureKind = access.SubscriptionExpired ? AIApplyFailureKind.ApplicationUnavailable : AIApplyFailureKind.MissingUserInformation;
            application.LastErrorCode = "ai_apply_subscription_inactive";
            AIApplyStateMachine.Transition(application, AIApplyRunStatus.Failed, now);
            ReleaseLease(application);
            await LogAsync(application, AIApplyExecutionEvent.ApplicationFailed, "subscription_inactive", ct);
            await repository.SaveChangesAsync(ct); return true;
        }
        var setting = await repository.GetSettingsAsync(application.UserId, ct);
        if (setting is not { Enabled: true, Paused: false })
        {
            AIApplyStateMachine.Transition(application, AIApplyRunStatus.Queued, now);
            application.ScheduledAtUtc = now.AddMinutes(5);
            ReleaseLease(application);
            await repository.SaveChangesAsync(ct); return true;
        }
        await LogAsync(application, AIApplyExecutionEvent.ApplicationStarted, "application_started", ct);
        BrowserApplicationResult result;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(options.Value.ApplicationTimeoutMinutes, 1, 120)));
        try { result = await browser.ApplyAsync(new(application.Id, application.UserId, new Uri(application.ExternalApplicationUrl), setting.RequireConfirmationBeforeSubmit), timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = new(false, AIApplyFailureKind.Timeout, "application_timeout"); }
        if (!result.SubmissionConfirmed && result.Usage is { } measured && (measured.BrowserSeconds > options.Value.MaximumBrowserSecondsPerApplication || measured.AIRequests > options.Value.MaximumAIRequestsPerApplication || EstimatedCost(measured) > options.Value.MaximumEstimatedCostPerApplication))
            result = result with { FailureKind = AIApplyFailureKind.OperationalLimitExceeded, ResultCode = "operational_limit_exceeded", Questions = null };
        if (result.Questions is { Count: > 0 })
        {
            var unknownCount = 0;
            var aiRequests = 0; long aiInputTokens = 0, aiOutputTokens = 0;
            foreach (var detected in result.Questions.Take(25))
            {
                var resolved = await answerResolver.ResolveAsync(application.UserId, detected, setting.AllowAIGeneratedAnswers, ct);
                if (resolved.Usage is not null) { aiRequests += resolved.Usage.AIRequests; aiInputTokens += resolved.Usage.InputTokens; aiOutputTokens += resolved.Usage.OutputTokens; }
                await LogAsync(application, AIApplyExecutionEvent.QuestionClassified, "question_classified", ct);
                if (resolved.Found && resolved.CanAutoUse && resolved.Answer is not null)
                {
                    var normalized = questionMatcher.Normalize(detected.Text);
                    var alias = await repository.FindAnswerAsync(application.UserId, normalized, ct);
                    if (alias is null) { alias = new UserApplicationAnswer { UserId = application.UserId, Question = detected.Text[..Math.Min(2000, detected.Text.Length)], NormalizedQuestion = normalized }; await repository.AddAsync(alias, ct); }
                    alias.Answer = resolved.Answer; alias.Category = resolved.CanonicalKey; alias.Source = resolved.Source == ApplicationAnswerResolutionSource.ProfileDerived ? AIApplyAnswerSource.ProfileDerived : AIApplyAnswerSource.UserVerified; alias.Confidence = resolved.Confidence; alias.IsVerified = resolved.Source != ApplicationAnswerResolutionSource.ProfileDerived; alias.IsActive = true;
                    await LogAsync(application, resolved.Source == ApplicationAnswerResolutionSource.UserVerifiedSemantic ? AIApplyExecutionEvent.SemanticAnswerMatched : resolved.Source == ApplicationAnswerResolutionSource.ProfileDerived ? AIApplyExecutionEvent.ProfileAnswerDerived : AIApplyExecutionEvent.CanonicalAnswerMatched, "answer_resolved", ct);
                    continue;
                }
                var q = new AIApplyQuestion { ApplicationId = application.Id, UserId = application.UserId, Question = detected.Text[..Math.Min(2000, detected.Text.Length)], NormalizedQuestion = questionMatcher.Normalize(detected.Text), QuestionType = detected.Type[..Math.Min(50, detected.Type.Length)], SuggestedAnswer = resolved.Answer };
                await repository.AddAsync(q, ct);
                unknownCount++;
                if (resolved.Source == ApplicationAnswerResolutionSource.AIGenerated) await LogAsync(application, AIApplyExecutionEvent.AIAnswerGenerated, "generated_answer_requires_confirmation", ct);
                await LogAsync(application, AIApplyExecutionEvent.UserConfirmationRequired, "user_confirmation_required", ct);
                await repository.AddAsync(new Notification { UserId = application.UserId, Type = NotificationType.Application, Title = "Application needs your attention", Message = "An application question requires your answer.", ActionUrl = $"/dashboard/ai-apply/applications/{application.Id}/questions/{q.Id}" }, ct);
            }
            if (aiRequests > 0) await RecordCostAsync(application, new(aiRequests, aiInputTokens, aiOutputTokens, 0, 0), ct);
            if (unknownCount > 0)
            {
                application.RequiresUserInput = true;
                application.FailureKind = result.FailureKind == AIApplyFailureKind.None
                    ? AIApplyFailureKind.MissingUserInformation : result.FailureKind;
                AIApplyStateMachine.Transition(application, AIApplyRunStatus.WaitingForUser, now);
                ReleaseLease(application);
                await LogAsync(application, AIApplyExecutionEvent.WaitingForUser, "unknown_question", ct);
            }
            else
            {
                application.RetryCount++;
                AIApplyStateMachine.Transition(application, AIApplyRunStatus.Queued, now);
                application.ScheduledAtUtc = now;
                ReleaseLease(application);
            }
        }
        else if (result.SubmissionConfirmed)
        {
            application.FailureKind = AIApplyFailureKind.None; AIApplyStateMachine.Transition(application, AIApplyRunStatus.Submitted, now);
            application.SubmissionConfirmedAtUtc ??= now; ReleaseLease(application);
            await LogAsync(application, AIApplyExecutionEvent.ApplicationCompleted, "submission_confirmed", ct);
            AIApplyTelemetry.ApplicationsCompleted.Add(1, AIApplyTelemetry.Tags("submission", "confirmed", result.Adapter?.Site.ToString()));
        }
        else
        {
            var decision = retryPolicy.Decide(result.FailureKind, result.ResultCode, application.RetryCount, application.SubmissionAttemptedAtUtc.HasValue, now);
            application.FailureKind = result.FailureKind; application.LastErrorCode = SafeCode(result.ResultCode); application.FailureClassification = decision.Classification.ToString(); application.FirstFailureAtUtc ??= now; application.LastFailureAtUtc = now;
            if (decision.ShouldRetry) { application.RetryCount++; AIApplyStateMachine.Transition(application, AIApplyRunStatus.Queued, now); application.ScheduledAtUtc = decision.NextAttemptAtUtc!.Value; AIApplyTelemetry.RetriesScheduled.Add(1, AIApplyTelemetry.Tags("retry", "scheduled")); AIApplyTelemetry.RetryDelaySeconds.Record((decision.NextAttemptAtUtc.Value - now).TotalSeconds, AIApplyTelemetry.Tags("retry", "scheduled")); }
            else if (decision.Classification == AIApplyFailureClassification.SubmissionUnconfirmed) { AIApplyStateMachine.Transition(application, AIApplyRunStatus.NeedsReview, now); }
            else if (decision.DeadLetter) { AIApplyStateMachine.Transition(application, AIApplyRunStatus.DeadLettered, now); application.DeadLetteredAtUtc = now; await LogAsync(application, AIApplyExecutionEvent.DeadLettered, "retry_limit_exhausted", ct); AIApplyTelemetry.RetryExhausted.Add(1, AIApplyTelemetry.Tags("retry", "exhausted")); }
            else { AIApplyStateMachine.Transition(application, AIApplyRunStatus.Failed, now); await LogAsync(application, AIApplyExecutionEvent.ApplicationFailed, application.LastErrorCode ?? "unknown", ct); }
            if (application.Status == AIApplyRunStatus.Failed && result.FailureKind is AIApplyFailureKind.LoginRequired or AIApplyFailureKind.HumanVerificationRequired)
                await repository.AddAsync(new Notification { UserId = application.UserId, Type = NotificationType.Application, Title = "Action required", Message = "Action required to continue your application.", ActionUrl = $"/dashboard/ai-apply/applications/{application.Id}" }, ct);
            ReleaseLease(application);
        }
        if (result.Events is not null)
        {
            foreach (var browserEvent in result.Events)
            {
                if (Enum.TryParse<AIApplyExecutionEvent>(browserEvent.Code, out var eventType))
                    await LogAsync(application, eventType, browserEvent.Code, ct, result.Adapter);
            }
        }
        if (result.Usage is not null) await RecordCostAsync(application, result.Usage, ct);
        activity?.SetTag("application_status", application.Status.ToString()); activity?.SetTag("result", "processed");
        await repository.SaveChangesAsync(ct); return true;
    }

    private Task LogAsync(AIApplyApplication app, AIApplyExecutionEvent type, string code, CancellationToken ct, SiteAdapterSupportResult? adapter = null) => repository.AddAsync(new AIApplyExecutionLog { ApplicationId = app.Id, EventType = type, Status = app.Status, MessageCode = SafeCode(code) ?? "unknown", StartedAtUtc = clock.GetUtcNow().UtcDateTime, CompletedAtUtc = clock.GetUtcNow().UtcDateTime, MetadataJson = adapter is null ? null : System.Text.Json.JsonSerializer.Serialize(new { site = adapter.Site.ToString(), adapter = adapter.AdapterName, version = adapter.AdapterVersion, support = adapter.SupportLevel.ToString() }) }, ct);
    private Task RecordCostAsync(AIApplyApplication app, AIApplyUsage u, CancellationToken ct) { var o = options.Value; var estimated = u.InputTokens / 1_000_000m * o.AIInputTokenCostPerMillion + u.OutputTokens / 1_000_000m * o.AIOutputTokenCostPerMillion + u.BrowserSeconds * o.BrowserCostPerSecond + u.ProxyCost; AIApplyTelemetry.AIRequests.Add(u.AIRequests, AIApplyTelemetry.Tags("ai_request", "completed")); AIApplyTelemetry.AITokens.Add(u.InputTokens + u.OutputTokens, AIApplyTelemetry.Tags("ai_tokens", "measured")); AIApplyTelemetry.EstimatedCost.Record((double)estimated, AIApplyTelemetry.Tags("application_cost", "measured")); return repository.AddAsync(new AIApplyCost { ApplicationId = app.Id, UserId = app.UserId, AIRequestCount = u.AIRequests, AIInputTokens = u.InputTokens, AIOutputTokens = u.OutputTokens, BrowserExecutionSeconds = u.BrowserSeconds, ProxyCost = u.ProxyCost, RetryCount = app.RetryCount, EstimatedCost = estimated }, ct); }
    private decimal EstimatedCost(AIApplyUsage u) { var o = options.Value; return u.InputTokens / 1_000_000m * o.AIInputTokenCostPerMillion + u.OutputTokens / 1_000_000m * o.AIOutputTokenCostPerMillion + u.BrowserSeconds * o.BrowserCostPerSecond + u.ProxyCost; }
    private static void ReleaseLease(AIApplyApplication application) { application.LeaseOwner = null; application.LeaseExpiresAtUtc = null; }
    private static string? SafeCode(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; var safe = new string(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').Take(100).ToArray()); return safe.Length == 0 ? null : safe; }
}
