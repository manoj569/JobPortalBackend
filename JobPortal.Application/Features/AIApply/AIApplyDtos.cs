using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.AIApply;

public enum AIApplyControlAction { Enable = 1, Disable, Pause, Resume }
public sealed record AIApplyProfileResponse(string FirstName, string LastName, string Email, string? Phone, string? CurrentLocation, IReadOnlyList<string> PreferredLocations, string? LinkedInUrl, string? GitHubUrl, string? PortfolioUrl, string? Degree, string? University, int? GraduationYear, decimal? TotalExperience, IReadOnlyList<string> Skills, IReadOnlyList<string> Certifications, CandidateAvailability? NoticePeriod, decimal? CurrentSalary, decimal? ExpectedSalary, bool? WillingToRelocate, string? WorkAuthorization, string? VisaSponsorshipPreference);
public sealed record UpdateAIApplyProfileRequest(string? GitHubUrl, bool? WillingToRelocate, string? WorkAuthorization, string? VisaSponsorshipPreference);
public sealed record AIApplyPreferencesResponse(IReadOnlyList<string> JobTitles, IReadOnlyList<string> Skills, decimal? MinimumExperience, decimal? MaximumExperience, IReadOnlyList<string> PreferredLocations, IReadOnlyList<WorkplaceType> WorkplaceTypes, decimal? MinimumSalary, decimal? MaximumSalary, IReadOnlyList<EmploymentType> EmploymentTypes);
public sealed record UpdateAIApplyPreferencesRequest(IReadOnlyList<string> JobTitles, IReadOnlyList<string> Skills, decimal? MinimumExperience, decimal? MaximumExperience, IReadOnlyList<string> PreferredLocations, IReadOnlyList<WorkplaceType> WorkplaceTypes, decimal? MinimumSalary, decimal? MaximumSalary, IReadOnlyList<EmploymentType> EmploymentTypes);
public sealed record AIApplySettingsResponse(bool Enabled, bool Paused, TimeOnly? PreferredStartTime, string Timezone, IReadOnlyList<DayOfWeek> PreferredDays, bool AutoResumeApplications, bool AllowAIGeneratedAnswers, bool RequireConfirmationBeforeSubmit);
public sealed record UpdateAIApplySettingsRequest(bool Enabled, TimeOnly? PreferredStartTime, string Timezone, IReadOnlyList<DayOfWeek> PreferredDays, bool AutoResumeApplications, bool AllowAIGeneratedAnswers, bool RequireConfirmationBeforeSubmit);
public sealed record UpsertAIApplyRuleRequest(AIApplyRuleType RuleType, AIApplyRuleOperator Operator, string Value, bool IsEnabled = true);
public sealed record AIApplyRuleResponse(Guid Id, AIApplyRuleType RuleType, AIApplyRuleOperator Operator, string Value, bool IsEnabled);
public sealed record QueueAIApplyRequest(Guid JobId, int Priority = 0, DateTime? ScheduledAtUtc = null);
public sealed record CandidateExternalApplicationResponse(JobSiteIdentifier Site, string DisplayName, string Url, bool CanOpen);
public sealed record AIApplyApplicationActions(bool CanOpenExternalApplication, bool CanContinue, bool CanCancel, bool RequiresCandidateAction);
public sealed record AIApplyApplicationResponse(Guid Id, Guid JobId, AIApplyRunStatus Status, int Priority, DateTime ScheduledAtUtc, DateTime? StartedAtUtc, DateTime? CompletedAtUtc, int RetryCount, AIApplyFailureKind FailureKind, bool RequiresUserInput, decimal? MatchScore, DateTime CreatedAtUtc, CandidateExternalApplicationResponse? ExternalApplication = null, AIApplyApplicationActions? Actions = null, string? Message = null);
public sealed record AIApplyQuestionResponse(Guid Id, Guid ApplicationId, string Question, string QuestionType, string? SuggestedAnswer, string? FinalAnswer, AIApplyQuestionStatus Status, DateTime? AnsweredAtUtc);
public sealed record AnswerAIApplyQuestionRequest(string Answer, bool SaveToMemory = false);
public sealed record UpdateAIApplyAnswerRequest(string Answer, bool IsActive = true);
public sealed record AIApplyAnswerResponse(Guid Id, string Question, string Answer, string? Category, AIApplyAnswerSource Source, decimal Confidence, bool IsVerified, bool IsActive);
public sealed record AIApplyAnalyticsResponse(DateTime FromUtc, DateTime ToUtc, int TotalApplications, int SubmittedApplications, int WaitingApplications, int FailedApplications, int SkippedApplications, int CompaniesReached, decimal AverageMatchScore, int ApplicationsRequiringUserInput);
public sealed record ExternalJobSiteSessionResponse(Guid Id, JobSiteIdentifier Site, ExternalJobSiteSessionStatus Status, DateTime ExpiresAtUtc, DateTime? LastValidatedAtUtc, bool RequiresReauthentication, bool CanRevoke);
public sealed record StartExternalSessionCaptureRequest(bool Reconnect = false);
public sealed record ExternalSessionCaptureResponse(Guid CaptureId, JobSiteIdentifier Site, string DisplayName, ExternalSessionCaptureStatus Status, DateTime ExpiresAtUtc, bool RequiresCandidateAction, bool InteractionAvailable, string InteractionMode, string Message);
public sealed record CompleteExternalSessionCaptureResponse(ExternalSessionCaptureResponse Capture, ExternalJobSiteSessionResponse? Session);
