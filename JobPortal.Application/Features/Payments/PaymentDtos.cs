using System.Text.Json.Serialization;
using JobPortal.Application.Features.Memberships;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Payments;

public sealed record CreatePaymentOrderRequest;
public sealed record PaymentOrderResponse(
    Guid PaymentId, Guid MembershipId, string ProviderOrderId, string KeyId,
    long AmountInMinorUnits, string CurrencyCode, string Receipt, string PlanName, int DurationDays);
public sealed record ConfirmRazorpayPaymentRequest(
    string RazorpayOrderId, string RazorpayPaymentId, string RazorpaySignature);
public sealed record PaymentResponse(
    Guid Id, decimal Amount, string CurrencyCode, PaymentStatus Status,
    PaymentProvider Provider, string? ProviderOrderId, string? ProviderPaymentId,
    DateTime? PaidAtUtc, Guid? MembershipId, DateTime CreatedAtUtc,
    DateTime? ProviderOrderCreatedAtUtc = null, DateTime? LastReconciledAtUtc = null);
public sealed record PaymentHistoryResponse(
    Guid Id, Guid PaymentId, PaymentStatus? PreviousStatus, PaymentStatus CurrentStatus,
    DateTime OccurredAtUtc, string? ProviderEventId, string? Reason);
public sealed record PaymentHistoryPage(PagedResponse<PaymentHistoryResponse> Page);
public sealed record PaymentStatusResponse(
    MembershipResponse? Membership, PaymentResponse? LatestPayment);
public sealed record RazorpayWebhookRequest(
    ReadOnlyMemory<byte> RawBody, string Signature, string? EventId);
public sealed record RazorpayWebhookResponse(string Outcome);
public sealed record PhonePeCheckoutResponse(
    string MerchantOrderId, string RedirectUrl, DateTime? ExpiresAtUtc,
    string PlanName, long AmountInMinorUnits, string CurrencyCode, int DurationDays,
    string? ReturnTo = null);
public enum PhonePeBrowserPaymentStatus { Pending = 1, Completed, Failed, Cancelled }
public sealed record PhonePeReturnStatusResponse(
    string MerchantOrderId, PhonePeBrowserPaymentStatus Status, string? ReturnTo = null);
public sealed record PhonePeWebhookRequest(ReadOnlyMemory<byte> RawBody, string Authorization);
public sealed record PhonePeWebhookResponse(string Outcome);
[JsonConverter(typeof(JsonStringEnumConverter<MembershipCheckoutStatus>))]
public enum MembershipCheckoutStatus { Created = 1, Pending, Failed, Cancelled, Completed }
public sealed record PendingMembershipCheckoutResponse(
    string PublicReference,
    [property: JsonConverter(typeof(JsonStringEnumConverter<PaymentProvider>))] PaymentProvider Provider,
    MembershipCheckoutStatus Status,
    decimal Amount, string Currency, DateTime CreatedAtUtc,
    bool CanResume, bool CanCancel, string? RedirectUrl);
public sealed record PendingMembershipCheckoutRecovery(
    [property: JsonConverter(typeof(JsonStringEnumConverter<PaymentProvider>))] PaymentProvider Provider,
    string PublicReference, MembershipCheckoutStatus Status,
    DateTime CreatedAtUtc, bool CanResume, bool CanCancel);
