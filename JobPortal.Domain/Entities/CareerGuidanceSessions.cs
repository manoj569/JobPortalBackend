using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public enum CareerSessionStatus { Scheduled = 1, Ready, InProgress, Completed, CandidateNoShow, ConsultantNoShow, Cancelled }
public enum CareerReminderStatus { Pending = 1, Delivered, Cancelled }

public sealed class CareerGuidanceSession : BaseEntity
{
    public Guid BookingId { get; set; }
    public CareerGuidanceBooking Booking { get; set; } = null!;
    public Guid CandidateUserId { get; set; }
    public Guid ConsultantId { get; set; }
    public string MeetingProvider { get; set; } = "Manual";
    public string? ProviderMeetingId { get; set; }
    public byte[]? ProtectedParticipantUrl { get; set; }
    public byte[]? ProtectedHostUrl { get; set; }
    public DateTime ScheduledStartUtc { get; set; }
    public DateTime ScheduledEndUtc { get; set; }
    public CareerSessionStatus Status { get; set; } = CareerSessionStatus.Scheduled;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CandidateJoinedAtUtc { get; set; }
    public DateTime? ConsultantJoinedAtUtc { get; set; }
    public DateTime? CandidateNoShowMarkedAtUtc { get; set; }
    public DateTime? ConsultantNoShowMarkedAtUtc { get; set; }
    public DateTime? ConsultantNoShowReportedAtUtc { get; set; }
    public DateTime MeetingProvisioningAttemptedAtUtc { get; set; }
    public DateTime? MeetingCreatedAtUtc { get; set; }
    public int EarningReleaseDelayHours { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<CareerGuidanceSessionReminder> Reminders { get; set; } = new List<CareerGuidanceSessionReminder>();
}

public sealed class CareerGuidanceSessionReminder : BaseEntity
{
    public Guid SessionId { get; set; }
    public CareerGuidanceSession Session { get; set; } = null!;
    public Guid RecipientUserId { get; set; }
    public int OffsetMinutes { get; set; }
    public DateTime ScheduledForUtc { get; set; }
    public CareerReminderStatus Status { get; set; } = CareerReminderStatus.Pending;
    // Delivered means committed to the in-app notification inbox, not email/SMS delivery.
    public DateTime? SentAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
