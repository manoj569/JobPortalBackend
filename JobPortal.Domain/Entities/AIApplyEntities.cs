using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class AIApplyProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public string? GitHubUrl { get; set; }
    public bool? WillingToRelocate { get; set; }
    public string? WorkAuthorization { get; set; }
    public string? VisaSponsorshipPreference { get; set; }
}

public sealed class AIApplyPreference : BaseEntity
{
    public Guid UserId { get; set; }
    public string JobTitlesJson { get; set; } = "[]";
    public string SkillsJson { get; set; } = "[]";
    public string PreferredLocationsJson { get; set; } = "[]";
    public string WorkplaceTypesJson { get; set; } = "[]";
    public string EmploymentTypesJson { get; set; } = "[]";
    public decimal? MinimumExperience { get; set; }
    public decimal? MaximumExperience { get; set; }
    public decimal? MinimumSalary { get; set; }
    public decimal? MaximumSalary { get; set; }
}

public sealed class AIApplySetting : BaseEntity
{
    public Guid UserId { get; set; }
    public bool Enabled { get; set; }
    public bool Paused { get; set; }
    public TimeOnly? PreferredStartTime { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string PreferredDaysJson { get; set; } = "[]";
    public bool AutoResumeApplications { get; set; } = true;
    public bool AllowAIGeneratedAnswers { get; set; }
    public bool RequireConfirmationBeforeSubmit { get; set; } = true;
}

public sealed class AIApplyRule : BaseEntity
{
    public Guid UserId { get; set; }
    public AIApplyRuleType RuleType { get; set; }
    public AIApplyRuleOperator Operator { get; set; }
    public string Value { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public sealed class AIApplyApplication : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid JobId { get; set; }
    public string ExternalApplicationUrl { get; set; } = string.Empty;
    public string NormalizedApplicationUrl { get; set; } = string.Empty;
    public AIApplyRunStatus Status { get; set; } = AIApplyRunStatus.Queued;
    public int Priority { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public AIApplyFailureKind FailureKind { get; set; }
    public string? LastErrorCode { get; set; }
    public bool RequiresUserInput { get; set; }
    public decimal? MatchScore { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? SubmissionAttemptedAtUtc { get; set; }
    public DateTime? SubmissionConfirmedAtUtc { get; set; }
    public DateTime? FirstFailureAtUtc { get; set; }
    public DateTime? LastFailureAtUtc { get; set; }
    public DateTime? DeadLetteredAtUtc { get; set; }
    public string? FailureClassification { get; set; }
}

public sealed class AIApplyQuestion : BaseEntity
{
    public Guid ApplicationId { get; set; }
    public Guid UserId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string NormalizedQuestion { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "Text";
    public string? SuggestedAnswer { get; set; }
    public string? FinalAnswer { get; set; }
    public AIApplyQuestionStatus Status { get; set; } = AIApplyQuestionStatus.Pending;
    public DateTime? AnsweredAtUtc { get; set; }
}

public sealed class UserApplicationAnswer : BaseEntity
{
    public Guid UserId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string NormalizedQuestion { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? Category { get; set; }
    public AIApplyAnswerSource Source { get; set; }
    public decimal Confidence { get; set; }
    public bool IsVerified { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AIApplyExecutionLog : BaseEntity
{
    public Guid ApplicationId { get; set; }
    public AIApplyExecutionEvent EventType { get; set; }
    public AIApplyRunStatus Status { get; set; }
    public string MessageCode { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? MetadataJson { get; set; }
}

public sealed class AIApplyCost : BaseEntity
{
    public Guid ApplicationId { get; set; }
    public Guid UserId { get; set; }
    public int AIRequestCount { get; set; }
    public long AIInputTokens { get; set; }
    public long AIOutputTokens { get; set; }
    public decimal BrowserExecutionSeconds { get; set; }
    public decimal ProxyCost { get; set; }
    public int RetryCount { get; set; }
    public decimal EstimatedCost { get; set; }
}

public sealed class ExternalJobSiteSession : BaseEntity
{
    public Guid UserId { get; set; }
    public JobSiteIdentifier Site { get; set; }
    public ExternalJobSiteSessionStatus Status { get; set; } = ExternalJobSiteSessionStatus.Active;
    public byte[] EncryptedStorageState { get; set; } = [];
    public string EncryptionPurposeVersion { get; set; } = "v1";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? LastValidatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public bool RequiresReauthentication { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class AIApplyWorkerInstance : BaseEntity
{
    public string WorkerInstanceId { get; set; } = string.Empty;
    public AIApplyWorkerStatus Status { get; set; } = AIApplyWorkerStatus.Running;
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastHeartbeatAtUtc { get; set; }
    public DateTime? LastSuccessfulPollAtUtc { get; set; }
    public DateTime? LastFailureAtUtc { get; set; }
    public DateTime? StoppedAtUtc { get; set; }
    public int ProcessingCount { get; set; }
    public int MaximumConcurrency { get; set; }
    public string HostVersion { get; set; } = string.Empty;
    public long Version { get; set; }
}

public sealed class AIApplySiteOperationalState : BaseEntity
{
    public JobSiteIdentifier Site { get; set; }
    public AIApplyCircuitState CircuitState { get; set; } = AIApplyCircuitState.Closed;
    public DateTime ObservationWindowStartedAtUtc { get; set; }
    public int SampleCount { get; set; }
    public int FailureCount { get; set; }
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? CooldownUntilUtc { get; set; }
    public string? HalfOpenProbeOwner { get; set; }
    public DateTime? HalfOpenProbeLeaseExpiresAtUtc { get; set; }
    public DateTime? LastSuccessAtUtc { get; set; }
    public DateTime? LastFailureAtUtc { get; set; }
    public bool? ManualOverrideOpen { get; set; }
    public long Version { get; set; }
}
