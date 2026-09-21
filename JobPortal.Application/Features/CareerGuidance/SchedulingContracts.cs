using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerGuidanceSchedulingOptions
{
    public const string SectionName = "CareerGuidance";
    public int SlotIncrementMinutes { get; set; } = 15;
    public int MinimumBookingNoticeMinutes { get; set; } = 120;
    public int MaximumBookingDaysAhead { get; set; } = 60;
    public int CandidateCancellationNoticeMinutes { get; set; } = 120;
    public bool IsValid() => SlotIncrementMinutes is >= 5 and <= 60 &&
        MinimumBookingNoticeMinutes is >= 0 and <= 10080 && MaximumBookingDaysAhead is >= 1 and <= 365 &&
        MinimumBookingNoticeMinutes < MaximumBookingDaysAhead * 1440 && CandidateCancellationNoticeMinutes is >= 0 and <= 10080;
}

public sealed record WeeklyWindow(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, bool IsActive = true);
public sealed record SaveAvailabilityRequest(string TimeZoneId, bool IsAcceptingBookings, WeeklyWindow[] Windows, Guid Revision);
public sealed record AvailabilityResponse(string? TimeZoneId, bool IsAcceptingBookings, IReadOnlyCollection<WeeklyWindow> Windows, Guid Revision);
public sealed record AvailabilityExceptionRequest(DateOnly LocalDate, TimeOnly? StartTime, TimeOnly? EndTime, Guid Revision);
public sealed record AvailabilityExceptionResponse(Guid Id, DateOnly LocalDate, TimeOnly? StartTime, TimeOnly? EndTime);
public sealed record ExceptionChangeResponse(AvailabilityExceptionResponse? Exception, Guid Revision);
public sealed record SlotQuery(DateOnly From, DateOnly To);
public sealed record CareerSlot(DateTime StartUtc, DateTime EndUtc);
public sealed record SlotResponse(string TimeZoneId, IReadOnlyCollection<CareerSlot> Slots);
public sealed record Questionnaire(string? TargetCompany, string? TargetRole, decimal? YearsOfExperience,
    string? CurrentRoleOrStatus, string SessionGoal, string? Questions, string? Notes);
public sealed record CreateCareerBookingRequest(Guid ConsultantId, Guid ServiceId, DateTimeOffset StartUtc, Questionnaire Questionnaire);
public sealed record BookingActionRequest(Guid Revision, string? Reason = null);
public sealed record BookingStatusRequest(Guid Revision, CareerBookingStatus Status);
public sealed record BookingQuery(int PageNumber = 1, int PageSize = 20);
public sealed record CareerBookingResponse(Guid Id, Guid ConsultantId, Guid ServiceId, DateTime StartUtc, DateTime EndUtc,
    string ConsultantTimeZone, string ServiceTitle, string ServiceType, int DurationMinutes, decimal Price, string Currency,
    CareerBookingStatus Status, Questionnaire Questionnaire, string? CancellationReason, Guid? CancelledByUserId,
    DateTime? CancelledAtUtc, DateTime? CompletedAtUtc, Guid Revision, bool RequiresPayment = false);

public interface ICareerSchedulingRepository
{
    Task<CareerConsultant?> ProfileAsync(Guid id, CancellationToken ct);
    Task<CareerConsultant?> OwnedProfileAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<CareerConsultantAvailability>> WindowsAsync(Guid consultantId, CancellationToken ct);
    Task<IReadOnlyList<CareerConsultantAvailabilityException>> ExceptionsAsync(Guid consultantId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<CareerConsultantAvailabilityException?> ExceptionAsync(Guid consultantId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<CareerGuidanceBooking>> OccupiedAsync(Guid consultantId, DateTime from, DateTime to, CancellationToken ct);
    Task<CareerGuidanceBooking?> BookingAsync(Guid id, Guid actor, bool consultant, bool admin, CancellationToken ct);
    Task<PagedResponse<CareerGuidanceBooking>> BookingsAsync(Guid actor, bool consultant, bool admin, BookingQuery query, CancellationToken ct);
    void AddWindows(IEnumerable<CareerConsultantAvailability> windows);
    void AddException(CareerConsultantAvailabilityException exception);
    void AddBooking(CareerGuidanceBooking booking);
    Task SaveAsync(CancellationToken ct);
}

public interface ICareerSchedulingService
{
    Task<AvailabilityResponse> AvailabilityAsync(Guid actor, CancellationToken ct);
    Task<AvailabilityResponse> SaveAvailabilityAsync(Guid actor, SaveAvailabilityRequest request, CancellationToken ct);
    Task<IReadOnlyCollection<AvailabilityExceptionResponse>> ExceptionsAsync(Guid actor, SlotQuery query, CancellationToken ct);
    Task<ExceptionChangeResponse> AddExceptionAsync(Guid actor, AvailabilityExceptionRequest request, CancellationToken ct);
    Task<ExceptionChangeResponse> DeleteExceptionAsync(Guid actor, Guid id, Guid revision, CancellationToken ct);
    Task<SlotResponse> SlotsAsync(Guid consultantId, Guid serviceId, SlotQuery query, CancellationToken ct);
    Task<CareerBookingResponse> CreateAsync(Guid actor, CreateCareerBookingRequest request, CancellationToken ct);
    Task<PagedResponse<CareerBookingResponse>> BookingsAsync(Guid actor, bool consultant, bool admin, BookingQuery query, CancellationToken ct);
    Task<CareerBookingResponse> GetAsync(Guid actor, Guid id, bool consultant, bool admin, CancellationToken ct);
    Task<CareerBookingResponse> CancelAsync(Guid actor, Guid id, bool consultant, BookingActionRequest request, CancellationToken ct);
    Task<CareerBookingResponse> SetStatusAsync(Guid actor, Guid id, BookingStatusRequest request, CancellationToken ct);
}
