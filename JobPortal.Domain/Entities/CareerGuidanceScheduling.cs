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

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "A calendar availability exception, not a CLR exception.")]
public sealed class CareerConsultantAvailabilityException : BaseEntity
{
    public Guid ConsultantId { get; set; }
    public CareerConsultant Consultant { get; set; } = null!;
    public DateOnly LocalDate { get; set; }
    // Both null means a full-day block; otherwise a half-open local time range.
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
}

public enum CareerBookingStatus { Pending = 1, Confirmed, CancelledByCandidate, CancelledByConsultant, Completed, NoShowCandidate, NoShowConsultant, Expired = 8 }

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
    public string? TargetCompany { get; set; }
    public string? TargetRole { get; set; }
    public decimal? YearsOfExperience { get; set; }
    public string? CurrentRoleOrStatus { get; set; }
    public string SessionGoal { get; set; } = "";
    public string? Questions { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
