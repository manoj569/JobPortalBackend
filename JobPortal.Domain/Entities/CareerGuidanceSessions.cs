using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public enum CareerSessionStatus
{
    Scheduled = 1,
    Ready = 2,
    InProgress = 3,
    Completed = 4,
    CandidateNoShow = 5,
    ConsultantNoShow = 6,
    Cancelled = 7,

    // Lifecycle-v2:
    // Consultant has requested completion, but final completion has not
    // yet been established by candidate confirmation / future policy.
    CompletionPending = 8
}

public enum CareerReminderStatus
{
    Pending = 1,
    Delivered = 2,
    Cancelled = 3
}

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

    public CareerSessionStatus Status { get; set; } =
        CareerSessionStatus.Scheduled;

    public DateTime? StartedAtUtc { get; set; }

    // ---------------------------------------------------------
    // Lifecycle-v2 completion foundation
    // ---------------------------------------------------------

    // Set when the consultant requests/marks the session as completed.
    //
    // For lifecycle-v2 this does NOT itself mean that the booking has
    // reached final completion or that the earning can be paid.
    public DateTime? CompletionRequestedAtUtc { get; set; }

    // Set when the candidate explicitly confirms completion.
    //
    // Future completion policy may also support a controlled automatic
    // completion path, but STEP 9B adds persistence only.
    public DateTime? CandidateConfirmedAtUtc { get; set; }

    // Final established completion timestamp.
    //
    // Existing lifecycle-v1 behavior continues to use this field exactly
    // as before. Later lifecycle-v2 logic will set it only after the
    // completion requirements have been satisfied.
    public DateTime? CompletedAtUtc { get; set; }

    // ---------------------------------------------------------
    // Attendance / no-show evidence
    // ---------------------------------------------------------

    public DateTime? CandidateJoinedAtUtc { get; set; }
    public DateTime? ConsultantJoinedAtUtc { get; set; }

    public DateTime? CandidateNoShowMarkedAtUtc { get; set; }
    public DateTime? ConsultantNoShowMarkedAtUtc { get; set; }
    public DateTime? ConsultantNoShowReportedAtUtc { get; set; }

    // ---------------------------------------------------------
    // Meeting provisioning
    // ---------------------------------------------------------

    public DateTime MeetingProvisioningAttemptedAtUtc { get; set; }
    public DateTime? MeetingCreatedAtUtc { get; set; }

    // Existing release-delay snapshot. STEP 9B does not change its
    // behavior. Later payout eligibility must also respect disputes and
    // the applicable lifecycle/policy rules.
    public int EarningReleaseDelayHours { get; set; }

    public Guid Revision { get; set; } = Guid.NewGuid();

    public ICollection<CareerGuidanceSessionReminder> Reminders { get; set; } =
        new List<CareerGuidanceSessionReminder>();
}

public sealed class CareerGuidanceSessionReminder : BaseEntity
{
    public Guid SessionId { get; set; }
    public CareerGuidanceSession Session { get; set; } = null!;

    public Guid RecipientUserId { get; set; }

    public int OffsetMinutes { get; set; }
    public DateTime ScheduledForUtc { get; set; }

    public CareerReminderStatus Status { get; set; } =
        CareerReminderStatus.Pending;

    // Delivered means committed to the in-app notification inbox,
    // not email/SMS delivery.
    public DateTime? SentAtUtc { get; set; }

    public Guid Revision { get; set; } = Guid.NewGuid();
}
