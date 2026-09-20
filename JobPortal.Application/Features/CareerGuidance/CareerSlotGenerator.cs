using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.CareerGuidance;

public static class CareerSlotGenerator
{
    public static TimeZoneInfo TimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !id.Contains('/', StringComparison.Ordinal) ||
            !TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _)) throw new BadRequestException("A valid IANA timezone is required.");
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new BadRequestException("Timezone is unavailable on this server."); }
        catch (InvalidTimeZoneException) { throw new BadRequestException("Invalid timezone."); }
    }

    public static IReadOnlyCollection<CareerSlot> Generate(TimeZoneInfo zone, DateOnly from, DateOnly to, int duration,
        IReadOnlyList<CareerConsultantAvailability> windows, IReadOnlyList<CareerConsultantAvailabilityException> exceptions,
        IReadOnlyList<CareerGuidanceBooking> bookings, CareerGuidanceSchedulingOptions options, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid() || duration is < 15 or > 180 || from.Year is < 2000 or > 2100 ||
            to.Year is < 2000 or > 2100 || to < from || to.DayNumber - from.DayNumber > 30)
            throw new BadRequestException("Invalid slot range or scheduling configuration.");
        var slots = new List<CareerSlot>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var blocks = exceptions.Where(e => !e.IsDeleted && e.LocalDate == day).ToArray();
            if (blocks.Any(e => !e.StartTime.HasValue)) continue;
            foreach (var window in windows.Where(w => !w.IsDeleted && w.IsActive && w.DayOfWeek == day.DayOfWeek))
            {
                var end = day.ToDateTime(window.EndTime, DateTimeKind.Unspecified);
                for (var start = day.ToDateTime(window.StartTime, DateTimeKind.Unspecified); start.AddMinutes(duration) <= end;
                    start = start.AddMinutes(options.SlotIncrementMinutes))
                {
                    var localEnd = start.AddMinutes(duration);
                    if (blocks.Any(b => start < day.ToDateTime(b.EndTime!.Value) && localEnd > day.ToDateTime(b.StartTime!.Value))) continue;
                    // Skip any slot touching a DST gap/fold, not just ambiguous endpoints.
                    var safe = true;
                    for (var minute = start; minute <= localEnd; minute = minute.AddMinutes(1))
                        if (zone.IsInvalidTime(minute) || zone.IsAmbiguousTime(minute)) { safe = false; break; }
                    if (!safe) continue;
                    var startUtc = TimeZoneInfo.ConvertTimeToUtc(start, zone);
                    var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, zone);
                    if (endUtc - startUtc != TimeSpan.FromMinutes(duration) || startUtc <= nowUtc ||
                        startUtc < nowUtc.AddMinutes(options.MinimumBookingNoticeMinutes) || endUtc > nowUtc.AddDays(options.MaximumBookingDaysAhead)) continue;
                    if (bookings.Any(b => !b.IsDeleted && b.Status is CareerBookingStatus.Pending or CareerBookingStatus.Confirmed &&
                        b.StartUtc < endUtc && b.EndUtc > startUtc)) continue;
                    slots.Add(new(startUtc, endUtc));
                }
            }
        }
        return slots.Distinct().OrderBy(s => s.StartUtc).ToArray();
    }
}
