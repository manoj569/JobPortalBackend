using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.CareerGuidance;

public static class CareerReservationPolicy
{
    public const int LifetimeMinutes = 10;

    public static bool HasCapturedPayment(CareerGuidancePayment? payment) => payment is not null &&
        (payment.PaidAtUtc.HasValue || payment.Status is CareerPaymentStatus.Captured or CareerPaymentStatus.RefundPending or CareerPaymentStatus.Refunded);

    // The caller must persist the revision transition before advertising released capacity.
    // A provider capture is a separate financial fact; an expired booking must never reopen.
    public static bool ExpireIfDue(CareerGuidanceBooking booking, CareerGuidancePayment? payment, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC is required.", nameof(nowUtc));
        if (booking.IsDeleted || booking.Status != CareerBookingStatus.Pending || !booking.RequiresPayment ||
            booking.CreatedAtUtc > nowUtc.AddMinutes(-LifetimeMinutes) || HasCapturedPayment(payment)) return false;
        booking.Status = CareerBookingStatus.Expired;
        booking.Revision = Guid.NewGuid();
        return true;
    }
}
