using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class CareerConsultantAvailability : BaseEntity
{
    public Guid ConsultantId { get; set; }
    public CareerConsultant Consultant { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsActive { get; set; } = true;
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711",
    Justification = "A calendar availability exception, not a CLR exception.")]
public sealed class CareerConsultantAvailabilityException : BaseEntity
{
    public Guid ConsultantId { get; set; }
    public CareerConsultant Consultant { get; set; } = null!;
    public DateOnly LocalDate { get; set; }

    // Both null means a full-day block; otherwise a half-open local time range.
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
}

public enum CareerBookingStatus
{
    Pending = 1,
    Confirmed = 2,
    CancelledByCandidate = 3,
    CancelledByConsultant = 4,
    Completed = 5,
    NoShowCandidate = 6,
    NoShowConsultant = 7,
    Expired = 8,

    // Lifecycle v2:
    // Candidate payment has been captured, but the consultant has not yet
    // accepted or declined the booking request.
    AwaitingConsultant = 9
}

public enum CareerConsultantBookingDecision
{
    Accepted = 1,
    Declined = 2
}

public sealed class CareerGuidanceBooking : BaseEntity
{
    public bool RequiresPayment { get; set; }

    public Guid CandidateUserId { get; set; }
    public User Candidate { get; set; } = null!;

    public Guid ConsultantId { get; set; }
    public CareerConsultant Consultant { get; set; } = null!;

    public Guid ConsultantServiceId { get; set; }
    public CareerConsultantService Service { get; set; } = null!;

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public string ConsultantTimeZoneSnapshot { get; set; } = "";
    public string ServiceTitleSnapshot { get; set; } = "";
    public string ServiceTypeSnapshot { get; set; } = "";
    public int DurationMinutesSnapshot { get; set; }
    public decimal PriceSnapshot { get; set; }
    public string CurrencySnapshot { get; set; } = "";

    public CareerBookingStatus Status { get; set; } = CareerBookingStatus.Pending;

    // ---------------------------------------------------------
    // Versioned booking lifecycle foundation
    // ---------------------------------------------------------
    //
    // Existing bookings use lifecycle version 1.
    //
    // Future paid-request flow will use lifecycle version 2:
    //
    // Pending
    // -> payment captured
    // -> AwaitingConsultant
    // -> Confirmed / CancelledByConsultant
    //
    // Step 9B only adds the persistence foundation.
    // Existing booking/payment behavior is intentionally unchanged.
    public int LifecycleVersion { get; set; } = 1;

    // Immutable snapshot of the booking/cancellation/refund/acceptance
    // policy that applied when the versioned booking was created.
    //
    // Kept nullable so existing legacy bookings require no backfill.
    public string? PolicySnapshotJson { get; set; }

    // Optional client/request idempotency identity for lifecycle-v2
    // booking creation. Legacy bookings remain null.
    public Guid? RequestId { get; set; }

    // SHA-256 or equivalent canonical request payload hash.
    // Used with RequestId in the future to detect an idempotency key
    // being replayed with a different payload.
    public string? RequestPayloadHash { get; set; }

    // Deadline by which the consultant must accept/decline a paid
    // lifecycle-v2 booking request. Null for legacy bookings.
    public DateTime? AcceptanceDueAtUtc { get; set; }

    // Consultant's explicit decision for lifecycle-v2 bookings.
    // Null means no explicit decision has been recorded.
    public CareerConsultantBookingDecision? ConsultantDecision { get; set; }

    public DateTime? ConsultantDecisionAtUtc { get; set; }

    // User who performed the consultant decision.
    // This supports auditability and future delegated/admin handling.
    public Guid? ConsultantDecisionByUserId { get; set; }

    // ---------------------------------------------------------
    // Candidate questionnaire
    // ---------------------------------------------------------

    public string? TargetCompany { get; set; }
    public string? TargetRole { get; set; }
    public decimal? YearsOfExperience { get; set; }
    public string? CurrentRoleOrStatus { get; set; }
    public string SessionGoal { get; set; } = "";
    public string? Questions { get; set; }
    public string? Notes { get; set; }

    // ---------------------------------------------------------
    // Cancellation / completion
    // ---------------------------------------------------------

    public string? CancellationReason { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public Guid Revision { get; set; } = Guid.NewGuid();
}
