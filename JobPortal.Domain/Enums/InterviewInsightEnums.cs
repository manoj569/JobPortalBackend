using System.Text.Json.Serialization;

namespace JobPortal.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<InterviewDifficulty>))]
public enum InterviewDifficulty { Easy = 1, Moderate, Hard }
[JsonConverter(typeof(JsonStringEnumConverter<InterviewOutcome>))]
public enum InterviewOutcome { Awaiting = 1, Rejected, Selected, Withdrawn, PreferNotToSay }
[JsonConverter(typeof(JsonStringEnumConverter<InterviewInsightStatus>))]
public enum InterviewInsightStatus { PendingReview = 1, Published, Rejected, Hidden }
[JsonConverter(typeof(JsonStringEnumConverter<InterviewRoundType>))]
public enum InterviewRoundType { HR = 1, Technical, Managerial, Coding, Aptitude, GroupDiscussion, Other }
[JsonConverter(typeof(JsonStringEnumConverter<InterviewFormat>))]
public enum InterviewFormat { Online = 1, InPerson, Phone, Video }
[JsonConverter(typeof(JsonStringEnumConverter<InterviewTimeOfDay>))]
public enum InterviewTimeOfDay { Morning = 1, Afternoon, Evening }
[JsonConverter(typeof(JsonStringEnumConverter<InterviewPreparationStatus>))]
public enum InterviewPreparationStatus { NotStarted = 1, Preparing, Ready }
[JsonConverter(typeof(JsonStringEnumConverter<InterviewInsightSort>))]
public enum InterviewInsightSort { MostHelpful = 1, Newest, MostRounds }
[JsonConverter(typeof(JsonStringEnumConverter<InterviewScheduleStatus>))]
public enum InterviewScheduleStatus { Scheduled = 1, InterviewCompleted, Cancelled }
[JsonConverter(typeof(JsonStringEnumConverter<InsightHelpfulness>))]
public enum InsightHelpfulness { Helped = 1, PartlyHelped, DidNotHelp }
[JsonConverter(typeof(JsonStringEnumConverter<InterviewMatch>))]
public enum InterviewMatch { Matched = 1, PartlyMatched, DidNotMatch }
[JsonConverter(typeof(JsonStringEnumConverter<InsightReportReason>))]
public enum InsightReportReason { ConfidentialContent = 1, Inaccurate, Abuse, Spam, PersonalData, Other }
[JsonConverter(typeof(JsonStringEnumConverter<InsightReportStatus>))]
public enum InsightReportStatus { Open = 1, Reviewed, Actioned, Dismissed }
