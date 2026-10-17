using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

// The source feedback records the triggering voter and insight for audit.
public sealed class UserPointTransaction : BaseEntity
{
    public const int InterviewInsightHelpfulPoints = 10;
    public const int InterviewInsightPublishedPoints = 5;
    public const int ReferralOpportunitySharedPoints = 5;
    public const int ReferralCandidateConfirmedPoints = 300;

    public const string InterviewInsightHelpfulReason = "InterviewInsightHelpful";
    public const string InterviewInsightPublishedReason = "InterviewInsightPublished";
    public const string ReferralOpportunitySharedReason = "ReferralOpportunityShared";
    public const string ReferralCandidateConfirmedReason = "ReferralCandidateConfirmed";

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? InsightHelpfulnessFeedbackId { get; set; }
    public InsightHelpfulnessFeedback? Feedback { get; set; }
    public Guid? InterviewInsightId { get; set; }
    public InterviewInsight? InterviewInsight { get; set; }
    public Guid? JobReferralId { get; set; }
    public JobReferral? JobReferral { get; set; }
    public Guid? ReferralRequestId { get; set; }
    public ReferralRequest? ReferralRequest { get; set; }
    public int Points { get; set; }
    public string Reason { get; set; } = InterviewInsightHelpfulReason;

    public static UserPointTransaction ForInterviewInsightPublished(InterviewInsight insight) => new()
    {
        UserId = insight.AuthorCandidateId,
        InterviewInsightId = insight.Id,
        Points = InterviewInsightPublishedPoints,
        Reason = InterviewInsightPublishedReason
    };

    public static UserPointTransaction ForInterviewInsightHelpful(Guid authorId, InsightHelpfulnessFeedback feedback) => new()
    {
        UserId = authorId,
        Feedback = feedback,
        InsightHelpfulnessFeedbackId = feedback.Id,
        Points = InterviewInsightHelpfulPoints,
        Reason = InterviewInsightHelpfulReason
    };

    public static UserPointTransaction ForReferralOpportunityShared(JobReferral opportunity) => new()
    {
        UserId = opportunity.ReferrerUserId,
        JobReferralId = opportunity.Id,
        Points = ReferralOpportunitySharedPoints,
        Reason = ReferralOpportunitySharedReason
    };

    public static UserPointTransaction ForReferralCandidateConfirmed(ReferralRequest request) => new()
    {
        UserId = request.ReferrerUserId,
        ReferralRequestId = request.Id,
        Points = ReferralCandidateConfirmedPoints,
        Reason = ReferralCandidateConfirmedReason
    };
}
