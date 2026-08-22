using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.InterviewInsights;

public sealed record InterviewRoundRequest(InterviewRoundType RoundType, string? RoundTitle,
    int? DurationMinutes, string QuestionsOrTopics, string? CandidateAdvice);
public sealed record CreateInterviewInsightRequest(Guid CompanyId, Guid? JobId, string RoleTitle,
    string? ExperienceLevel, DateOnly InterviewDateMonth, InterviewDifficulty OverallDifficulty,
    string ProcessSummary, string PreparationTips, InterviewOutcome? Outcome, bool IsAnonymous,
    bool ContentGuidelinesAccepted, IReadOnlyCollection<InterviewRoundRequest> Rounds);
public sealed record UpdateInterviewInsightRequest(string RoleTitle, string? ExperienceLevel,
    DateOnly InterviewDateMonth, InterviewDifficulty OverallDifficulty, string ProcessSummary,
    string PreparationTips, InterviewOutcome? Outcome, bool IsAnonymous,
    bool ContentGuidelinesAccepted, IReadOnlyCollection<InterviewRoundRequest> Rounds);
public sealed record InterviewRoundResponse(Guid Id, int Sequence, InterviewRoundType RoundType,
    string? RoundTitle, int? DurationMinutes, string QuestionsOrTopics, string? CandidateAdvice);
public sealed record InterviewInsightResponse(Guid Id, Guid CompanyId, string CompanyName,
    string? CompanyLogoUrl, Guid? JobId, string RoleTitle, string? ExperienceLevel,
    int InterviewMonth, int InterviewYear, InterviewDifficulty OverallDifficulty,
    string ProcessSummary, string? PreparationTips, InterviewOutcome? Outcome, bool IsAnonymous,
    string? AuthorDisplayName, InterviewInsightStatus Status, DateTime? PublishedAtUtc,
    int RoundCount, int HelpfulConfirmedCount, int QualityScore, bool CanReadFull,
    bool CanGiveFeedback, IReadOnlyCollection<InterviewRoundResponse> Rounds);
public sealed record InterviewInsightQuery(Guid? CompanyId = null, string? Role = null,
    InterviewRoundType? RoundType = null, InterviewDifficulty? Difficulty = null,
    string Sort = "mostHelpful", int PageNumber = 1, int PageSize = 20);

public sealed record CreateInterviewScheduleRequest(Guid CompanyId, Guid? JobId,
    string? RoleTitle, DateTime InterviewAtUtc);
public sealed record UpdateInterviewScheduleRequest(string? RoleTitle, DateTime InterviewAtUtc,
    InterviewScheduleStatus Status);
public sealed record InterviewScheduleResponse(Guid Id, Guid CompanyId, string CompanyName,
    Guid? JobId, string? RoleTitle, DateTime InterviewAtUtc, InterviewScheduleStatus Status,
    DateTime ConfirmFeedbackAvailableAtUtc, bool FeedbackAvailable);

public sealed record CreateInsightFeedbackRequest(Guid CandidateInterviewScheduleId,
    InsightHelpfulness Helpfulness, InterviewMatch InterviewMatch, string? Feedback);
public sealed record InsightFeedbackResponse(Guid Id, InsightHelpfulness Helpfulness,
    InterviewMatch InterviewMatch, DateTime CreatedAtUtc, int HelpfulConfirmedCount, int QualityScore);
public sealed record CreateInsightReportRequest(InsightReportReason Reason, string? Details);
public sealed record InsightReportResponse(Guid Id, InsightReportStatus Status, DateTime CreatedAtUtc);
public sealed record MyInterviewContributionsResponse(int InsightsPublished, int CandidatesHelped,
    int ContributionScore, IReadOnlyCollection<string> Badges);
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
