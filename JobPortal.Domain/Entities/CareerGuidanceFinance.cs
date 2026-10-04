using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public enum CareerPaymentStatus
{
    Created = 1,
    OrderCreated = 2,
    Authorized = 3,
    Captured = 4,
    RefundPending = 5,
    Refunded = 6
}

public enum CareerEarningStatus
{
    Pending = 1,
    Payable = 2,
    Settled = 3,
    Reversed = 4
}

public enum CareerRefundStatus
{
    Requested = 1,
    ProviderPending = 2,
    Processed = 3,
    Failed = 4
}

public enum CareerRefundReason
{
    ConsultantCancellation = 1,
    ConsultantNoShow = 2,
    PlatformFailure = 3,
    AdminCorrection = 4,

    // Lifecycle-v2 reasons. Existing numeric values above must never change.
    ConsultantDeclined = 5,
    ConsultantNoResponse = 6,
    CandidateCancellation = 7
}

public enum CareerPayoutStatus
{
    Pending = 1,
    Processing = 2,
    Paid = 3,
    Failed = 4
}

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

    // Immutable financial snapshots.
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

    // Pending:
    // Payment captured but earning is not yet eligible for payout.
    //
    // Payable:
    // Session/release/dispute requirements have been satisfied.
    //
    // Settled:
    // Actual consultant payout succeeded.
    //
    // Reversed:
    // Earning was invalidated, normally because the payment was refunded.
    public CareerEarningStatus Status { get; set; } = CareerEarningStatus.Pending;

    public DateTime? AvailableAtUtc { get; set; }
    public DateTime? SettledAtUtc { get; set; }
    public DateTime? ReversedAtUtc { get; set; }

    public string? SettlementReference { get; set; }

    public Guid Revision { get; set; } = Guid.NewGuid();

    // MVP uses one payout record per earning.
    // Database configuration will enforce one-to-one uniqueness.
    public CareerConsultantPayout? Payout { get; set; }
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

    // Versioned decision snapshot for future automatic cancellation/refund
    // rules. Null preserves all existing legacy refund records.
    public string? PolicyDecisionSnapshotJson { get; set; }

    public Guid Revision { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Represents the actual settlement/payout of one consultant earning.
///
/// This is intentionally separate from CareerGuidancePayment and
/// CareerGuidanceEarning:
///
/// Payment = candidate money collected.
/// Earning = consultant accounting entitlement.
/// Payout = actual settlement/transfer to consultant.
///
/// STEP 9B adds persistence only. It does not call a payout provider.
/// </summary>
public sealed class CareerConsultantPayout : BaseEntity
{
    public Guid EarningId { get; set; }
    public CareerGuidanceEarning Earning { get; set; } = null!;

    public Guid ConsultantId { get; set; }
    public CareerConsultant Consultant { get; set; } = null!;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";

    public CareerPayoutStatus Status { get; set; } = CareerPayoutStatus.Pending;

    // Provider-neutral for now. Later this may contain Razorpay Route /
    // another marketplace payout provider's transfer/payout identifier.
    public string? ProviderPayoutId { get; set; }

    public DateTime? ProcessingAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }

    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }

    public Guid Revision { get; set; } = Guid.NewGuid();
}
