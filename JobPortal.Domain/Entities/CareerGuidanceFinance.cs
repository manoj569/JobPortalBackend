using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public enum CareerPaymentStatus { Created = 1, OrderCreated, Authorized, Captured, RefundPending, Refunded }
public enum CareerEarningStatus { Pending = 1, Payable, Settled, Reversed }
public enum CareerRefundStatus { Requested = 1, ProviderPending, Processed, Failed }
public enum CareerRefundReason { ConsultantCancellation = 1, ConsultantNoShow, PlatformFailure, AdminCorrection }

public sealed class CareerGuidancePayment : BaseEntity
{
    public Guid BookingId { get; set; }
    public CareerGuidanceBooking Booking { get; set; } = null!;
    public Guid CandidateUserId { get; set; }
    public Guid ConsultantId { get; set; }
    public CareerConsultant Consultant { get; set; } = null!;
    public string Provider { get; set; } = "Razorpay";
    public string? ProviderOrderId { get; set; }
    public string? ProviderPaymentId { get; set; }
    public decimal AmountGross { get; set; }
    public string Currency { get; set; } = "INR";
    public decimal PlatformCommissionPercentSnapshot { get; set; }
    public decimal PlatformCommissionAmount { get; set; }
    public decimal ConsultantNetAmount { get; set; }
    public CareerPaymentStatus Status { get; set; } = CareerPaymentStatus.Created;
    public string? FailureCode { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public bool RequiresRefundReview { get; set; }
    public string RefundPolicyVersion { get; set; } = "admin-full-v1";
    public Guid Revision { get; set; } = Guid.NewGuid();
    public CareerGuidanceEarning? Earning { get; set; }
    public CareerGuidanceRefund? Refund { get; set; }
}

public sealed class CareerGuidancePaymentEvent : BaseEntity
{
    public Guid PaymentId { get; set; }
    public CareerGuidancePayment Payment { get; set; } = null!;
    public string EventKey { get; set; } = "";
    public string EventType { get; set; } = "";
}

public sealed class CareerGuidanceEarning : BaseEntity
{
    public Guid PaymentId { get; set; }
    public CareerGuidancePayment Payment { get; set; } = null!;
    public Guid BookingId { get; set; }
    public Guid ConsultantId { get; set; }
    public CareerConsultant Consultant { get; set; } = null!;
    public decimal GrossAmount { get; set; }
    public decimal PlatformCommissionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "INR";
    public CareerEarningStatus Status { get; set; } = CareerEarningStatus.Pending;
    public DateTime? AvailableAtUtc { get; set; }
    public DateTime? SettledAtUtc { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
    public string? SettlementReference { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public sealed class CareerGuidanceRefund : BaseEntity
{
    public Guid PaymentId { get; set; }
    public CareerGuidancePayment Payment { get; set; } = null!;
    public Guid BookingId { get; set; }
    public Guid CandidateUserId { get; set; }
    public Guid ConsultantId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public CareerRefundReason ReasonCode { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public CareerRefundStatus Status { get; set; } = CareerRefundStatus.Requested;
    public string? ProviderRefundId { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
