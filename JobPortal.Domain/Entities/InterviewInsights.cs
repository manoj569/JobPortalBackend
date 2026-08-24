using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class InterviewInsight : BaseEntity
{
    public Guid AuthorCandidateId { get; set; }
    public User AuthorCandidate { get; set; } = null!;
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid? JobId { get; set; }
    public Job? Job { get; set; }
    public string RoleTitle { get; set; } = string.Empty;
    public string? ExperienceLevel { get; set; }
    public DateOnly InterviewDateMonth { get; set; }
    public InterviewDifficulty OverallDifficulty { get; set; }
    public InterviewFormat? InterviewFormat { get; set; }
    public string ProcessSummary { get; set; } = string.Empty;
    public string PreparationTips { get; set; } = string.Empty;
    public InterviewOutcome? Outcome { get; set; }
    public bool IsAnonymous { get; set; }
    public InterviewInsightStatus Status { get; set; } = InterviewInsightStatus.PendingReview;
    public DateTime? PublishedAtUtc { get; set; }
    public string? ModerationReason { get; set; }
    public int HelpfulConfirmedCount { get; set; }
    public int QualityScore { get; set; }
    public ICollection<InterviewRound> Rounds { get; set; } = [];
    public ICollection<InsightHelpfulnessFeedback> Feedback { get; set; } = [];
    public ICollection<InsightReport> Reports { get; set; } = [];
}

public sealed class InterviewRound : BaseEntity
{
    public Guid InterviewInsightId { get; set; }
    public InterviewInsight InterviewInsight { get; set; } = null!;
    public int Sequence { get; set; }
    public InterviewRoundType RoundType { get; set; }
    public string? RoundTitle { get; set; }
    public int? DurationMinutes { get; set; }
    public string QuestionsOrTopics { get; set; } = string.Empty;
    public string? CandidateAdvice { get; set; }
}

public sealed class CandidateInterviewSchedule : BaseEntity
{
    public Guid CandidateId { get; set; }
    public User Candidate { get; set; } = null!;
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid? JobId { get; set; }
    public Job? Job { get; set; }
    public string? RoleTitle { get; set; }
    public DateTime InterviewAtUtc { get; set; }
    public InterviewScheduleStatus Status { get; set; } = InterviewScheduleStatus.Scheduled;
    public DateTime ConfirmFeedbackAvailableAtUtc { get; set; }
    public DateTime? FeedbackNotificationSentAtUtc { get; set; }
    public InterviewFormat? InterviewFormat { get; set; }
    public InterviewTimeOfDay? ApproximateTimeOfDay { get; set; }
    public string? ExpectedRoundTypes { get; set; }
    public InterviewPreparationStatus? PreparationStatus { get; set; }
    public bool ReminderRequested { get; set; }
}

public sealed class InsightHelpfulnessFeedback : BaseEntity
{
    public Guid InsightId { get; set; }
    public InterviewInsight Insight { get; set; } = null!;
    public Guid CandidateId { get; set; }
    public User Candidate { get; set; } = null!;
    public Guid CandidateInterviewScheduleId { get; set; }
    public CandidateInterviewSchedule CandidateInterviewSchedule { get; set; } = null!;
    public InsightHelpfulness Helpfulness { get; set; }
    public InterviewMatch InterviewMatch { get; set; }
    public string? Feedback { get; set; }
}

public sealed class InsightReport : BaseEntity
{
    public Guid InsightId { get; set; }
    public InterviewInsight Insight { get; set; } = null!;
    public Guid ReporterCandidateId { get; set; }
    public User ReporterCandidate { get; set; } = null!;
    public InsightReportReason Reason { get; set; }
    public string? Details { get; set; }
    public InsightReportStatus Status { get; set; } = InsightReportStatus.Open;
}
