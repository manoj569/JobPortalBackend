using System.Security.Cryptography;
using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerFinanceService(ICareerFinanceRepository repository, IUserRepository users,
    ICareerPaymentGateway gateway, IAuditWriter audit, TimeProvider clock, IOptions<CareerFinanceOptions> options) : ICareerFinanceService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static (decimal Commission, decimal Net) Split(decimal gross, decimal percent)
    {
        if (gross <= 0 || decimal.Round(gross, 2) != gross || percent is < 0 or > 100)
            throw new BadRequestException("Invalid financial amounts.");
        var commission = decimal.Round(gross * percent / 100m, 2, MidpointRounding.AwayFromZero);
        return (commission, gross - commission);
    }
    public static long MinorUnits(decimal amount) => amount > 0 && decimal.Round(amount, 2) == amount && amount <= 1000000m
        ? checked((long)(amount * 100m)) : throw new BadRequestException("Unsupported payment amount.");

    public async Task<CareerCheckout> OrderAsync(Guid actor, Guid bookingId, CancellationToken ct)
    {
        await Actor(actor, "Candidate", ct);
        if (!options.Value.PaymentsEnabled || !options.Value.IsValid()) throw new ConflictException("Career Guidance checkout is not enabled.");
        gateway.ValidateConfiguration();
        var booking = await repository.BookingAsync(bookingId, ct);
        if (booking is null || booking.CandidateUserId != actor || booking.IsDeleted) throw new NotFoundException("Booking not found.");
        var payment = await repository.ForBookingAsync(bookingId, ct);
        if (CareerReservationPolicy.ExpireIfDue(booking, payment, Now))
        {
            await Audit(booking.Id, "reservation_expired", actor, ct);
            await repository.SaveAsync(ct);
            throw new ConflictException("The unpaid booking reservation has expired.", "reservation_expired");
        }
        if (booking.Consultant.UserId == actor || booking.Status != CareerBookingStatus.Pending || booking.StartUtc <= Now ||
            booking.Consultant.IsDeleted || booking.Consultant.VerificationStatus != ConsultantVerificationStatus.Verified ||
            booking.Consultant.User.IsDeleted || booking.Consultant.User.Status != UserStatus.Active || booking.Service.IsDeleted || !booking.Service.IsActive)
            throw new ConflictException("Booking is not eligible for checkout.");
        if (booking.CurrencySnapshot != "INR") throw new BadRequestException("Career Guidance checkout currently supports INR only.");
        var firstDispatch = payment is null;
        if (payment is null)
        {
            var amounts = Split(booking.PriceSnapshot, options.Value.PlatformCommissionPercent!.Value);
            _ = MinorUnits(booking.PriceSnapshot);
            payment = new() { BookingId = booking.Id, Booking = booking, CandidateUserId = actor, ConsultantId = booking.ConsultantId,
                AmountGross = booking.PriceSnapshot, Currency = booking.CurrencySnapshot,
                PlatformCommissionPercentSnapshot = options.Value.PlatformCommissionPercent.Value,
                PlatformCommissionAmount = amounts.Commission, ConsultantNetAmount = amounts.Net };
            repository.Add(payment);
            booking.RequiresPayment = true;
            booking.Revision = Guid.NewGuid();
            // Commit a unique durable intent BEFORE any external write. Never retry POST on an uncertain outcome.
            await Save(payment, "order_requested", actor, ct);
        }
        if (payment.Status is not (CareerPaymentStatus.Created or CareerPaymentStatus.OrderCreated or CareerPaymentStatus.Authorized))
            throw new ConflictException("Payment is no longer payable.");
        if (payment.ProviderOrderId is null)
        {
            var receipt = Receipt(payment.Id);
            var order = firstDispatch
                ? await gateway.CreateOrderAsync(MinorUnits(payment.AmountGross), payment.Currency, receipt, ct)
                : await gateway.FindOrderAsync(receipt, ct);
            if (order is null) throw new ConflictException("Order outcome is unresolved. Retry reconciliation later; do not create another order.", "provider_reconciliation_required");
            if (order.Amount != MinorUnits(payment.AmountGross) || order.Currency != payment.Currency || order.Receipt != receipt || !Identifier(order.Id))
                throw new ConflictException("Provider order does not match the payment.");
            payment.ProviderOrderId = order.Id;
            payment.Status = CareerPaymentStatus.OrderCreated;
            await Save(payment, "order_created", actor, ct);
        }
        return new(payment.Id, gateway.KeyId, payment.ProviderOrderId, MinorUnits(payment.AmountGross), payment.Currency);
    }

    public async Task<CareerPaymentResponse> VerifyAsync(Guid actor, Guid bookingId, CareerVerifyRequest request, CancellationToken ct)
    {
        await Actor(actor, "Candidate", ct);
        var payment = await repository.ForBookingAsync(bookingId, ct);
        if (payment is null || payment.CandidateUserId != actor) throw new NotFoundException("Payment not found.");
        if (!Identifier(request.PaymentId) || request.OrderId != payment.ProviderOrderId ||
            !gateway.VerifyCheckout(payment.ProviderOrderId!, request.PaymentId, request.Signature))
            throw new BadRequestException("Payment verification failed.");
        var state = await gateway.GetPaymentAsync(request.PaymentId, ct);
        if (state.Id != request.PaymentId || state.Status != "captured") throw new ConflictException("Provider has not confirmed the requested payment capture.");
        await Capture(payment, state, actor, ct);
        await Save(payment, "payment_verified", actor, ct);
        return PaymentDto(payment);
    }

    public async Task<CareerPaymentResponse> ReconcileAsync(Guid actor, Guid bookingId, CancellationToken ct)
    {
        await Actor(actor, "Candidate", ct);
        var p = await repository.ForBookingAsync(bookingId, ct);
        if (p is null || p.CandidateUserId != actor) throw new NotFoundException("Payment not found.");
        if (p.ProviderOrderId is null)
        {
            var order = await gateway.FindOrderAsync(Receipt(p.Id), ct);
            if (order is null) throw new ConflictException("Order requires reconciliation.", "provider_reconciliation_required");
            if (order.Amount != MinorUnits(p.AmountGross) || order.Currency != p.Currency || order.Receipt != Receipt(p.Id))
                throw new ConflictException("Provider order mismatch.");
            p.ProviderOrderId = order.Id; p.Status = CareerPaymentStatus.OrderCreated;
        }
        var state = await gateway.CapturedPaymentAsync(p.ProviderOrderId, ct);
        if (state is not null) await Capture(p, state, actor, ct);
        await Save(p, "payment_reconciled", actor, ct);
        return PaymentDto(p);
    }

    private async Task Capture(CareerGuidancePayment p, GatewayPayment state, Guid? actor, CancellationToken ct)
    {
        Validate(p, state);
        if (p.ProviderPaymentId is not null && p.ProviderPaymentId != state.Id) throw new ConflictException("Provider payment is already bound.");
        if (p.PaidAtUtc.HasValue) return;
        if (CareerReservationPolicy.ExpireIfDue(p.Booking, p, Now))
            await Audit(p.BookingId, "reservation_expired", actor, ct);
        p.ProviderPaymentId = state.Id; p.PaidAtUtc = Now; p.Status = CareerPaymentStatus.Captured; p.FailureCode = null;
        var b = p.Booking;
        // Late capture is a financial fact, not permission to resurrect cancelled/expired capacity.
        if (!b.IsDeleted && b.Status == CareerBookingStatus.Pending && b.StartUtc > Now && !b.Consultant.IsDeleted &&
            b.Consultant.VerificationStatus == ConsultantVerificationStatus.Verified && !b.Consultant.User.IsDeleted && b.Consultant.User.Status == UserStatus.Active)
        {
            b.Status = CareerBookingStatus.Confirmed;
            b.Consultant.Revision = Guid.NewGuid();
        }
        else p.RequiresRefundReview = true;
        b.Revision = Guid.NewGuid();
        p.Earning = new() { PaymentId = p.Id, BookingId = p.BookingId, ConsultantId = p.ConsultantId,
            GrossAmount = p.AmountGross, PlatformCommissionAmount = p.PlatformCommissionAmount,
            NetAmount = p.ConsultantNetAmount, Currency = p.Currency };
        await Audit(p.Id, "payment_captured", actor, ct);
        await Audit(p.Id, "earning_created", actor, ct);
    }

    public async Task<bool> TryWebhookAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken ct)
    {
        if (body.IsEmpty || body.Length > 1024 * 1024 || !gateway.VerifyWebhook(body, signature)) return false;
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var type = root.GetProperty("event").GetString() ?? "";
            if (type is not ("payment.authorized" or "payment.captured" or "payment.failed" or "refund.created" or "refund.processed" or "refund.failed")) return false;
            var refundEvent = type.StartsWith("refund.", StringComparison.Ordinal);
            var entity = root.GetProperty("payload").GetProperty(refundEvent ? "refund" : "payment").GetProperty("entity");
            var id = entity.GetProperty("id").GetString();
            if (!Identifier(id)) throw new BadRequestException("Invalid webhook entity.");
            CareerGuidancePayment? p;
            if (refundEvent)
                p = await repository.ForProviderPaymentAsync(entity.GetProperty("payment_id").GetString() ?? "", ct);
            else p = await repository.ForOrderAsync(entity.GetProperty("order_id").GetString() ?? "", ct);
            if (p is null) return false; // The existing membership handler owns its own orders.
            // Hash signed raw bytes, not the unsigned event-id header. Financial revision/unique keys
            // additionally make semantically duplicate payloads harmless.
            var key = Convert.ToHexString(SHA256.HashData(body.Span));
            if (await repository.HasEventAsync(key, ct)) return true;
            if (refundEvent)
            {
                var state = await gateway.GetRefundAsync(id!, ct);
                if (p.Refund is null) throw new ConflictException("Unregistered provider refund requires administrator reconciliation.");
                await ApplyRefund(p, state, null, ct);
            }
            else
            {
                var state = await gateway.GetPaymentAsync(id!, ct);
                Validate(p, state);
                if (state.Status == "captured") await Capture(p, state, null, ct);
                else if (!p.PaidAtUtc.HasValue)
                {
                    if (state.Status == "authorized") p.Status = CareerPaymentStatus.Authorized;
                    // Failure belongs to an attempt; it must not close an order that can still be paid.
                    if (state.Status == "failed") { p.FailureCode = "provider_attempt_failed"; await Audit(p.Id, "payment_attempt_failed", null, ct); }
                }
            }
            repository.Add(new CareerGuidancePaymentEvent { PaymentId = p.Id, EventKey = key, EventType = type });
            await Save(p, "webhook_processed", null, ct);
            return true;
        }
        catch (JsonException) { throw new BadRequestException("Invalid webhook JSON."); }
        catch (KeyNotFoundException) { throw new BadRequestException("Invalid webhook structure."); }
        catch (InvalidOperationException) { throw new BadRequestException("Invalid webhook structure."); }
    }

    public async Task<CareerRefundResponse> RefundAsync(Guid actor, Guid paymentId, CareerRefundRequest request, CancellationToken ct)
    {
        await Actor(actor, "Administrator", ct);
        if (!Enum.IsDefined(request.Reason)) throw new BadRequestException("Invalid refund reason.");
        var p = await repository.PaymentAsync(paymentId, ct) ?? throw new NotFoundException("Payment not found.");
        if (!p.PaidAtUtc.HasValue || p.ProviderPaymentId is null || p.Earning is null) throw new ConflictException("Payment is not captured.");
        if (p.Earning.Status is CareerEarningStatus.Payable or CareerEarningStatus.Settled) throw new ConflictException("Settlement reconciliation is required before refunding.");
        var firstDispatch = p.Refund is null;
        if (p.Refund is null)
        {
            if (request.Reason == CareerRefundReason.ConsultantCancellation && p.Booking.Status != CareerBookingStatus.CancelledByConsultant ||
                request.Reason == CareerRefundReason.ConsultantNoShow && p.Booking.Status != CareerBookingStatus.NoShowConsultant)
                throw new ConflictException("Booking does not meet the selected objective refund condition.");
            p.Refund = new() { PaymentId = p.Id, BookingId = p.BookingId, CandidateUserId = p.CandidateUserId,
                ConsultantId = p.ConsultantId, Amount = p.AmountGross, Currency = p.Currency, ReasonCode = request.Reason,
                RequestedByUserId = actor, RequestedAtUtc = Now };
            p.Status = CareerPaymentStatus.RefundPending;
            p.Earning!.AvailableAtUtc = null; p.Earning.Revision = Guid.NewGuid();
            if (p.Booking.Status is CareerBookingStatus.Pending or CareerBookingStatus.Confirmed)
            {
                p.Booking.Status = CareerBookingStatus.CancelledByConsultant;
                p.Booking.CancelledAtUtc = Now; p.Booking.CancelledByUserId = actor;
                p.Booking.CancellationReason = "Administrator approved refund."; p.Booking.Revision = Guid.NewGuid();
            }
            await Save(p, "refund_approved", actor, ct);
        }
        if (p.Refund.Status == CareerRefundStatus.Processed) return RefundDto(p.Refund);
        var receipt = Receipt(p.Refund.Id);
        var state = firstDispatch ? await gateway.CreateRefundAsync(p.ProviderPaymentId, MinorUnits(p.AmountGross), receipt, ct)
            : p.Refund.ProviderRefundId is not null ? await gateway.GetRefundAsync(p.Refund.ProviderRefundId, ct)
            : await gateway.FindRefundAsync(p.ProviderPaymentId, receipt, ct);
        if (state is null) throw new ConflictException("Refund outcome is unresolved; do not submit a second refund.", "provider_reconciliation_required");
        await ApplyRefund(p, state, actor, ct);
        await Save(p, "refund_reconciled", actor, ct);
        return RefundDto(p.Refund);
    }

    private async Task ApplyRefund(CareerGuidancePayment p, GatewayRefund state, Guid? actor, CancellationToken ct)
    {
        var r = p.Refund!;
        if (!Identifier(state.Id) || state.PaymentId != p.ProviderPaymentId || state.Amount != MinorUnits(r.Amount) || state.Currency != r.Currency ||
            state.Receipt != Receipt(r.Id) || r.ProviderRefundId is not null && r.ProviderRefundId != state.Id)
            throw new ConflictException("Provider refund does not match the approved refund.");
        if (r.Status == CareerRefundStatus.Processed) return;
        r.ProviderRefundId = state.Id; r.Revision = Guid.NewGuid();
        switch (state.Status)
        {
            case "processed":
                r.Status = CareerRefundStatus.Processed; r.ProcessedAtUtc = Now;
                p.Status = CareerPaymentStatus.Refunded; p.RequiresRefundReview = false;
                p.Earning!.Status = CareerEarningStatus.Reversed; p.Earning.ReversedAtUtc = Now; p.Earning.AvailableAtUtc = null; p.Earning.Revision = Guid.NewGuid();
                await Audit(p.Id, "refund_processed", actor, ct); await Audit(p.Id, "earning_reversed", actor, ct);
                break;
            case "failed":
                r.Status = CareerRefundStatus.Failed; r.FailedAtUtc = Now; p.RequiresRefundReview = true;
                await Audit(p.Id, "refund_failed", actor, ct);
                break;
            case "pending":
                if (r.Status != CareerRefundStatus.Failed) r.Status = CareerRefundStatus.ProviderPending;
                break;
            default: throw new ConflictException("Unsupported provider refund state.");
        }
    }

    public async Task<CareerPaymentResponse> GetAsync(Guid actor, Guid id, bool admin, CancellationToken ct)
    {
        await Actor(actor, admin ? "Administrator" : "Candidate", ct);
        var p = admin ? await repository.PaymentAsync(id, ct) : await repository.ForBookingAsync(id, ct);
        if (p is null || !admin && p.CandidateUserId != actor) throw new NotFoundException("Payment not found.");
        return PaymentDto(p);
    }
    public async Task<PagedResponse<CareerPaymentResponse>> PaymentsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct)
    {
        await Actor(actor, admin ? "Administrator" : "Candidate", ct); Page(query);
        var page = await repository.PaymentsAsync(actor, admin, query, ct);
        return new(page.Items.Select(PaymentDto).ToArray(), page.PageNumber, page.PageSize, page.TotalCount);
    }
    public async Task<PagedResponse<CareerRefundResponse>> RefundsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct)
    {
        await Actor(actor, admin ? "Administrator" : "Candidate", ct); Page(query);
        var page = await repository.RefundsAsync(actor, admin, query, ct);
        return new(page.Items.Select(RefundDto).ToArray(), page.PageNumber, page.PageSize, page.TotalCount);
    }
    public async Task<CareerRefundResponse> RefundDetailAsync(Guid actor, Guid id, CancellationToken ct)
    {
        await Actor(actor, "Administrator", ct);
        return RefundDto(await repository.RefundAsync(id, ct) ?? throw new NotFoundException("Refund not found."));
    }
    public async Task<PagedResponse<CareerEarningResponse>> EarningsAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct)
    {
        await Actor(actor, admin ? "Administrator" : null, ct); Page(query);
        var page = await repository.EarningsAsync(actor, admin, query, ct);
        return new(page.Items.Select(e => new CareerEarningResponse(e.Id, e.PaymentId, e.BookingId, e.GrossAmount,
            e.PlatformCommissionAmount, e.NetAmount, e.Currency, e.Status, e.ReversedAtUtc)).ToArray(), page.PageNumber, page.PageSize, page.TotalCount);
    }
    public async Task<IReadOnlyCollection<CareerEarningSummary>> SummaryAsync(Guid actor, CancellationToken ct)
    { await Actor(actor, null, ct); return await repository.SummaryAsync(actor, ct); }
    private async Task Actor(Guid id, string? role, CancellationToken ct)
    {
        var u = await users.GetByIdWithRoleAsync(id, ct);
        if (u is null || u.IsDeleted || u.Status != UserStatus.Active) throw new UnauthorizedException();
        if (role is not null && u.Role.Name != role) throw new AppException("Access denied.", 403, "forbidden");
    }
    private static void Page(FinanceQuery q)
    { if (q.PageNumber is < 1 or > 1000000 || q.PageSize is < 1 or > 100) throw new BadRequestException("Invalid pagination."); }
    private static bool Identifier(string? value) => value is { Length: > 0 and <= 100 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    private static string Receipt(Guid id) => $"cg_{id:N}";
    private static void Validate(CareerGuidancePayment p, GatewayPayment s)
    {
        if (!Identifier(s.Id) || s.OrderId != p.ProviderOrderId || s.Amount != MinorUnits(p.AmountGross) || s.Currency != p.Currency)
            throw new ConflictException("Provider payment does not match the order.");
    }
    private async Task Save(CareerGuidancePayment p, string action, Guid? actor, CancellationToken ct)
    { p.Revision = Guid.NewGuid(); await Audit(p.Id, action, actor, ct); await repository.SaveAsync(ct); }
    private Task Audit(Guid id, string action, Guid? actor, CancellationToken ct) => audit.AppendAsync(new(AuditAction.Update,
        "CareerGuidanceFinance", id.ToString(), new Dictionary<string, string?> { ["result"] = action }, new(actor, "CareerGuidanceFinance")), ct);
    private static CareerPaymentResponse PaymentDto(CareerGuidancePayment p) => new(p.Id, p.BookingId, p.AmountGross, p.Currency,
        p.PlatformCommissionPercentSnapshot, p.PlatformCommissionAmount, p.ConsultantNetAmount, p.Status, p.ProviderOrderId,
        p.ProviderPaymentId, p.PaidAtUtc, p.RequiresRefundReview, p.FailureCode);
    private static CareerRefundResponse RefundDto(CareerGuidanceRefund r) => new(r.Id, r.PaymentId, r.BookingId, r.Amount, r.Currency, r.ReasonCode, r.Status, r.RequestedAtUtc, r.ProcessedAtUtc);
}
