using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Candidate")]
[Route("api/payments")]
[Produces("application/json")]
public sealed class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [NonAction]
    public async Task<ActionResult<ApiResponse<PaymentOrderResponse>>> CreateOrder(
        [FromBody] CreatePaymentOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await paymentService.CreateOrderAsync(User.GetRequiredUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<PaymentOrderResponse>(result, "Razorpay order created."));
    }

    [HttpPost("phonepe/checkout")]
    [ProducesResponseType(typeof(ApiResponse<PhonePeCheckoutResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<PhonePeCheckoutResponse>>> CreatePhonePeCheckout(
        [FromQuery] string? returnTo, CancellationToken cancellationToken)
    {
        var result = await paymentService.CreatePhonePeCheckoutAsync(
            User.GetRequiredUserId(), returnTo, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<PhonePeCheckoutResponse>(result, "PhonePe checkout created."));
    }

    [HttpGet("pending-membership-checkout")]
    public async Task<ActionResult<ApiResponse<PendingMembershipCheckoutResponse?>>> PendingMembershipCheckout(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PendingMembershipCheckoutResponse?>(
            await paymentService.GetPendingMembershipCheckoutAsync(
                User.GetRequiredUserId(), cancellationToken)));

    [HttpPost("pending-membership-checkout/{publicReference}/cancel")]
    public async Task<ActionResult<ApiResponse<PendingMembershipCheckoutResponse>>> CancelPendingMembershipCheckout(
        string publicReference, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PendingMembershipCheckoutResponse>(
            await paymentService.CancelPendingMembershipCheckoutAsync(
                User.GetRequiredUserId(), publicReference, cancellationToken),
            "Membership checkout reconciled."));

    [HttpGet("phonepe/return/{merchantOrderId}")]
    public async Task<ActionResult<ApiResponse<PhonePeReturnStatusResponse>>> PhonePeReturnStatus(
        string merchantOrderId, [FromQuery] string? returnTo, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PhonePeReturnStatusResponse>(
            await paymentService.GetPhonePeStatusAsync(
                User.GetRequiredUserId(), merchantOrderId, returnTo, cancellationToken)));

    [AllowAnonymous]
    [HttpPost("phonepe/webhook")]
    [Consumes("application/json")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<ActionResult<ApiResponse<PhonePeWebhookResponse>>> PhonePeWebhook(
        CancellationToken cancellationToken)
    {
        const int maximumBytes = 1024 * 1024;
        if (Request.ContentLength > maximumBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        await using var body = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await Request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (body.Length + read > maximumBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        var result = await paymentService.ProcessPhonePeWebhookAsync(new(
            body.ToArray(), Request.Headers.Authorization.ToString()), cancellationToken);
        return Ok(new ApiResponse<PhonePeWebhookResponse>(result));
    }

    [HttpPost("{paymentId:guid}/razorpay/confirm")]
    public async Task<ActionResult<ApiResponse<PaymentResponse>>> Confirm(
        Guid paymentId, [FromBody] ConfirmRazorpayPaymentRequest request,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PaymentResponse>(
            await paymentService.ConfirmAsync(User.GetRequiredUserId(), paymentId, request, cancellationToken),
            "Payment confirmed and membership activated."));

    [HttpPost("{paymentId:guid}/razorpay/reconcile")]
    public async Task<ActionResult<ApiResponse<PaymentResponse>>> Reconcile(
        Guid paymentId, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PaymentResponse>(
            await paymentService.ReconcileAsync(
                User.GetRequiredUserId(), paymentId, cancellationToken),
            "Payment reconciliation completed."));

    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<PaymentStatusResponse>>> Status(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PaymentStatusResponse>(
            await paymentService.GetStatusAsync(User.GetRequiredUserId(), cancellationToken)));

    [AllowAnonymous]
    [HttpPost("razorpay/webhook")]
    [Consumes("application/json")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<ActionResult<ApiResponse<RazorpayWebhookResponse>>> Webhook(
        CancellationToken cancellationToken)
    {
        const int maximumBytes = 1024 * 1024;
        if (Request.ContentLength > maximumBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        await using var body = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await Request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (body.Length + read > maximumBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        var result = await paymentService.ProcessWebhookAsync(
            new(body.ToArray(), Request.Headers["X-Razorpay-Signature"].ToString(),
                Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault()),
            cancellationToken);
        return Ok(new ApiResponse<RazorpayWebhookResponse>(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<PaymentResponse>>>> Payments(
        [FromQuery] HistoryQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<PaymentResponse>>(
            await paymentService.GetPaymentsAsync(User.GetRequiredUserId(), query, cancellationToken)));

    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<PagedResponse<PaymentHistoryResponse>>>> History(
        [FromQuery] HistoryQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<PaymentHistoryResponse>>(
            await paymentService.GetHistoryAsync(User.GetRequiredUserId(), query, cancellationToken)));
}
