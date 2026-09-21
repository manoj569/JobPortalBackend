using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Application.Features.Payments;
using JobPortal.Infrastructure.Payments;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace JobPortal.API.Controllers;

// Keep the existing single URL. Resolve the membership service only for its signed events:
// Career Guidance never invokes membership fulfillment or its test-mode-only gateway.
[ApiController, AllowAnonymous, Route("api/payments/razorpay/webhook")]
public sealed class RazorpayWebhooksController(ICareerFinanceService finance, ICareerPaymentGateway gateway,
    IConfiguration configuration, IServiceProvider services) : ControllerBase
{
    [HttpPost, Consumes("application/json"), RequestSizeLimit(1024 * 1024)]
    public async Task<ActionResult<ApiResponse<RazorpayWebhookResponse>>> Webhook(CancellationToken ct)
    {
        const int limit = 1024 * 1024;
        if (Request.ContentLength > limit) return StatusCode(413);
        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await Request.Body.ReadAsync(buffer, ct)) != 0)
        {
            if (body.Length + read > limit) return StatusCode(413);
            body.Write(buffer, 0, read);
        }
        var bytes = body.ToArray();
        var signature = Request.Headers["X-Razorpay-Signature"].ToString();
        if (await finance.TryWebhookAsync(bytes, signature, ct))
            return Ok(new ApiResponse<RazorpayWebhookResponse>(new("Career Guidance event acknowledged.")));
        if (CareerRazorpayGateway.VerifyHmac(bytes, signature, configuration["Razorpay:WebhookSecret"]))
        {
            // Do not construct membership's test-only gateway for unknown/live Career Guidance
            // orders. The same public URL may receive unrelated merchant events.
            string? orderId = null;
            try
            {
                using var json = JsonDocument.Parse(bytes);
                if (json.RootElement.TryGetProperty("payload", out var payload) && payload.TryGetProperty("payment", out var payment) &&
                    payment.TryGetProperty("entity", out var entity) && entity.TryGetProperty("order_id", out var order)) orderId = order.GetString();
            }
            catch (JsonException) { throw new BadRequestException("Invalid webhook JSON."); }
            catch (InvalidOperationException) { throw new BadRequestException("Invalid webhook structure."); }
            if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 200 ||
                await services.GetRequiredService<IPaymentRepository>().GetByProviderOrderIdAsync(orderId, ct) is null)
                return Ok(new ApiResponse<RazorpayWebhookResponse>(new("Unknown event acknowledged.")));
            var membership = services.GetRequiredService<IPaymentService>();
            var response = await membership.ProcessWebhookAsync(new(bytes, signature, Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault()), ct);
            return Ok(new ApiResponse<RazorpayWebhookResponse>(response));
        }
        if (gateway.VerifyWebhook(bytes, signature))
            return Ok(new ApiResponse<RazorpayWebhookResponse>(new("Unknown event acknowledged.")));
        throw new BadRequestException("Invalid webhook authentication.");
    }
}
