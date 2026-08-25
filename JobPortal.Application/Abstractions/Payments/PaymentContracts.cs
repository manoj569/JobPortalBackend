using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Payments;

public interface IPaymentService
{
    Task<PaymentOrderResponse> CreateOrderAsync(Guid userId, CreatePaymentOrderRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResponse> ConfirmAsync(Guid userId, Guid paymentId, ConfirmRazorpayPaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResponse> ReconcileAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken = default);
    Task<PaymentStatusResponse> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PhonePeCheckoutResponse> CreatePhonePeCheckoutAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PhonePeCheckoutResponse> CreatePhonePeCheckoutAsync(Guid userId, string? returnTo, CancellationToken cancellationToken = default);
    Task<PhonePeReturnStatusResponse> GetPhonePeStatusAsync(Guid userId, string merchantOrderId, CancellationToken cancellationToken = default);
    Task<PhonePeReturnStatusResponse> GetPhonePeStatusAsync(Guid userId, string merchantOrderId, string? returnTo, CancellationToken cancellationToken = default);
    Task<PhonePeWebhookResponse> ProcessPhonePeWebhookAsync(PhonePeWebhookRequest request, CancellationToken cancellationToken = default);
    Task<PendingMembershipCheckoutResponse?> GetPendingMembershipCheckoutAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PendingMembershipCheckoutResponse> CancelPendingMembershipCheckoutAsync(Guid userId, string publicReference, CancellationToken cancellationToken = default);
    Task<RazorpayWebhookResponse> ProcessWebhookAsync(RazorpayWebhookRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<PaymentResponse>> GetPaymentsAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default);
    Task<PagedResponse<PaymentHistoryResponse>> GetHistoryAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default);
}

public interface IPhonePeGateway
{
    Task<PhonePeCheckout> CreateCheckoutAsync(string merchantOrderId, long amountInMinorUnits, CancellationToken cancellationToken = default);
    Task<PhonePeCheckout> CreateCheckoutAsync(string merchantOrderId, long amountInMinorUnits, string? returnTo, CancellationToken cancellationToken = default) =>
        CreateCheckoutAsync(merchantOrderId, amountInMinorUnits, cancellationToken);
    Task<PhonePeOrderState> GetOrderStatusAsync(string merchantOrderId, CancellationToken cancellationToken = default);
    bool VerifyWebhookAuthorization(string authorization);
    PhonePeCallback ParseCallback(ReadOnlyMemory<byte> rawBody);
}

public sealed record PhonePeCheckout(string RedirectUrl, DateTime? ExpiresAtUtc = null);
public enum PhonePeOrderStateKind { Pending = 1, Completed, Failed, Cancelled }
public sealed record PhonePeOrderState(PhonePeOrderStateKind State, string MerchantOrderId,
    string? TransactionId = null, long? AmountInMinorUnits = null);
public sealed record PhonePeCallback(string MerchantOrderId, PhonePeOrderStateKind State, string EventId);

public interface IRazorpayGateway
{
    string KeyId { get; }
    Task<RazorpayOrder> CreateOrderAsync(long amountInMinorUnits, string currencyCode, string receipt, CancellationToken cancellationToken = default);
    bool VerifyPaymentSignature(string orderId, string paymentId, string signature);
    bool VerifyWebhookSignature(ReadOnlyMemory<byte> payload, string signature);
    Task<RazorpayPaymentState> GetOrderPaymentStateAsync(
        string orderId, CancellationToken cancellationToken = default);
}

public sealed record RazorpayOrder(string Id, long Amount, string Currency, string Receipt);
public enum RazorpayPaymentStateKind { Pending = 1, Paid, Failed, Cancelled, Expired }
public sealed record RazorpayPaymentState(
    RazorpayPaymentStateKind State, string? PaymentId = null,
    long? AmountInMinorUnits = null, string? CurrencyCode = null);

public interface IMembershipPlanProvider
{
    MembershipPlan GetDefaultPlan();
}

public sealed record MembershipPlan(string Name, decimal Amount, string CurrencyCode, int DurationDays);
