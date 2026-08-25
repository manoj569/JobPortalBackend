using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Validation;
using JobPortal.Application.Features.Memberships;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Payments;

public sealed class PaymentService(
    IPaymentRepository payments,
    IMembershipRepository memberships,
    IUserRepository users,
    IRazorpayGateway razorpay,
    IPhonePeGateway phonePe,
    IMembershipPlanProvider plans,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<CreatePaymentOrderRequest> createOrderValidator,
    IValidator<ConfirmRazorpayPaymentRequest> confirmValidator,
    TimeProvider timeProvider) : IPaymentService
{
    private const int MaximumWebhookBytes = 1024 * 1024;

    public async Task<PhonePeCheckoutResponse> CreatePhonePeCheckoutAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await CreatePhonePeCheckoutAsync(userId, null, cancellationToken);

    public async Task<PhonePeCheckoutResponse> CreatePhonePeCheckoutAsync(
        Guid userId, string? returnTo, CancellationToken cancellationToken = default)
    {
        returnTo = PaymentReturnPath.Validate(returnTo);
        await RequiredCandidateAsync(userId, cancellationToken);
        var utcNow = UtcNow;
        var plan = plans.GetDefaultPlan();
        var membership = await memberships.GetPortalMembershipForUserAsync(userId, cancellationToken);
        await ExpireMembershipIfNeededAsync(membership, userId, cancellationToken);
        if (membership is { Status: MembershipStatus.Active } && membership.StartsAtUtc <= utcNow &&
            (!membership.EndsAtUtc.HasValue || membership.EndsAtUtc > utcNow))
            throw new ConflictException("An active portal membership already exists.");
        if (membership?.Status == MembershipStatus.Pending)
            await ThrowPendingCheckoutConflictAsync(userId, cancellationToken);

        var previousMembershipStatus = membership?.Status;
        if (membership is null)
        {
            membership = new Membership
            {
                UserId = userId, PlanName = plan.Name,
                Status = MembershipStatus.Pending, StartsAtUtc = utcNow
            };
            membership.History.Add(NewMembershipHistory(
                membership, null, MembershipStatus.Pending, userId, "Payment initiated."));
            await memberships.AddAsync(membership, cancellationToken);
        }
        else
        {
            membership.Status = MembershipStatus.Pending;
            membership.PlanName = plan.Name;
            membership.History.Add(NewMembershipHistory(
                membership, previousMembershipStatus, MembershipStatus.Pending, userId, "Payment re-initiated."));
        }

        var payment = new Payment
        {
            UserId = userId, Membership = membership, Amount = plan.Amount,
            CurrencyCode = plan.CurrencyCode.ToUpperInvariant(), Provider = PaymentProvider.PhonePe,
            Status = PaymentStatus.Created
        };
        var merchantOrderId = $"ch_{payment.Id:N}";
        payment.ProviderOrderId = merchantOrderId;
        payment.TransactionReference = merchantOrderId;
        payment.ProviderReceipt = merchantOrderId;
        payment.History.Add(NewPaymentHistory(payment, null, PaymentStatus.Created, userId, "Local order created."));
        await payments.AddAsync(payment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var amount = ToMinorUnits(payment.Amount);
            var checkout = await phonePe.CreateCheckoutAsync(merchantOrderId, amount, returnTo, cancellationToken);
            payment.ProviderOrderCreatedAtUtc = UtcNow;
            payment.Status = PaymentStatus.Pending;
            payment.History.Add(NewPaymentHistory(
                payment, PaymentStatus.Created, PaymentStatus.Pending, userId, "PhonePe checkout created."));
            await auditWriter.AppendAsync(new(AuditAction.Create, "Payment", payment.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["amount"] = payment.Amount.ToString(CultureInfo.InvariantCulture),
                    ["currency"] = payment.CurrencyCode,
                    ["provider"] = PaymentProvider.PhonePe.ToString(),
                    ["status"] = PaymentStatus.Pending.ToString()
                }, new(userId, "Candidate")), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new(merchantOrderId, checkout.RedirectUrl, checkout.ExpiresAtUtc,
                plan.Name, amount, payment.CurrencyCode, plan.DurationDays, returnTo);
        }
        catch
        {
            payment.Status = PaymentStatus.Failed;
            payment.History.Add(NewPaymentHistory(payment, PaymentStatus.Created, PaymentStatus.Failed,
                userId, "PhonePe checkout creation failed."));
            RestoreMembershipAfterFailedOrder(
                membership, previousMembershipStatus, userId, "PhonePe checkout creation failed.");
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<PhonePeReturnStatusResponse> GetPhonePeStatusAsync(
        Guid userId, string merchantOrderId, CancellationToken cancellationToken = default)
        => await GetPhonePeStatusAsync(userId, merchantOrderId, null, cancellationToken);

    public async Task<PhonePeReturnStatusResponse> GetPhonePeStatusAsync(
        Guid userId, string merchantOrderId, string? returnTo, CancellationToken cancellationToken = default)
    {
        returnTo = PaymentReturnPath.Validate(returnTo);
        await RequiredCandidateAsync(userId, cancellationToken);
        if (string.IsNullOrWhiteSpace(merchantOrderId) || merchantOrderId.Length > 200)
            throw new NotFoundException("Payment was not found.");
        var payment = await payments.GetOwnedByProviderOrderIdAsync(merchantOrderId, userId, cancellationToken);
        if (payment is null || payment.Provider != PaymentProvider.PhonePe)
            throw new NotFoundException("Payment was not found.");
        if (payment.Status is PaymentStatus.Created or PaymentStatus.Pending)
            await ReconcilePhonePeAsync(payment, cancellationToken);
        var status = BrowserStatus(payment.Status);
        var membership = payment.Membership;
        var canReturn = status == PhonePeBrowserPaymentStatus.Completed &&
            membership is { Status: MembershipStatus.Active } &&
            membership.StartsAtUtc <= UtcNow &&
            (!membership.EndsAtUtc.HasValue || membership.EndsAtUtc > UtcNow);
        return new(merchantOrderId, status, canReturn ? returnTo : null);
    }

    public async Task<PhonePeWebhookResponse> ProcessPhonePeWebhookAsync(
        PhonePeWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RawBody.IsEmpty || request.RawBody.Length > MaximumWebhookBytes ||
            string.IsNullOrWhiteSpace(request.Authorization) || !phonePe.VerifyWebhookAuthorization(request.Authorization))
            throw new BadRequestException("Invalid PhonePe webhook authentication.", "invalid_webhook_authentication");
        var callback = phonePe.ParseCallback(request.RawBody);
        if (await payments.HasProcessedProviderEventAsync(callback.EventId, cancellationToken))
            return new("Duplicate event acknowledged.");
        var payment = await payments.GetByProviderOrderIdAsync(callback.MerchantOrderId, cancellationToken);
        if (payment is null || payment.Provider != PaymentProvider.PhonePe)
            throw new NotFoundException("Payment order was not found.");

        var verified = await phonePe.GetOrderStatusAsync(callback.MerchantOrderId, cancellationToken);
        ValidatePhonePeState(payment, verified);
        payment.LastReconciledAtUtc = UtcNow;
        await ApplyPhonePeStateAsync(payment, verified, callback.EventId,
            new(null, "PhonePeWebhook"), "Webhook", cancellationToken);
        return new("Payment event processed.");
    }

    private async Task ReconcilePhonePeAsync(Payment payment, CancellationToken cancellationToken)
    {
        var state = await phonePe.GetOrderStatusAsync(payment.ProviderOrderId!, cancellationToken);
        ValidatePhonePeState(payment, state);
        payment.LastReconciledAtUtc = UtcNow;
        await ApplyPhonePeStateAsync(payment, state, null,
            new(payment.UserId, "Candidate"), "Reconciliation", cancellationToken);
    }

    private async Task ApplyPhonePeStateAsync(
        Payment payment, PhonePeOrderState state, string? eventId,
        AuditActor actor, string source, CancellationToken cancellationToken)
    {
        switch (state.State)
        {
            case PhonePeOrderStateKind.Completed:
                if (string.IsNullOrWhiteSpace(state.TransactionId))
                    throw new ConflictException("PhonePe verification did not include a transaction reference.");
                if (payment.Status != PaymentStatus.Paid)
                    await CompletePaymentAsync(payment, state.TransactionId,
                        "PhonePe server verification confirmed completion.", eventId,
                        AuditAction.WebhookSuccess, source, actor, cancellationToken);
                else if (eventId is not null)
                {
                    payment.History.Add(NewPaymentHistory(payment, PaymentStatus.Paid, PaymentStatus.Paid,
                        payment.UserId, "Duplicate completion acknowledged.", eventId));
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                break;
            case PhonePeOrderStateKind.Failed:
                if (payment.Status != PaymentStatus.Paid)
                    await TransitionToTerminalAsync(payment, PaymentStatus.Failed, state.TransactionId,
                        "PhonePe verification reported failure.", cancellationToken, eventId);
                break;
            case PhonePeOrderStateKind.Cancelled:
                if (payment.Status != PaymentStatus.Paid)
                    await TransitionToTerminalAsync(payment, PaymentStatus.Cancelled, state.TransactionId,
                        "PhonePe verification reported cancellation.", cancellationToken, eventId);
                break;
            default:
                if (eventId is not null)
                    payment.History.Add(NewPaymentHistory(payment, payment.Status, payment.Status,
                        payment.UserId, "Pending PhonePe event acknowledged.", eventId));
                await unitOfWork.SaveChangesAsync(cancellationToken);
                break;
        }
    }

    private static void ValidatePhonePeState(Payment payment, PhonePeOrderState state)
    {
        if (!string.Equals(payment.ProviderOrderId, state.MerchantOrderId, StringComparison.Ordinal) ||
            state.AmountInMinorUnits != ToMinorUnits(payment.Amount))
            throw new ConflictException("PhonePe payment details do not match the local payment.");
    }

    private static PhonePeBrowserPaymentStatus BrowserStatus(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => PhonePeBrowserPaymentStatus.Completed,
        PaymentStatus.Failed or PaymentStatus.Expired => PhonePeBrowserPaymentStatus.Failed,
        PaymentStatus.Cancelled => PhonePeBrowserPaymentStatus.Cancelled,
        _ => PhonePeBrowserPaymentStatus.Pending
    };

    public async Task<PendingMembershipCheckoutResponse?> GetPendingMembershipCheckoutAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var payment = await payments.GetLatestUnresolvedMembershipAsync(userId, cancellationToken);
        return payment is null ? null : ToPendingCheckoutResponse(payment);
    }

    public async Task<PendingMembershipCheckoutResponse> CancelPendingMembershipCheckoutAsync(
        Guid userId, string publicReference, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        if (string.IsNullOrWhiteSpace(publicReference) || publicReference.Length > 200)
            throw new NotFoundException("Payment was not found.");
        var payment = await payments.GetOwnedByProviderOrderIdAsync(
            publicReference, userId, cancellationToken);
        if (payment?.MembershipId is null ||
            payment.Provider is not (PaymentProvider.Razorpay or PaymentProvider.PhonePe))
            throw new NotFoundException("Payment was not found.");

        if (payment.Status == PaymentStatus.Paid)
            throw new ConflictException(
                "A completed membership payment cannot be cancelled.",
                "completed_payment_cannot_be_cancelled");
        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Expired)
            return ToPendingCheckoutResponse(payment);
        if (payment.Status is not (PaymentStatus.Created or PaymentStatus.Pending or PaymentStatus.Authorized))
            throw new ConflictException("Payment is not in a cancellable state.");

        if (payment.Provider == PaymentProvider.PhonePe)
        {
            var state = await phonePe.GetOrderStatusAsync(publicReference, cancellationToken);
            ValidatePhonePeState(payment, state);
            payment.LastReconciledAtUtc = UtcNow;
            await ApplyPhonePeStateAsync(payment, state, null,
                new(userId, "Candidate"), "CancellationReconciliation", cancellationToken);
        }
        else
        {
            var state = await razorpay.GetOrderPaymentStateAsync(publicReference, cancellationToken);
            payment.LastReconciledAtUtc = UtcNow;
            await ApplyRazorpayCancellationStateAsync(payment, state, userId, cancellationToken);
        }

        if (payment.Status is PaymentStatus.Created or PaymentStatus.Pending or PaymentStatus.Authorized)
        {
            await auditWriter.AppendAsync(new(
                AuditAction.Update, "Payment", payment.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["provider"] = payment.Provider.ToString(),
                    ["status"] = PaymentStatus.Cancelled.ToString(),
                    ["source"] = "CandidateCheckoutCancellation"
                }, new(userId, "Candidate")), cancellationToken);
            await TransitionToTerminalAsync(payment, PaymentStatus.Cancelled, null,
                "Candidate abandoned the local checkout; provider payment was not reversed.",
                cancellationToken);
        }

        return ToPendingCheckoutResponse(payment);
    }

    private async Task ApplyRazorpayCancellationStateAsync(
        Payment payment, RazorpayPaymentState state, Guid userId, CancellationToken cancellationToken)
    {
        switch (state.State)
        {
            case RazorpayPaymentStateKind.Paid:
                ValidateProviderPaymentState(payment, state);
                await CompletePaymentAsync(payment, state.PaymentId!,
                    "Razorpay cancellation check confirmed capture.", null,
                    AuditAction.Confirm, "CancellationReconciliation",
                    new(userId, "Candidate"), cancellationToken);
                break;
            case RazorpayPaymentStateKind.Failed:
                ValidateProviderPaymentState(payment, state);
                await TransitionToTerminalAsync(payment, PaymentStatus.Failed, state.PaymentId,
                    "Razorpay cancellation check reported failure.", cancellationToken);
                break;
            case RazorpayPaymentStateKind.Cancelled:
                await TransitionToTerminalAsync(payment, PaymentStatus.Cancelled, state.PaymentId,
                    "Razorpay cancellation check reported cancellation.", cancellationToken);
                break;
            case RazorpayPaymentStateKind.Expired:
                await TransitionToTerminalAsync(payment, PaymentStatus.Expired, state.PaymentId,
                    "Razorpay cancellation check reported expiration.", cancellationToken);
                break;
            default:
                await unitOfWork.SaveChangesAsync(cancellationToken);
                break;
        }
    }

    public async Task<PaymentOrderResponse> CreateOrderAsync(
        Guid userId, CreatePaymentOrderRequest request, CancellationToken cancellationToken = default)
    {
        await createOrderValidator.ValidateAndThrowAsync(request, cancellationToken);
        await RequiredCandidateAsync(userId, cancellationToken);
        var utcNow = UtcNow;
        var plan = plans.GetDefaultPlan();
        var membership = await memberships.GetPortalMembershipForUserAsync(userId, cancellationToken);
        await ExpireMembershipIfNeededAsync(membership, userId, cancellationToken);
        if (membership is { Status: MembershipStatus.Active } &&
            membership.StartsAtUtc <= utcNow &&
            (!membership.EndsAtUtc.HasValue || membership.EndsAtUtc > utcNow))
            throw new ConflictException("An active portal membership already exists.");
        if (membership?.Status == MembershipStatus.Pending)
            await ThrowPendingCheckoutConflictAsync(userId, cancellationToken);

        var previousMembershipStatus = membership?.Status;
        if (membership is null)
        {
            membership = new Membership
            {
                UserId = userId,
                PlanName = plan.Name,
                Status = MembershipStatus.Pending,
                StartsAtUtc = utcNow
            };
            membership.History.Add(NewMembershipHistory(
                membership, null, MembershipStatus.Pending, userId, "Payment initiated."));
            await memberships.AddAsync(membership, cancellationToken);
        }
        else
        {
            membership.Status = MembershipStatus.Pending;
            membership.PlanName = plan.Name;
            membership.History.Add(NewMembershipHistory(
                membership, previousMembershipStatus, MembershipStatus.Pending,
                userId, "Payment re-initiated."));
        }

        var payment = new Payment
        {
            UserId = userId,
            Membership = membership,
            Amount = plan.Amount,
            CurrencyCode = plan.CurrencyCode.ToUpperInvariant(),
            Provider = PaymentProvider.Razorpay,
            Status = PaymentStatus.Created
        };
        payment.ProviderReceipt = $"m_{payment.Id:N}";
        payment.History.Add(NewPaymentHistory(
            payment, null, PaymentStatus.Created, userId, "Local order created."));
        await payments.AddAsync(payment, cancellationToken);

        // The local payment and membership intent exist before any provider request is made.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var requestedAmount = ToMinorUnits(payment.Amount);
            var order = await razorpay.CreateOrderAsync(
                requestedAmount, payment.CurrencyCode, payment.ProviderReceipt, cancellationToken);
            if (string.IsNullOrWhiteSpace(order.Id) || order.Id.Length > 200 ||
                order.Amount != requestedAmount ||
                !string.Equals(order.Currency, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(order.Receipt, payment.ProviderReceipt, StringComparison.Ordinal))
                throw new ConflictException("Razorpay returned order details that do not match the local payment.");

            payment.ProviderOrderId = order.Id;
            payment.TransactionReference = order.Id;
            payment.ProviderOrderCreatedAtUtc = UtcNow;
            payment.Status = PaymentStatus.Pending;
            payment.History.Add(NewPaymentHistory(
                payment, PaymentStatus.Created, PaymentStatus.Pending, userId, "Provider order created."));
            await auditWriter.AppendAsync(new(
                AuditAction.Create,
                "Payment",
                payment.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["amount"] = payment.Amount.ToString(CultureInfo.InvariantCulture),
                    ["currency"] = payment.CurrencyCode,
                    ["provider"] = PaymentProvider.Razorpay.ToString(),
                    ["status"] = PaymentStatus.Pending.ToString()
                },
                new(userId, "Candidate")), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new PaymentOrderResponse(
                payment.Id, membership.Id, order.Id, razorpay.KeyId, order.Amount,
                order.Currency, order.Receipt, plan.Name, plan.DurationDays);
        }
        catch
        {
            payment.Status = PaymentStatus.Failed;
            payment.History.Add(NewPaymentHistory(
                payment, PaymentStatus.Created, PaymentStatus.Failed,
                userId, "Provider order creation failed."));
            RestoreMembershipAfterFailedOrder(
                membership, previousMembershipStatus, userId, "Provider order creation failed.");
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<PaymentResponse> ConfirmAsync(
        Guid userId, Guid paymentId, ConfirmRazorpayPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        await confirmValidator.ValidateAndThrowAsync(request, cancellationToken);
        await RequiredCandidateAsync(userId, cancellationToken);
        var payment = await payments.GetOwnedAsync(paymentId, userId, cancellationToken)
            ?? throw new NotFoundException("Payment was not found.");
        EnsureProviderOrderMatches(payment, request.RazorpayOrderId);
        if (payment.Status == PaymentStatus.Paid)
        {
            if (!string.Equals(payment.ProviderPaymentId, request.RazorpayPaymentId, StringComparison.Ordinal))
                throw new ConflictException("Payment confirmation does not match the stored payment.");
            return ToResponse(payment);
        }
        if (payment.Status != PaymentStatus.Pending)
            throw new ConflictException("Payment is not in a confirmable state.");
        if (!razorpay.VerifyPaymentSignature(
                payment.ProviderOrderId!, request.RazorpayPaymentId, request.RazorpaySignature))
            throw new BadRequestException(
                "Payment signature verification failed.", "invalid_payment_signature");

        var providerState = await razorpay.GetOrderPaymentStateAsync(
            payment.ProviderOrderId!, cancellationToken);
        payment.LastReconciledAtUtc = UtcNow;
        if (providerState.State != RazorpayPaymentStateKind.Paid)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new ConflictException(
                "Razorpay has not confirmed that the payment is captured.");
        }
        ValidateProviderPaymentState(payment, providerState, request.RazorpayPaymentId);
        await CompletePaymentAsync(
            payment,
            request.RazorpayPaymentId,
            "Checkout signature verified.",
            null,
            AuditAction.Confirm,
            "Checkout",
            new(userId, "Candidate"),
            cancellationToken);
        return ToResponse(payment);
    }

    public async Task<PaymentResponse> ReconcileAsync(
        Guid userId, Guid paymentId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var payment = await payments.GetOwnedAsync(paymentId, userId, cancellationToken)
            ?? throw new NotFoundException("Payment was not found.");
        if (payment.Status is PaymentStatus.Paid or PaymentStatus.Failed or
            PaymentStatus.Cancelled or PaymentStatus.Expired)
            return ToResponse(payment);
        if (payment.ProviderOrderId is null)
        {
            await TransitionToTerminalAsync(
                payment, PaymentStatus.Failed, null,
                "Local order has no Razorpay order and cannot be reconciled.", cancellationToken);
            return ToResponse(payment);
        }

        var state = await razorpay.GetOrderPaymentStateAsync(
            payment.ProviderOrderId, cancellationToken);
        payment.LastReconciledAtUtc = UtcNow;
        switch (state.State)
        {
            case RazorpayPaymentStateKind.Paid:
                ValidateProviderPaymentState(payment, state);
                await CompletePaymentAsync(
                    payment,
                    state.PaymentId!,
                    "Razorpay reconciliation confirmed capture.",
                    null,
                    AuditAction.Confirm,
                    "Reconciliation",
                    new(userId, "Candidate"),
                    cancellationToken);
                break;
            case RazorpayPaymentStateKind.Failed:
                ValidateProviderPaymentState(payment, state);
                await TransitionToTerminalAsync(
                    payment, PaymentStatus.Failed, state.PaymentId,
                    "Razorpay reconciliation reported a failed payment.", cancellationToken);
                break;
            case RazorpayPaymentStateKind.Cancelled:
                await TransitionToTerminalAsync(
                    payment, PaymentStatus.Cancelled, state.PaymentId,
                    "Razorpay reconciliation reported cancellation.", cancellationToken);
                break;
            case RazorpayPaymentStateKind.Expired:
                await TransitionToTerminalAsync(
                    payment, PaymentStatus.Expired, state.PaymentId,
                    "Razorpay reconciliation reported expiration.", cancellationToken);
                break;
            default:
                await unitOfWork.SaveChangesAsync(cancellationToken);
                break;
        }
        return ToResponse(payment);
    }

    public async Task<PaymentStatusResponse> GetStatusAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var membership = await memberships.GetPortalMembershipForUserAsync(userId, cancellationToken);
        await ExpireMembershipIfNeededAsync(membership, userId, cancellationToken);
        var latestPayment = await payments.GetLatestForUserAsync(userId, cancellationToken);
        return new(
            membership is null ? null : new MembershipResponse(
                membership.Id, membership.PlanName, membership.Status,
                membership.StartsAtUtc, membership.EndsAtUtc, membership.AutoRenew),
            latestPayment is null ? null : ToResponse(latestPayment));
    }

    public async Task<RazorpayWebhookResponse> ProcessWebhookAsync(
        RazorpayWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RawBody.IsEmpty || request.RawBody.Length > MaximumWebhookBytes ||
            string.IsNullOrWhiteSpace(request.Signature) || request.Signature.Length != 64)
            throw new BadRequestException("Invalid Razorpay webhook.", "invalid_webhook");
        if (!razorpay.VerifyWebhookSignature(request.RawBody, request.Signature))
            throw new BadRequestException(
                "Razorpay webhook signature verification failed.", "invalid_webhook_signature");

        var providerEventId = GetProviderEventId(request);
        if (await payments.HasProcessedProviderEventAsync(providerEventId, cancellationToken))
            return new("Duplicate event acknowledged.");

        var webhook = ParseWebhook(request.RawBody);
        if (webhook is null)
            return new("Unsupported event ignored.");
        var payment = await payments.GetByProviderOrderIdAsync(
            webhook.OrderId, cancellationToken);
        if (payment is null)
            return new("Unknown order ignored.");

        ValidateWebhookPayment(payment, webhook);
        if (webhook.IsSuccessful)
        {
            if (payment.Status == PaymentStatus.Paid)
            {
                payment.History.Add(NewPaymentHistory(
                    payment, PaymentStatus.Paid, PaymentStatus.Paid, payment.UserId,
                    "Successful webhook acknowledged.", providerEventId));
                await AppendWebhookAuditAsync(
                    payment,
                    AuditAction.WebhookSuccess,
                    "Acknowledged",
                    null,
                    cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await CompletePaymentAsync(
                    payment,
                    webhook.PaymentId,
                    "Razorpay webhook confirmed capture.",
                    providerEventId,
                    AuditAction.WebhookSuccess,
                    "Webhook",
                    new(null, "RazorpayWebhook"),
                    cancellationToken);
            }
            return new("Payment event processed.");
        }

        if (payment.Status == PaymentStatus.Paid)
        {
            payment.History.Add(NewPaymentHistory(
                payment, PaymentStatus.Paid, PaymentStatus.Paid, payment.UserId,
                "Late failure event ignored for paid payment.", providerEventId));
            await AppendWebhookAuditAsync(
                payment,
                AuditAction.WebhookFailure,
                "IgnoredPaidPayment",
                null,
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await AppendWebhookAuditAsync(
                payment,
                AuditAction.WebhookFailure,
                "Failed",
                PaymentStatus.Failed,
                cancellationToken);
            await TransitionToTerminalAsync(
                payment, PaymentStatus.Failed, webhook.PaymentId,
                "Razorpay webhook reported payment failure.", cancellationToken, providerEventId);
        }
        return new("Payment event processed.");
    }

    public async Task<PagedResponse<PaymentResponse>> GetPaymentsAsync(
        Guid userId, HistoryQuery query, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await payments.GetForUserAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task<PagedResponse<PaymentHistoryResponse>> GetHistoryAsync(
        Guid userId, HistoryQuery query, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await payments.GetHistoryAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    private async Task CompletePaymentAsync(
        Payment payment,
        string providerPaymentId,
        string reason,
        string? providerEventId,
        AuditAction paymentAuditAction,
        string auditSource,
        AuditActor auditActor,
        CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.Paid) return;
        var previousPaymentStatus = payment.Status;
        var utcNow = UtcNow;
        payment.Status = PaymentStatus.Paid;
        payment.ProviderPaymentId = providerPaymentId;
        payment.PaidAtUtc = utcNow;
        payment.History.Add(NewPaymentHistory(
            payment, previousPaymentStatus, PaymentStatus.Paid,
            payment.UserId, reason, providerEventId));

        var membership = payment.Membership
            ?? throw new ConflictException("Payment has no membership.");
        var previousMembershipStatus = membership.Status;
        var extensionStart = membership.Status == MembershipStatus.Active &&
            membership.EndsAtUtc.HasValue && membership.EndsAtUtc > utcNow
                ? membership.EndsAtUtc.Value
                : utcNow;
        if (membership.Status != MembershipStatus.Active)
            membership.StartsAtUtc = utcNow;
        membership.Status = MembershipStatus.Active;
        membership.EndsAtUtc = extensionStart.AddDays(plans.GetDefaultPlan().DurationDays);
        membership.History.Add(NewMembershipHistory(
            membership, previousMembershipStatus, MembershipStatus.Active,
            payment.UserId, $"Verified {payment.Provider} payment completed."));
        await auditWriter.AppendAsync(new(
            paymentAuditAction,
            "Payment",
            payment.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["provider"] = payment.Provider.ToString(),
                ["source"] = auditSource,
                ["status"] = PaymentStatus.Paid.ToString()
            },
            auditActor), cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Activate,
            "Membership",
            membership.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["membershipStatus"] = MembershipStatus.Active.ToString(),
                ["source"] = auditSource
            },
            auditActor), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Task AppendWebhookAuditAsync(
        Payment payment,
        AuditAction action,
        string result,
        PaymentStatus? resultingStatus,
        CancellationToken cancellationToken) =>
        auditWriter.AppendAsync(new(
            action,
            "Payment",
            payment.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["provider"] = PaymentProvider.Razorpay.ToString(),
                ["result"] = result,
                ["status"] = (resultingStatus ?? payment.Status).ToString()
            },
            new(null, "RazorpayWebhook")), cancellationToken);

    private async Task TransitionToTerminalAsync(
        Payment payment, PaymentStatus status, string? providerPaymentId, string reason,
        CancellationToken cancellationToken, string? providerEventId = null)
    {
        var previous = payment.Status;
        payment.Status = status;
        if (!string.IsNullOrWhiteSpace(providerPaymentId))
            payment.ProviderPaymentId = providerPaymentId;
        payment.History.Add(NewPaymentHistory(
            payment, previous, status, payment.UserId, reason, providerEventId));
        if (payment.Membership is { Status: MembershipStatus.Pending } membership)
        {
            var membershipStatus = status == PaymentStatus.Expired
                ? MembershipStatus.Expired
                : MembershipStatus.Cancelled;
            membership.Status = membershipStatus;
            membership.History.Add(NewMembershipHistory(
                membership, MembershipStatus.Pending, membershipStatus,
                payment.UserId, "Payment did not complete."));
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ExpireMembershipIfNeededAsync(
        Membership? membership, Guid userId, CancellationToken cancellationToken)
    {
        if (membership is not { Status: MembershipStatus.Active, EndsAtUtc: not null } ||
            membership.EndsAtUtc > UtcNow)
            return;
        membership.Status = MembershipStatus.Expired;
        membership.History.Add(NewMembershipHistory(
            membership, MembershipStatus.Active, MembershipStatus.Expired,
            userId, "Membership term expired."));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ThrowPendingCheckoutConflictAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var payment = await payments.GetLatestUnresolvedMembershipAsync(userId, cancellationToken);
        if (payment is null)
            throw new ConflictException("A portal membership payment order is already pending.");
        var response = ToPendingCheckoutResponse(payment);
        throw new PendingMembershipCheckoutException(new(
            response.Provider, response.PublicReference, response.Status,
            response.CreatedAtUtc, response.CanResume, response.CanCancel));
    }

    private static PendingMembershipCheckoutResponse ToPendingCheckoutResponse(Payment payment)
    {
        var status = payment.Status switch
        {
            PaymentStatus.Created => MembershipCheckoutStatus.Created,
            PaymentStatus.Pending or PaymentStatus.Authorized => MembershipCheckoutStatus.Pending,
            PaymentStatus.Paid or PaymentStatus.Refunded => MembershipCheckoutStatus.Completed,
            PaymentStatus.Cancelled => MembershipCheckoutStatus.Cancelled,
            _ => MembershipCheckoutStatus.Failed
        };
        var canCancel = status is MembershipCheckoutStatus.Created or MembershipCheckoutStatus.Pending;
        return new PendingMembershipCheckoutResponse(
            payment.ProviderOrderId!, payment.Provider, status,
            payment.Amount, payment.CurrencyCode, payment.CreatedAtUtc,
            false, canCancel, null);
    }

    private void RestoreMembershipAfterFailedOrder(
        Membership membership, MembershipStatus? previousStatus, Guid userId, string reason)
    {
        var restoredStatus = previousStatus ?? MembershipStatus.Cancelled;
        membership.Status = restoredStatus;
        membership.History.Add(NewMembershipHistory(
            membership, MembershipStatus.Pending, restoredStatus, userId, reason));
    }

    private async Task RequiredCandidateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdWithRoleAsync(userId, cancellationToken);
        if (user is null || user.RoleId != SystemRoleIds.Candidate ||
            user.Status != UserStatus.Active)
            throw new UnauthorizedException("An active Candidate account is required.");
    }

    private static void EnsureProviderOrderMatches(Payment payment, string orderId)
    {
        if (payment.Provider != PaymentProvider.Razorpay ||
            !string.Equals(payment.ProviderOrderId, orderId, StringComparison.Ordinal))
            throw new ConflictException("Payment confirmation does not match the stored order.");
    }

    private static void ValidateProviderPaymentState(
        Payment payment, RazorpayPaymentState state, string? expectedPaymentId = null)
    {
        if (string.IsNullOrWhiteSpace(state.PaymentId) ||
            (expectedPaymentId is not null &&
                !string.Equals(state.PaymentId, expectedPaymentId, StringComparison.Ordinal)) ||
            state.AmountInMinorUnits != ToMinorUnits(payment.Amount) ||
            !string.Equals(state.CurrencyCode, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Razorpay payment details do not match the local payment.");
    }

    private static void ValidateWebhookPayment(Payment payment, WebhookPayment webhook)
    {
        if (webhook.AmountInMinorUnits != ToMinorUnits(payment.Amount) ||
            !string.Equals(webhook.CurrencyCode, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Razorpay webhook payment details do not match the local payment.");
    }

    private static WebhookPayment? ParseWebhook(ReadOnlyMemory<byte> rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var eventName = root.GetProperty("event").GetString();
            var isSuccessful = eventName is "payment.captured" or "order.paid";
            if (!isSuccessful && eventName is not "payment.failed") return null;
            var entity = root.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var orderId = entity.GetProperty("order_id").GetString();
            var paymentId = entity.GetProperty("id").GetString();
            var currency = entity.GetProperty("currency").GetString();
            var status = entity.GetProperty("status").GetString();
            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(paymentId) ||
                string.IsNullOrWhiteSpace(currency) || orderId.Length > 200 ||
                paymentId.Length > 200 || currency.Length > 3 ||
                (isSuccessful && status is not "captured") ||
                (!isSuccessful && status is not "failed"))
                throw new JsonException("Required payment fields are missing.");
            return new(
                orderId, paymentId, entity.GetProperty("amount").GetInt64(),
                currency, isSuccessful);
        }
        catch (JsonException)
        {
            throw new BadRequestException("Invalid Razorpay webhook payload.", "invalid_webhook");
        }
    }

    private static string GetProviderEventId(RazorpayWebhookRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.EventId) && request.EventId.Length <= 200)
            return request.EventId;
        return $"body_{Convert.ToHexString(SHA256.HashData(request.RawBody.Span)).ToLowerInvariant()}";
    }

    private static long ToMinorUnits(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    private static PaymentResponse ToResponse(Payment x) => new(
        x.Id, x.Amount, x.CurrencyCode, x.Status, x.Provider, x.ProviderOrderId,
        x.ProviderPaymentId, x.PaidAtUtc, x.MembershipId, x.CreatedAtUtc,
        x.ProviderOrderCreatedAtUtc, x.LastReconciledAtUtc);

    private PaymentHistory NewPaymentHistory(
        Payment payment, PaymentStatus? previous, PaymentStatus current,
        Guid userId, string reason, string? providerEventId = null) =>
        new()
        {
            Payment = payment,
            UserId = userId,
            PreviousStatus = previous,
            CurrentStatus = current,
            OccurredAtUtc = UtcNow,
            Reason = reason,
            ProviderEventId = providerEventId
        };

    private MembershipHistory NewMembershipHistory(
        Membership membership, MembershipStatus? previous,
        MembershipStatus current, Guid userId, string reason) =>
        new()
        {
            Membership = membership,
            UserId = userId,
            PreviousStatus = previous,
            CurrentStatus = current,
            OccurredAtUtc = UtcNow,
            Reason = reason
        };

    private sealed record WebhookPayment(
        string OrderId, string PaymentId, long AmountInMinorUnits,
        string CurrencyCode, bool IsSuccessful);

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
