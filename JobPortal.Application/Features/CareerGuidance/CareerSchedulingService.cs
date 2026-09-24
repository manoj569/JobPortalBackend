using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerSchedulingService(
    ICareerSchedulingRepository repository,
    IUserRepository users,
    IAuditWriter audit,
    TimeProvider clock,
    IOptions<CareerGuidanceSchedulingOptions> options,
    IValidator<SaveAvailabilityRequest> availabilityValidator,
    IValidator<AvailabilityExceptionRequest> exceptionValidator,
    IValidator<CreateCareerBookingRequest> bookingValidator) : ICareerSchedulingService
{
    private CareerGuidanceSchedulingOptions Settings =>
        options.Value.IsValid()
            ? options.Value
            : throw new InvalidOperationException(
                "Invalid Career Guidance scheduling configuration.");

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AvailabilityResponse> AvailabilityAsync(
        Guid actor,
        CancellationToken ct)
    {
        var p = await Owner(actor, ct);

        return Availability(
            p,
            await repository.WindowsAsync(p.Id, ct));
    }

    public async Task<AvailabilityResponse> SaveAvailabilityAsync(
        Guid actor,
        SaveAvailabilityRequest request,
        CancellationToken ct)
    {
        var p = await Owner(actor, ct);

        RequireAvailabilityEditor(p);
        Revision(p.Revision, request.Revision);

        await availabilityValidator.ValidateAndThrowAsync(request, ct);

        _ = CareerSlotGenerator.TimeZone(request.TimeZoneId);

        if (p.TimeZoneId != request.TimeZoneId &&
            (await repository.OccupiedAsync(
                p.Id,
                Now,
                DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
                Now,
                ct)).Count > 0)
        {
            throw new ConflictException(
                "Timezone cannot change while future bookings exist.");
        }

        var windows = await repository.WindowsAsync(p.Id, ct);

        foreach (var window in windows)
        {
            window.IsDeleted = true;
            window.DeletedAtUtc = Now;
        }

        var added = request.Windows
            .Select(w => new CareerConsultantAvailability
            {
                ConsultantId = p.Id,
                DayOfWeek = w.DayOfWeek,
                StartTime = w.StartTime,
                EndTime = w.EndTime,
                IsActive = w.IsActive
            })
            .ToArray();

        repository.AddWindows(added);

        p.TimeZoneId = request.TimeZoneId;

        // Draft, Pending and Rejected consultants may configure availability,
        // but only a Verified consultant can become publicly bookable.
        p.IsAcceptingBookings =
            p.VerificationStatus == ConsultantVerificationStatus.Verified &&
            request.IsAcceptingBookings;

        Touch(p);

        await Save(p.Id, "availability_saved", ct);

        return Availability(p, added);
    }

    public async Task<IReadOnlyCollection<AvailabilityExceptionResponse>>
        ExceptionsAsync(
            Guid actor,
            SlotQuery query,
            CancellationToken ct)
    {
        var p = await Owner(actor, ct);

        ValidateRange(query);

        return (await repository.ExceptionsAsync(
                p.Id,
                query.From,
                query.To,
                ct))
            .Select(ExceptionDto)
            .ToArray();
    }

    public async Task<ExceptionChangeResponse> AddExceptionAsync(
        Guid actor,
        AvailabilityExceptionRequest request,
        CancellationToken ct)
    {
        var p = await Owner(actor, ct);

        RequireAvailabilityEditor(p);
        Revision(p.Revision, request.Revision);

        await exceptionValidator.ValidateAndThrowAsync(request, ct);

        var zone = CareerSlotGenerator.TimeZone(p.TimeZoneId);

        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(Now, zone));

        if (request.LocalDate < today ||
            request.LocalDate >
            today.AddDays(Settings.MaximumBookingDaysAhead))
        {
            throw new BadRequestException(
                "Exception date must be within the booking horizon.");
        }

        var existing = await repository.ExceptionsAsync(
            p.Id,
            request.LocalDate,
            request.LocalDate,
            ct);

        if (existing.Count >= 16 ||
            existing.Any(e =>
                e.StartTime is null ||
                request.StartTime is null ||
                (e.StartTime < request.EndTime &&
                 e.EndTime > request.StartTime)))
        {
            throw new ConflictException(
                "Exception overlaps an existing block or exceeds the daily limit.");
        }

        var item = new CareerConsultantAvailabilityException
        {
            ConsultantId = p.Id,
            LocalDate = request.LocalDate,
            StartTime = request.StartTime,
            EndTime = request.EndTime
        };

        repository.AddException(item);

        Touch(p);

        await Save(p.Id, "exception_added", ct);

        return new(ExceptionDto(item), p.Revision);
    }

    public async Task<ExceptionChangeResponse> DeleteExceptionAsync(
        Guid actor,
        Guid id,
        Guid revision,
        CancellationToken ct)
    {
        var p = await Owner(actor, ct);

        RequireAvailabilityEditor(p);
        Revision(p.Revision, revision);

        var item = await repository.ExceptionAsync(
                       p.Id,
                       id,
                       ct)
                   ?? throw new NotFoundException(
                       "Exception not found.");

        item.IsDeleted = true;
        item.DeletedAtUtc = Now;

        Touch(p);

        await Save(p.Id, "exception_deleted", ct);

        return new(null, p.Revision);
    }

    public async Task<SlotResponse> SlotsAsync(
        Guid consultantId,
        Guid serviceId,
        SlotQuery query,
        CancellationToken ct)
    {
        ValidateRange(query);

        var p = await repository.ProfileAsync(
                    consultantId,
                    ct)
                ?? throw new NotFoundException(
                    "Consultant not found.");

        if (!Bookable(p))
        {
            return new("", Array.Empty<CareerSlot>());
        }

        var service = p.Services.SingleOrDefault(
            s => s.Id == serviceId &&
                 !s.IsDeleted &&
                 s.IsActive);

        if (service is null)
        {
            return new(
                p.TimeZoneId!,
                Array.Empty<CareerSlot>());
        }

        return await Generate(p, service, query, ct);
    }

    public async Task<CareerBookingResponse> CreateAsync(
        Guid actor,
        CreateCareerBookingRequest request,
        CancellationToken ct)
    {
        await Actor(
            actor,
            false,
            ct,
            candidate: true);

        await bookingValidator.ValidateAndThrowAsync(
            request,
            ct);

        var p = await repository.ProfileAsync(
                    request.ConsultantId,
                    ct)
                ?? throw new NotFoundException(
                    "Consultant not found.");

        if (p.UserId == actor)
        {
            throw new BadRequestException(
                "You cannot book yourself.");
        }

        if (!Bookable(p))
        {
            throw new ConflictException(
                "Consultant is not accepting bookings.");
        }

        var service = p.Services.SingleOrDefault(
                          s => s.Id == request.ServiceId &&
                               !s.IsDeleted &&
                               s.IsActive)
                      ?? throw new ConflictException(
                          "Service is not available.");

        var start = request.StartUtc.UtcDateTime;

        if (start <= Now ||
            start < Now.AddMinutes(
                Settings.MinimumBookingNoticeMinutes) ||
            start > Now.AddDays(
                Settings.MaximumBookingDaysAhead))
        {
            throw new BadRequestException(
                "Requested time is outside the booking notice or horizon.");
        }

        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                start,
                CareerSlotGenerator.TimeZone(
                    p.TimeZoneId)));

        var slots = await Generate(
            p,
            service,
            new(localDate, localDate),
            ct);

        var slot = slots.Slots.SingleOrDefault(
                       s => s.StartUtc == start)
                   ?? throw new ConflictException(
                       "This time slot is no longer available.");

        var q = request.Questionnaire;

        var booking = new CareerGuidanceBooking
        {
            RequiresPayment = true,
            CandidateUserId = actor,
            ConsultantId = p.Id,
            ConsultantServiceId = service.Id,
            StartUtc = slot.StartUtc,
            EndUtc = slot.EndUtc,
            ConsultantTimeZoneSnapshot = p.TimeZoneId!,
            ServiceTitleSnapshot = service.Title,
            ServiceTypeSnapshot = service.ServiceType,
            DurationMinutesSnapshot = service.DurationMinutes,
            PriceSnapshot = service.Price,
            CurrencySnapshot = service.Currency,
            TargetCompany = Text(q.TargetCompany),
            TargetRole = Text(q.TargetRole),
            YearsOfExperience = q.YearsOfExperience,
            CurrentRoleOrStatus =
                Text(q.CurrentRoleOrStatus),
            SessionGoal = Text(q.SessionGoal)!,
            Questions = Text(q.Questions),
            Notes = Text(q.Notes)
        };

        repository.AddBooking(booking);

        // Atomic profile CAS also detects races with moderation,
        // service edits and schedule changes.
        Touch(p);

        await Save(
            booking.Id,
            "booking_requested",
            ct);

        return BookingDto(booking);
    }

    public async Task<PagedResponse<CareerBookingResponse>>
        BookingsAsync(
            Guid actor,
            bool consultant,
            bool admin,
            BookingQuery query,
            CancellationToken ct)
    {
        await Actor(actor, admin, ct);

        if (query.PageNumber is < 1 or > 1000000 ||
            query.PageSize is < 1 or > 100)
        {
            throw new BadRequestException(
                "Invalid pagination.");
        }

        var page = await repository.BookingsAsync(
            actor,
            consultant,
            admin,
            query,
            ct);

        return new(
            page.Items.Select(BookingDto).ToArray(),
            page.PageNumber,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<CareerBookingResponse> GetAsync(
        Guid actor,
        Guid id,
        bool consultant,
        bool admin,
        CancellationToken ct) =>
        BookingDto(
            await RequiredBooking(
                actor,
                id,
                consultant,
                admin,
                ct));

    public async Task<CareerBookingResponse> CancelAsync(
        Guid actor,
        Guid id,
        bool consultant,
        BookingActionRequest request,
        CancellationToken ct)
    {
        var b = await RequiredBooking(
            actor,
            id,
            consultant,
            false,
            ct);

        Revision(b.Revision, request.Revision);

        if (request.Reason?.Length > 1000)
        {
            throw new BadRequestException(
                "Cancellation reason is too long.");
        }

        if (b.Status is not (
                CareerBookingStatus.Pending or
                CareerBookingStatus.Confirmed) ||
            b.StartUtc <= Now ||
            (!consultant &&
             b.StartUtc <
             Now.AddMinutes(
                 Settings.CandidateCancellationNoticeMinutes)))
        {
            throw new ConflictException(
                "This booking cannot be cancelled under the current cancellation policy.");
        }

        b.Status = consultant
            ? CareerBookingStatus.CancelledByConsultant
            : CareerBookingStatus.CancelledByCandidate;

        b.CancellationReason = Text(request.Reason);
        b.CancelledByUserId = actor;
        b.CancelledAtUtc = Now;
        b.Revision = Guid.NewGuid();

        await Save(
            b.Id,
            "booking_cancelled",
            ct);

        return BookingDto(b);
    }

    public async Task<CareerBookingResponse> SetStatusAsync(
        Guid actor,
        Guid id,
        BookingStatusRequest request,
        CancellationToken ct)
    {
        var b = await RequiredBooking(
            actor,
            id,
            true,
            false,
            ct);

        Revision(b.Revision, request.Revision);

        var valid =
            request.Status ==
                CareerBookingStatus.Confirmed &&
            b.Status ==
                CareerBookingStatus.Pending &&
            b.StartUtc > Now
            ||
            request.Status is
                CareerBookingStatus.Completed or
                CareerBookingStatus.NoShowCandidate or
                CareerBookingStatus.NoShowConsultant &&
            b.Status ==
                CareerBookingStatus.Confirmed &&
            b.EndUtc <= Now;

        if (!valid)
        {
            throw new ConflictException(
                "Invalid booking status transition.");
        }

        if (b.RequiresPayment &&
            request.Status !=
            CareerBookingStatus.Confirmed)
        {
            throw new ConflictException(
                "Use the paid session lifecycle to complete or mark no-show.");
        }

        if (request.Status ==
            CareerBookingStatus.Confirmed)
        {
            if (b.RequiresPayment)
            {
                throw new ConflictException(
                    "This booking requires verified payment capture before confirmation.");
            }

            var p = await repository.ProfileAsync(
                        b.ConsultantId,
                        ct)
                    ?? throw new NotFoundException(
                        "Consultant not found.");

            // Booking confirmation remains strictly Verified-only.
            RequireVerified(p);

            Touch(p);
        }

        b.Status = request.Status;
        b.Revision = Guid.NewGuid();

        if (b.Status ==
            CareerBookingStatus.Completed)
        {
            b.CompletedAtUtc = Now;
        }

        await Save(
            b.Id,
            "booking_status_changed",
            ct);

        return BookingDto(b);
    }

    private async Task<SlotResponse> Generate(
        CareerConsultant p,
        CareerConsultantService service,
        SlotQuery query,
        CancellationToken ct)
    {
        var zone = CareerSlotGenerator.TimeZone(
            p.TimeZoneId);

        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                Now,
                zone));

        if (query.From < today ||
            query.To >
            today.AddDays(
                Settings.MaximumBookingDaysAhead))
        {
            throw new BadRequestException(
                "Slot dates must be within the booking horizon.");
        }

        var windows = await repository.WindowsAsync(
            p.Id,
            ct);

        var exceptions =
            await repository.ExceptionsAsync(
                p.Id,
                query.From,
                query.To,
                ct);

        // UTC envelope safely covers all IANA offsets,
        // including DST/date-line zones.
        var bookings =
            await repository.OccupiedAsync(
                p.Id,
                query.From
                    .ToDateTime(
                        TimeOnly.MinValue,
                        DateTimeKind.Utc)
                    .AddDays(-1),
                query.To
                    .ToDateTime(
                        TimeOnly.MinValue,
                        DateTimeKind.Utc)
                    .AddDays(2),
                Now,
                ct);

        return new(
            p.TimeZoneId!,
            CareerSlotGenerator.Generate(
                zone,
                query.From,
                query.To,
                service.DurationMinutes,
                windows,
                exceptions,
                bookings,
                Settings,
                Now));
    }

    private async Task<CareerConsultant> Owner(
        Guid actor,
        CancellationToken ct)
    {
        await Actor(
            actor,
            false,
            ct);

        return await repository.OwnedProfileAsync(
                   actor,
                   ct)
               ?? throw new NotFoundException(
                   "Consultant profile not found.");
    }

    private async Task Actor(
        Guid actor,
        bool admin,
        CancellationToken ct,
        bool candidate = false)
    {
        var user =
            await users.GetByIdWithRoleAsync(
                actor,
                ct);

        if (user is null ||
            user.IsDeleted ||
            user.Status != UserStatus.Active)
        {
            throw new UnauthorizedException();
        }

        if (admin &&
            user.Role.Name != "Administrator")
        {
            throw new AppException(
                "Administrator access required.",
                403,
                "forbidden");
        }

        if (candidate &&
            user.Role.Name != "Candidate")
        {
            throw new AppException(
                "Candidate access required.",
                403,
                "forbidden");
        }
    }

    private async Task<CareerGuidanceBooking>
        RequiredBooking(
            Guid actor,
            Guid id,
            bool consultant,
            bool admin,
            CancellationToken ct)
    {
        await Actor(actor, admin, ct);

        return await repository.BookingAsync(
                   id,
                   actor,
                   consultant,
                   admin,
                   ct)
               ?? throw new NotFoundException(
                   "Booking not found.");
    }

    private static bool Bookable(
        CareerConsultant p) =>
        !p.IsDeleted &&
        p.VerificationStatus ==
            ConsultantVerificationStatus.Verified &&
        !p.User.IsDeleted &&
        p.User.Status == UserStatus.Active &&
        p.IsAcceptingBookings &&
        p.TimeZoneId is not null;

    private static void RequireAvailabilityEditor(
        CareerConsultant p)
    {
        if (p.IsDeleted ||
            p.User.IsDeleted ||
            p.User.Status != UserStatus.Active ||
            p.VerificationStatus ==
                ConsultantVerificationStatus.Suspended)
        {
            throw new AppException(
                "This consultant cannot manage availability.",
                403,
                "consultant_availability_forbidden");
        }
    }

    private static void RequireVerified(
        CareerConsultant p)
    {
        if (p.IsDeleted ||
            p.VerificationStatus !=
                ConsultantVerificationStatus.Verified ||
            p.User.IsDeleted ||
            p.User.Status != UserStatus.Active)
        {
            throw new AppException(
                "Only verified active consultants can manage bookable availability.",
                403,
                "consultant_unverified");
        }
    }

    private static void Revision(
        Guid actual,
        Guid expected)
    {
        if (expected == Guid.Empty ||
            actual != expected)
        {
            throw new ConflictException(
                "The resource changed. Reload before retrying.",
                "concurrency_conflict");
        }
    }

    private static void Touch(
        CareerConsultant p) =>
        p.Revision = Guid.NewGuid();

    private static string? Text(
        string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : text.Trim();

    private static void ValidateRange(
        SlotQuery q)
    {
        if (q.From.Year is < 2000 or > 2100 ||
            q.To.Year is < 2000 or > 2100 ||
            q.To < q.From ||
            q.To.DayNumber - q.From.DayNumber > 30)
        {
            throw new BadRequestException(
                "Request up to 31 local dates between years 2000 and 2100.");
        }
    }

    private async Task Save(
        Guid id,
        string result,
        CancellationToken ct)
    {
        await audit.AppendAsync(
            new(
                AuditAction.Update,
                "CareerGuidanceScheduling",
                id.ToString(),
                new Dictionary<string, string?>
                {
                    ["result"] = result
                }),
            ct);

        await repository.SaveAsync(ct);
    }

    private static AvailabilityResponse Availability(
        CareerConsultant p,
        IReadOnlyList<CareerConsultantAvailability> windows) =>
        new(
            p.TimeZoneId,
            p.VerificationStatus ==
                ConsultantVerificationStatus.Verified &&
            p.IsAcceptingBookings,
            windows
                .Select(w => new WeeklyWindow(
                    w.DayOfWeek,
                    w.StartTime,
                    w.EndTime,
                    w.IsActive))
                .ToArray(),
            p.Revision);

    private static AvailabilityExceptionResponse ExceptionDto(
        CareerConsultantAvailabilityException e) =>
        new(
            e.Id,
            e.LocalDate,
            e.StartTime,
            e.EndTime);

    private static CareerBookingResponse BookingDto(
        CareerGuidanceBooking b) =>
        new(
            b.Id,
            b.ConsultantId,
            b.ConsultantServiceId,
            b.StartUtc,
            b.EndUtc,
            b.ConsultantTimeZoneSnapshot,
            b.ServiceTitleSnapshot,
            b.ServiceTypeSnapshot,
            b.DurationMinutesSnapshot,
            b.PriceSnapshot,
            b.CurrencySnapshot,
            b.Status,
            new(
                b.TargetCompany,
                b.TargetRole,
                b.YearsOfExperience,
                b.CurrentRoleOrStatus,
                b.SessionGoal,
                b.Questions,
                b.Notes),
            b.CancellationReason,
            b.CancelledByUserId,
            b.CancelledAtUtc,
            b.CompletedAtUtc,
            b.Revision,
            b.RequiresPayment);
}
