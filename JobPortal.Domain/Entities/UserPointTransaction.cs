using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

// The source feedback records the triggering voter and insight for audit.
public sealed class UserPointTransaction : BaseEntity
{
    public const int InterviewInsightHelpfulPoints = 10;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid InsightHelpfulnessFeedbackId { get; set; }
    public InsightHelpfulnessFeedback Feedback { get; set; } = null!;
    public int Points { get; set; }
    public string Reason { get; set; } = "InterviewInsightHelpful";
}
