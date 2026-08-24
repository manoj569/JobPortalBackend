using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.InterviewInsights;

public sealed record InterviewRoundRequest(InterviewRoundType RoundType, string? RoundTitle,
    int? DurationMinutes, string QuestionsOrTopics, string? CandidateAdvice);
public sealed record CreateInterviewInsightRequest(Guid CompanyId, Guid? JobId, string RoleTitle,
    string? ExperienceLevel, DateOnly InterviewDateMonth, InterviewDifficulty OverallDifficulty,
    string ProcessSummary, string PreparationTips, InterviewOutcome? Outcome, bool IsAnonymous,
    bool ContentGuidelinesAccepted, IReadOnlyCollection<InterviewRoundRequest> Rounds,
    InterviewFormat? InterviewFormat = null);
public sealed record UpdateInterviewInsightRequest(string RoleTitle, string? ExperienceLevel,
    DateOnly InterviewDateMonth, InterviewDifficulty OverallDifficulty, string ProcessSummary,
    string PreparationTips, InterviewOutcome? Outcome, bool IsAnonymous,
    bool ContentGuidelinesAccepted, IReadOnlyCollection<InterviewRoundRequest> Rounds,
    InterviewFormat? InterviewFormat = null);
public sealed record InterviewRoundResponse(Guid Id, int Sequence, InterviewRoundType RoundType,
    string? RoundTitle, int? DurationMinutes, string QuestionsOrTopics, string? CandidateAdvice);
public sealed record InterviewInsightResponse(Guid Id, Guid CompanyId, string CompanyName,
    string? CompanyLogoUrl, Guid? JobId, string RoleTitle, string? ExperienceLevel,
    int InterviewMonth, int InterviewYear, InterviewDifficulty OverallDifficulty,
    string ProcessSummary, string? PreparationTips, InterviewOutcome? Outcome, bool IsAnonymous,
    string? AuthorDisplayName, InterviewInsightStatus Status, DateTime? PublishedAtUtc,
    int RoundCount, int HelpfulConfirmedCount, int QualityScore, bool CanReadFull,
    bool CanGiveFeedback, IReadOnlyCollection<InterviewRoundResponse> Rounds,
    InterviewFormat? InterviewFormat = null);
public sealed record InterviewInsightQuery(Guid? CompanyId = null, string? Role = null,
    InterviewRoundType? RoundType = null, InterviewDifficulty? Difficulty = null,
    string Sort = "MostHelpful", int PageNumber = 1, int PageSize = 20,
    string? Company = null, string? ExperienceLevel = null, InterviewOutcome? Outcome = null,
    InterviewFormat? InterviewFormat = null, int? FromMonth = null, int? FromYear = null,
    int? RecencyMonths = null);

public sealed record InterviewInsightCardResponse(Guid Id, Guid CompanyId, string CompanyName,
    string RoleTitle, int InterviewMonth, int InterviewYear, InterviewDifficulty Difficulty,
    InterviewOutcome? Outcome, string? ExperienceLevel, InterviewFormat? InterviewFormat,
    int RoundCount, IReadOnlyCollection<InterviewRoundType> RoundTypes,
    IReadOnlyCollection<string> TopicTags, string ShortSummary, int HelpfulConfirmedCount,
    bool CanReadFull, bool CanGiveFeedback, bool IsAnonymous, string? AuthorLabel);
public sealed record InterviewInsightCompanyResponse(Guid Id, string CompanyName);

public sealed record CreateInterviewScheduleRequest(Guid CompanyId, Guid? JobId,
    string? RoleTitle, DateTime InterviewAtUtc, InterviewFormat? InterviewFormat = null,
    InterviewTimeOfDay? ApproximateTimeOfDay = null,
    IReadOnlyCollection<InterviewRoundType>? ExpectedRoundTypes = null,
    InterviewPreparationStatus? PreparationStatus = null, bool ReminderRequested = false);
public sealed record UpdateInterviewScheduleRequest(string? RoleTitle, DateTime InterviewAtUtc,
    InterviewScheduleStatus Status, InterviewFormat? InterviewFormat = null,
    InterviewTimeOfDay? ApproximateTimeOfDay = null,
    IReadOnlyCollection<InterviewRoundType>? ExpectedRoundTypes = null,
    InterviewPreparationStatus? PreparationStatus = null, bool ReminderRequested = false);
public sealed record InterviewScheduleResponse(Guid Id, Guid CompanyId, string CompanyName,
    Guid? JobId, string? RoleTitle, DateTime InterviewAtUtc, InterviewScheduleStatus Status,
    DateTime ConfirmFeedbackAvailableAtUtc, bool FeedbackAvailable,
    InterviewFormat? InterviewFormat = null, InterviewTimeOfDay? ApproximateTimeOfDay = null,
    IReadOnlyCollection<InterviewRoundType>? ExpectedRoundTypes = null,
    InterviewPreparationStatus? PreparationStatus = null, bool ReminderRequested = false);

public sealed record CreateInsightFeedbackRequest(Guid CandidateInterviewScheduleId,
    InsightHelpfulness Helpfulness, InterviewMatch InterviewMatch, string? Feedback);
public sealed record InsightFeedbackResponse(Guid Id, InsightHelpfulness Helpfulness,
    InterviewMatch InterviewMatch, DateTime CreatedAtUtc, int HelpfulConfirmedCount, int QualityScore);
public sealed record CreateInsightReportRequest(InsightReportReason Reason, string? Details);
public sealed record InsightReportResponse(Guid Id, InsightReportStatus Status, DateTime CreatedAtUtc);
public sealed record MyInterviewContributionsResponse(int InsightsPublished, int CandidatesHelped,
    int ContributionScore, IReadOnlyCollection<string> Badges, int PendingReview = 0,
    int NeedsChanges = 0, IReadOnlyCollection<MyInterviewContributionCardResponse>? Items = null);
public sealed record MyInterviewContributionCardResponse(Guid Id, string CompanyName, string RoleTitle,
    int InterviewMonth, int InterviewYear, InterviewInsightStatus ModerationStatus,
    int HelpfulConfirmedCount, int ContributionPoints, DateTime UpdatedAt,
    string? ReviewerChangeRequest);
public sealed record CompanyInterviewInsightSummaryResponse(Guid CompanyId, string CompanyName,
    int PublishedCount, int HelpfulConfirmedCount, bool CanReadFull);

public sealed record ModerateInterviewInsightRequest(InterviewInsightStatus Status, string? Reason);
public sealed record ModerateInsightReportRequest(InsightReportStatus Status);
public sealed record AdminInterviewInsightQuery(InterviewInsightStatus? Status = null,
    int PageNumber = 1, int PageSize = 20);
public sealed record AdminInsightReportQuery(InsightReportStatus? Status = null,
    int PageNumber = 1, int PageSize = 20);
public sealed record AdminInsightReportResponse(Guid Id, Guid InsightId, InsightReportReason Reason,
    string? Details, InsightReportStatus Status, DateTime CreatedAtUtc);
