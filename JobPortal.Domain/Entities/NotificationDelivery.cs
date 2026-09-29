using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public enum NotificationChannel { InApp = 1, Email = 2 }
public enum NotificationDeliveryStatus { Pending = 1, Processing = 2, Sent = 3, Failed = 4, Cancelled = 5 }
public enum NotificationSource { InterviewReminder = 1, CareerConfirmation = 2, CareerReminder = 3, ReferralApproved = 4, ReferralRejected = 5, MembershipPurchase = 6 }

/// <summary>Durable delivery intent, not a second inbox. Saved with its originating business transaction.</summary>
public sealed class NotificationDelivery : BaseEntity
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public NotificationChannel Channel { get; set; }
    public NotificationSource Source { get; set; }
    public Guid SourceId { get; set; }
    public Guid SourceRevision { get; set; }
    public string BusinessKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public DateTime ScheduledForUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;
    public int AttemptCount { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureCode { get; set; }
}
