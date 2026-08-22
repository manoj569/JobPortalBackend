namespace JobPortal.Domain.Enums;

public enum InterviewDifficulty { Easy = 1, Moderate, Hard }
public enum InterviewOutcome { Awaiting = 1, Rejected, Selected, Withdrawn, PreferNotToSay }
public enum InterviewInsightStatus { PendingReview = 1, Published, Rejected, Hidden }
public enum InterviewRoundType { HR = 1, Technical, Managerial, Coding, Aptitude, GroupDiscussion, Other }
public enum InterviewScheduleStatus { Scheduled = 1, InterviewCompleted, Cancelled }
public enum InsightHelpfulness { Helped = 1, PartlyHelped, DidNotHelp }
public enum InterviewMatch { Matched = 1, PartlyMatched, DidNotMatch }
public enum InsightReportReason { ConfidentialContent = 1, Inaccurate, Abuse, Spam, PersonalData, Other }
public enum InsightReportStatus { Open = 1, Reviewed, Actioned, Dismissed }
