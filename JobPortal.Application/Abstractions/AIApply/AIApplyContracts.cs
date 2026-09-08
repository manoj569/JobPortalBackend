using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.AIApply;

public interface IAIApplyRepository
{
    Task<User?> GetUserProfileAsync(Guid userId, CancellationToken ct = default);
    Task<Membership?> GetMembershipAsync(Guid userId, CancellationToken ct = default);
    Task<Job?> GetJobAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetJobsAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default);
    Task<AIApplyProfile?> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<AIApplyPreference?> GetPreferencesAsync(Guid userId, CancellationToken ct = default);
    Task<AIApplySetting?> GetSettingsAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<AIApplyRule>> GetRulesAsync(Guid userId, CancellationToken ct = default);
    Task<AIApplyRule?> GetRuleAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<AIApplyApplication?> GetApplicationAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AIApplyApplication>> GetApplicationsAsync(Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<AIApplyApplication?> ClaimNextAsync(DateTime now, int perUserConcurrency, string workerId, DateTime leaseExpiresAtUtc, CancellationToken ct = default);
    Task<int> RecoverExpiredLeasesAsync(DateTime now, int maximum, CancellationToken ct = default);
    Task<bool> MarkSubmissionAttemptedAsync(Guid applicationId, DateTime now, CancellationToken ct = default);
    Task<bool> MarkSubmissionConfirmedAsync(Guid applicationId, DateTime now, CancellationToken ct = default);
    Task<bool> IsDuplicateAsync(Guid userId, Guid jobId, string normalizedUrl, CancellationToken ct = default);
    Task<bool> TryContinueCandidateActionAsync(Guid userId, Guid applicationId, AIApplyFailureKind expectedFailureKind, DateTime nowUtc, CancellationToken ct = default);
    Task<AIApplyQuestion?> GetQuestionAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AIApplyQuestion>> GetQuestionsAsync(Guid userId, AIApplyQuestionStatus? status, CancellationToken ct = default);
    Task<UserApplicationAnswer?> GetAnswerAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<UserApplicationAnswer?> FindAnswerAsync(Guid userId, string normalizedQuestion, CancellationToken ct = default);
    Task<IReadOnlyList<UserApplicationAnswer>> GetAnswersAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync<T>(T entity, CancellationToken ct = default) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IAIApplyService
{
    Task<AIApplyProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<AIApplyProfileResponse> UpdateProfileAsync(Guid userId, UpdateAIApplyProfileRequest request, CancellationToken ct = default);
    Task<AIApplyPreferencesResponse> GetPreferencesAsync(Guid userId, CancellationToken ct = default);
    Task<AIApplyPreferencesResponse> UpdatePreferencesAsync(Guid userId, UpdateAIApplyPreferencesRequest request, CancellationToken ct = default);
    Task<AIApplySettingsResponse> GetSettingsAsync(Guid userId, CancellationToken ct = default);
    Task<AIApplySettingsResponse> UpdateSettingsAsync(Guid userId, UpdateAIApplySettingsRequest request, CancellationToken ct = default);
    Task<AIApplySettingsResponse> SetModeAsync(Guid userId, AIApplyControlAction action, CancellationToken ct = default);
    Task<IReadOnlyList<AIApplyRuleResponse>> GetRulesAsync(Guid userId, CancellationToken ct = default);
    Task<AIApplyRuleResponse> CreateRuleAsync(Guid userId, UpsertAIApplyRuleRequest request, CancellationToken ct = default);
    Task<AIApplyRuleResponse> UpdateRuleAsync(Guid userId, Guid id, UpsertAIApplyRuleRequest request, CancellationToken ct = default);
    Task DeleteRuleAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<AIApplyApplicationResponse> QueueAsync(Guid userId, QueueAIApplyRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AIApplyApplicationResponse>> GetApplicationsAsync(Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<AIApplyApplicationResponse> GetApplicationAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<AIApplyApplicationResponse> CancelAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<AIApplyApplicationResponse> ContinueAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AIApplyQuestionResponse>> GetQuestionsAsync(Guid userId, AIApplyQuestionStatus? status, CancellationToken ct = default);
    Task<AIApplyQuestionResponse> GetQuestionAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<AIApplyQuestionResponse> AnswerAsync(Guid userId, Guid id, AnswerAIApplyQuestionRequest request, CancellationToken ct = default);
    Task<AIApplyQuestionResponse> SkipQuestionAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AIApplyAnswerResponse>> GetAnswersAsync(Guid userId, CancellationToken ct = default);
    Task<AIApplyAnswerResponse> UpdateAnswerAsync(Guid userId, Guid id, UpdateAIApplyAnswerRequest request, CancellationToken ct = default);
    Task DeleteAnswerAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<AIApplyAnalyticsResponse> AnalyticsAsync(Guid userId, DateTime from, DateTime to, CancellationToken ct = default);
}

public interface IAIApplyAuthorizationService { Task<AIApplyAccess> GetAccessAsync(Guid userId, CancellationToken ct = default); Task<AIApplyAccess> RequireAsync(Guid userId, bool pro = false, CancellationToken ct = default); }
public interface ICandidateExternalApplicationLinkService { Task<CandidateExternalApplicationDestination?> ResolveAsync(string url, CancellationToken ct); }
public sealed record CandidateExternalApplicationDestination(JobSiteIdentifier Site, string DisplayName, string Url, bool SupportsRestartFromUrl, bool OperationallyAvailable);
public sealed record AIApplyAccess(bool SubscriptionActive, bool SubscriptionExpired, AIApplyPlanTier Tier, bool CanUseAIApply, bool CanUseAIApplyPro, string? PlanCode = null, MembershipStatus? SubscriptionStatus = null, DateTime? StartsAt = null, DateTime? ExpiresAt = null, bool CanUseStandardMembership = false, bool CanUseAIGeneratedAnswers = false, bool CanUseAdvancedMatching = false, bool CanUseTailoredApplicationContent = false, int ProcessingPriority = 0);

public interface IAIApplyMatcher { AIApplyMatchResult Match(User user, Job job, AIApplyPreference preferences, IReadOnlyCollection<AIApplyRule> rules); }
public sealed record AIApplyMatchResult(bool IsEligible, decimal MatchScore, IReadOnlyList<string> MatchedSkills, IReadOnlyList<string> MissingSkills, IReadOnlyList<string> Reasons, IReadOnlyList<string> SkipReasons);

public interface IApplicationQuestionMatcher { string Normalize(string question); Task<UserApplicationAnswer?> FindAsync(Guid userId, string question, CancellationToken ct = default); }
public interface IApplicationQuestionClassifier { ApplicationQuestionAnalysis Analyze(DetectedApplicationQuestion question); }
public interface IApplicationSemanticMatcher { Task<SemanticMatchResult> FindBestMatchAsync(ApplicationQuestionAnalysis question, IReadOnlyCollection<UserApplicationAnswer> candidates, CancellationToken ct); }
public interface IApplicationAnswerResolver { Task<ApplicationAnswerResolution> ResolveAsync(Guid userId, DetectedApplicationQuestion question, bool allowGeneratedAnswers, CancellationToken ct); }
public interface IAIApplicationAnswerPolicy { AIAnswerPolicyDecision Evaluate(ApplicationQuestionAnalysis question, bool allowAutoUse); }
public interface IAIApplyLanguageModel { Task<AIApplyLanguageModelResult?> GenerateAsync(AIApplyLanguageModelRequest request, CancellationToken ct); }
public sealed record ApplicationQuestionAnalysis(string OriginalQuestion, string NormalizedQuestion, ApplicationQuestionCategory Category, ApplicationQuestionRisk RiskLevel, string? CanonicalKey, string? Subject, ApplicationAnswerType AnswerType, IReadOnlyList<string> AvailableOptions, bool Required, string SemanticIntent, ApplicationAnswerScope Scope);
public sealed record SemanticMatchResult(Guid? MatchedAnswerId, decimal Similarity, decimal Confidence, string Reason);
public sealed record ApplicationAnswerResolution(bool Found, string? Answer, ApplicationAnswerResolutionSource Source, decimal Confidence, string? CanonicalKey, string? MatchedQuestion, bool RequiresUserConfirmation, bool CanAutoUse, string Reason, AIApplyUsage? Usage = null);
public sealed record AIAnswerPolicyDecision(bool CanGenerate, bool CanAutoSubmit, bool RequiresConfirmation, string Reason);
public sealed record AIApplyLanguageModelRequest(string Question, ApplicationQuestionCategory Category, ApplicationAnswerType AnswerType, int MaximumCharacters, IReadOnlyDictionary<string, string> TrustedFacts);
public sealed record AIApplyLanguageModelResult(string Answer, decimal Confidence, bool RequiresUserConfirmation, long InputTokens, long OutputTokens);
public interface IJobApplicationBrowserAgent { Task<BrowserApplicationResult> ApplyAsync(BrowserApplicationContext context, CancellationToken ct); }
public sealed record BrowserApplicationContext(Guid ApplicationId, Guid UserId, Uri ApplicationUrl, bool RequireConfirmationBeforeSubmit);
public sealed record BrowserApplicationResult(bool SubmissionConfirmed, AIApplyFailureKind FailureKind, string ResultCode, IReadOnlyList<DetectedApplicationQuestion>? Questions = null, AIApplyUsage? Usage = null, SubmissionDetectionResult? Submission = null, IReadOnlyList<BrowserExecutionEvent>? Events = null, SiteAdapterSupportResult? Adapter = null);
public sealed record DetectedApplicationQuestion(string Text, string Type, IReadOnlyList<string>? Options = null, bool Required = true);
public sealed record AIApplyUsage(int AIRequests, long InputTokens, long OutputTokens, decimal BrowserSeconds, decimal ProxyCost);
public sealed record SubmissionDetectionResult(bool Confirmed, decimal Confidence, string EvidenceType, string? EvidenceText, string? ReferenceNumber, string? FinalUrl);
public sealed record BrowserExecutionEvent(string Code, DateTime OccurredAtUtc, int? FieldCount = null);
public sealed record JobSiteCapabilities(bool SupportsAnonymousApplication, bool SupportsAuthenticatedSession, bool SupportsResumeUpload, bool SupportsMultiStepForms, bool SupportsCustomQuestions, bool SupportsSubmissionConfirmation, bool SupportsRestartFromUrl, bool RequiresAccountOften, bool UsesHumanVerificationOften);
public sealed record SiteAdapterSupportResult(JobSiteIdentifier Site, JobSiteSupportLevel SupportLevel, string Reason, bool RequiresLogin, bool RequiresHumanVerification, string AdapterName, string AdapterVersion);
public sealed record ExternalJobSiteSessionMetadata(Guid Id, Guid UserId, JobSiteIdentifier Site, ExternalJobSiteSessionStatus Status, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime ExpiresAtUtc, DateTime? LastValidatedAtUtc, bool RequiresReauthentication, long Version);
public sealed record UsableExternalJobSiteSession(ExternalJobSiteSessionMetadata Metadata, byte[] StorageState);
public interface IExternalJobSiteSessionProtector { byte[] Protect(Guid userId, JobSiteIdentifier site, ReadOnlySpan<byte> plaintext); byte[] Unprotect(Guid userId, JobSiteIdentifier site, ReadOnlySpan<byte> ciphertext); }
public interface IExternalJobSiteSessionStore
{
    Task<ExternalJobSiteSessionMetadata?> GetMetadataAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct);
    Task<ExternalJobSiteSessionMetadata?> GetMetadataAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<UsableExternalJobSiteSession?> GetActiveAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct);
    Task<ExternalJobSiteSessionMetadata> SaveAsync(Guid userId, JobSiteIdentifier site, ReadOnlyMemory<byte> storageState, DateTime expiresAtUtc, long? expectedVersion, CancellationToken ct, bool allowRevokedReplacement = false);
    Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<bool> RequireReauthenticationAsync(Guid userId, Guid sessionId, long expectedVersion, CancellationToken ct);
}
public interface IExternalJobSiteSessionService
{
    Task<ExternalJobSiteSessionResponse> GetAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct);
    Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct);
}
public sealed record ExternalSessionSiteDescriptor(JobSiteIdentifier Site, string DisplayName, Uri LoginEntryUri, IReadOnlySet<string>? AllowedAuthenticationHosts = null);
public sealed record ExternalSessionCaptureSnapshot(Uri CurrentUri, bool AuthenticatedSignal, bool LoginSignal, bool HumanVerificationSignal);
public sealed record ExternalSessionValidationResult(ExternalSessionValidationStatus Status, string ReasonCode);
public interface IExternalSessionCaptureBrowser : IAsyncDisposable
{
    Task<ExternalSessionCaptureSnapshot> InspectAsync(CancellationToken ct);
    Task<byte[]> CaptureStorageStateAsync(CancellationToken ct);
    Task<ExternalSessionBrowserFrame> CaptureFrameAsync(CancellationToken ct) => throw new NotSupportedException();
    Task ClickAsync(double x, double y, CancellationToken ct) => throw new NotSupportedException();
    Task ScrollAsync(double deltaX, double deltaY, CancellationToken ct) => throw new NotSupportedException();
    Task InsertTextAsync(string text, CancellationToken ct) => throw new NotSupportedException();
    Task PressKeyAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    Task GoBackAsync(CancellationToken ct) => throw new NotSupportedException();
    Task ResizeAsync(int width, int height, CancellationToken ct) => throw new NotSupportedException();
}
public sealed record ExternalSessionBrowserFrame(byte[] Data, string ContentType, int Width, int Height, DateTime CapturedAtUtc);
public interface IExternalSessionCaptureTransport
{
    bool IsAvailable { get; }
    string InteractionMode { get; }
    Task<IExternalSessionCaptureBrowser> StartAsync(ExternalSessionSiteDescriptor site, CancellationToken ct);
}
public interface IExternalSessionSiteRegistry { Task<ExternalSessionSiteDescriptor?> GetAsync(JobSiteIdentifier site, CancellationToken ct); }
public interface IExternalJobSiteSessionValidator { JobSiteIdentifier Site { get; } Task<ExternalSessionValidationResult> ValidateAsync(IExternalSessionCaptureBrowser browser, CancellationToken ct); }
public interface IExternalSessionCaptureService
{
    Task<ExternalSessionCaptureResponse> StartAsync(Guid userId, JobSiteIdentifier site, StartExternalSessionCaptureRequest request, CancellationToken ct);
    Task<ExternalSessionCaptureResponse> GetAsync(Guid userId, Guid captureId, CancellationToken ct);
    Task<CompleteExternalSessionCaptureResponse> CompleteAsync(Guid userId, Guid captureId, CancellationToken ct);
    Task CancelAsync(Guid userId, Guid captureId, CancellationToken ct);
}
public interface IExternalSessionCaptureCleanup { Task<int> CleanupAsync(CancellationToken ct); }
public interface IExternalSessionCaptureInteractionService
{
    Task AttachAsync(Guid userId, Guid captureId, string connectionId, CancellationToken ct);
    Task DetachAsync(Guid userId, Guid captureId, string connectionId, CancellationToken ct);
    Task DetachConnectionAsync(Guid userId, string connectionId, CancellationToken ct);
    Task<ExternalSessionBrowserFrame> RequestFrameAsync(Guid userId, Guid captureId, string connectionId, CancellationToken ct);
    Task ClickAsync(Guid userId, Guid captureId, string connectionId, double x, double y, CancellationToken ct);
    Task ScrollAsync(Guid userId, Guid captureId, string connectionId, double deltaX, double deltaY, CancellationToken ct);
    Task InsertTextAsync(Guid userId, Guid captureId, string connectionId, string text, CancellationToken ct);
    Task PressKeyAsync(Guid userId, Guid captureId, string connectionId, string key, CancellationToken ct);
    Task GoBackAsync(Guid userId, Guid captureId, string connectionId, CancellationToken ct);
    Task ResizeAsync(Guid userId, Guid captureId, string connectionId, int width, int height, CancellationToken ct);
}
public interface IJobSiteAdapterHealthService { Task<JobSiteAdapterHealth> GetHealthAsync(JobSiteIdentifier site, CancellationToken ct); Task<bool> CanExecuteAsync(JobSiteIdentifier site, DateTime now, CancellationToken ct); Task RecordOutcomeAsync(JobSiteIdentifier site, bool technicalSuccess, DateTime now, CancellationToken ct); Task OpenCircuitAsync(JobSiteIdentifier site, DateTime now, CancellationToken ct); Task CloseCircuitAsync(JobSiteIdentifier site, CancellationToken ct); }
public sealed record ExternalApplicationField(string ElementIdentifier, string FieldType, string? Label, string? Name, string? Placeholder, string? AriaLabel, string? Autocomplete, string? NearbyText, IReadOnlyList<string> Options, bool Required, string? CurrentValue);
public interface IApplicationFieldMapper { string? Map(string? label, string? name, string? placeholder, string? inputType, string? associatedText); }
public interface IAIApplyExecutionService { Task<bool> ProcessNextAsync(CancellationToken ct); }
public interface IAIApplyRetryPolicy { AIApplyRetryDecision Decide(AIApplyFailureKind kind, string code, int attempt, bool submissionAttempted, DateTime now); }
public sealed record AIApplyRetryDecision(bool ShouldRetry, DateTime? NextAttemptAtUtc, AIApplyFailureClassification Classification, string Reason, bool DeadLetter);
public interface IAIApplyWorkerIdentity { string Id { get; } }
public interface IAIApplyWorkerState { AIApplyWorkerSnapshot Snapshot(); void Started(); void Heartbeat(int activeExecutions); }
public sealed record AIApplyWorkerSnapshot(string WorkerId, DateTime StartedAtUtc, DateTime LastHeartbeatAtUtc, int ActiveExecutions, string Status);
public interface IAIApplyOperationalStore
{
    Task UpsertWorkerAsync(string workerId, DateTime startedAtUtc, DateTime heartbeatAtUtc, int processingCount, int maximumConcurrency, bool pollSucceeded, bool pollFailed, CancellationToken ct);
    Task MarkWorkerInactiveAsync(string workerId, DateTime stoppedAtUtc, CancellationToken ct);
    Task<IReadOnlyList<AIApplyWorkerOperationalSnapshot>> GetWorkersAsync(DateTime staleBeforeUtc, CancellationToken ct);
    Task<int> CleanupWorkersAsync(DateTime retentionBeforeUtc, int maximum, CancellationToken ct);
    Task<AIApplySiteOperationalSnapshot> GetSiteAsync(JobSiteIdentifier site, CancellationToken ct);
    Task<IReadOnlyList<AIApplySiteOperationalSnapshot>> GetSitesAsync(CancellationToken ct);
    Task<bool> TryAcquireSiteExecutionAsync(JobSiteIdentifier site, string workerId, DateTime nowUtc, CancellationToken ct);
    Task RecordSiteOutcomeAsync(JobSiteIdentifier site, string workerId, bool technicalSuccess, DateTime nowUtc, CancellationToken ct);
    Task SetManualCircuitAsync(JobSiteIdentifier site, bool open, DateTime nowUtc, CancellationToken ct);
}
public sealed record AIApplyWorkerOperationalSnapshot(string WorkerInstanceId, DateTime StartedAtUtc, DateTime LastHeartbeatAtUtc, int ProcessingCount, int MaximumConcurrency, string HostVersion, string Status, bool IsStale, DateTime? LastSuccessfulPollAtUtc, DateTime? LastFailureAtUtc);
public sealed record AIApplySiteOperationalSnapshot(JobSiteIdentifier Site, AIApplyCircuitState CircuitState, int SampleCount, int FailureCount, DateTime? CooldownUntilUtc, DateTime? LastSuccessAtUtc, DateTime? LastFailureAtUtc, bool? ManualOverrideOpen);
public interface IApplicationExecutionCheckpoint { Task MarkSubmissionAttemptedAsync(Guid applicationId, CancellationToken ct); Task MarkSubmissionConfirmedAsync(Guid applicationId, CancellationToken ct); }
