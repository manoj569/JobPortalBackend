using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerFinanceOptions
{
    public bool PaymentsEnabled { get; set; }
    public decimal? PlatformCommissionPercent { get; set; }
    public bool IsValid() => (!PaymentsEnabled || PlatformCommissionPercent.HasValue) &&
        (PlatformCommissionPercent is null or >= 0 and <= 100) &&
        (!PlatformCommissionPercent.HasValue || decimal.Round(PlatformCommissionPercent.Value, 4) == PlatformCommissionPercent.Value);
}
public sealed record CareerCheckout(Guid PaymentId, string KeyId, string OrderId, long Amount, string Currency);
public sealed record CareerVerifyRequest(string OrderId, string PaymentId, string Signature);
public sealed record CareerRefundRequest(CareerRefundReason Reason);
public sealed record FinanceQuery(int PageNumber = 1, int PageSize = 20);
public sealed record CareerPaymentResponse(Guid Id, Guid BookingId, decimal Gross, string Currency,
    decimal CommissionPercent, decimal Commission, decimal Net, CareerPaymentStatus Status, string? ProviderOrderId,
    string? ProviderPaymentId, DateTime? PaidAtUtc, bool RequiresRefundReview, string? FailureCode);
public sealed record CareerRefundResponse(Guid Id, Guid PaymentId, Guid BookingId, decimal Amount, string Currency,
    CareerRefundReason Reason, CareerRefundStatus Status, DateTime RequestedAtUtc, DateTime? ProcessedAtUtc);
public sealed record CareerEarningResponse(Guid Id, Guid PaymentId, Guid BookingId, decimal Gross, decimal Commission,
    decimal Net, string Currency, CareerEarningStatus Status, DateTime? ReversedAtUtc);
public sealed record CareerEarningSummary(string Currency, CareerEarningStatus Status, decimal Gross, decimal Commission, decimal Net, int Count);
public sealed record GatewayOrder(string Id, long Amount, string Currency, string Receipt);
public sealed record GatewayPayment(string Id, string OrderId, long Amount, string Currency, string Status);
public sealed record GatewayRefund(string Id, string PaymentId, long Amount, string Currency, string Status, string? Receipt);

public interface ICareerPaymentGateway
{
    string KeyId { get; }
    void ValidateConfiguration();
    bool VerifyCheckout(string orderId, string paymentId, string signature);
    bool VerifyWebhook(ReadOnlyMemory<byte> body, string signature);
    Task<GatewayOrder> CreateOrderAsync(long amount, string currency, string receipt, CancellationToken ct);
    Task<GatewayOrder?> FindOrderAsync(string receipt, CancellationToken ct);
    Task<GatewayPayment> GetPaymentAsync(string paymentId, CancellationToken ct);
    Task<GatewayPayment?> CapturedPaymentAsync(string orderId, CancellationToken ct);
    Task<GatewayRefund> CreateRefundAsync(string paymentId, long amount, string receipt, CancellationToken ct);
    Task<GatewayRefund?> FindRefundAsync(string paymentId, string receipt, CancellationToken ct);
    Task<GatewayRefund> GetRefundAsync(string refundId, CancellationToken ct);
}

public interface ICareerFinanceRepository
{
    Task<CareerGuidanceBooking?> BookingAsync(Guid id, CancellationToken ct);
    Task<CareerGuidancePayment?> PaymentAsync(Guid id, CancellationToken ct);
    Task<CareerGuidancePayment?> ForBookingAsync(Guid bookingId, CancellationToken ct);
    Task<CareerGuidancePayment?> ForOrderAsync(string orderId, CancellationToken ct);
    Task<CareerGuidancePayment?> ForProviderPaymentAsync(string paymentId, CancellationToken ct);
    Task<bool> HasEventAsync(string key, CancellationToken ct);
    void Add(CareerGuidancePayment payment);
    void Add(CareerGuidancePaymentEvent item);
    Task SaveAsync(CancellationToken ct);
    Task<PagedResponse<CareerGuidancePayment>> PaymentsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct);
    Task<PagedResponse<CareerGuidanceRefund>> RefundsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct);
    Task<CareerGuidanceRefund?> RefundAsync(Guid id, CancellationToken ct);
    Task<PagedResponse<CareerGuidanceEarning>> EarningsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct);
    Task<IReadOnlyCollection<CareerEarningSummary>> SummaryAsync(Guid actor, CancellationToken ct);
}

public interface ICareerFinanceService
{
    Task<CareerCheckout> OrderAsync(Guid actor, Guid bookingId, CancellationToken ct);
    Task<CareerPaymentResponse> VerifyAsync(Guid actor, Guid bookingId, CareerVerifyRequest request, CancellationToken ct);
    Task<CareerPaymentResponse> ReconcileAsync(Guid actor, Guid bookingId, CancellationToken ct);
    Task<CareerPaymentResponse> GetAsync(Guid actor, Guid id, bool admin, CancellationToken ct);
    Task<PagedResponse<CareerPaymentResponse>> PaymentsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct);
    Task<PagedResponse<CareerRefundResponse>> RefundsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct);
    Task<CareerRefundResponse> RefundDetailAsync(Guid actor, Guid id, CancellationToken ct);
    Task<CareerRefundResponse> RefundAsync(Guid actor, Guid paymentId, CareerRefundRequest request, CancellationToken ct);
    Task<PagedResponse<CareerEarningResponse>> EarningsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct);
    Task<IReadOnlyCollection<CareerEarningSummary>> SummaryAsync(Guid actor, CancellationToken ct);
    Task<bool> TryWebhookAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken ct);
}
