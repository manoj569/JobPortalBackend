using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using Microsoft.Extensions.Configuration;

namespace JobPortal.Infrastructure.Payments;

public sealed class CareerRazorpayGateway(HttpClient client, IConfiguration configuration) : ICareerPaymentGateway
{
    // Separate optional credentials allow a controlled rollout without removing membership's test-mode guard.
    private string? Setting(string name) => configuration[$"CareerGuidance:Razorpay:{name}"] ?? configuration[$"Razorpay:{name}"];
    public string KeyId => Required("KeyId");
    public void ValidateConfiguration()
    {
        if (!KeyId.StartsWith("rzp_test_", StringComparison.Ordinal) && !KeyId.StartsWith("rzp_live_", StringComparison.Ordinal))
            throw new ConflictException("Career Guidance provider key mode is invalid.");
        _ = Required("KeySecret"); _ = Required("WebhookSecret");
    }
    private string Required(string name)
    {
        var value = Setting(name);
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("CONFIGURE_", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Career Guidance payment provider is not configured.");
        return value;
    }
    public bool VerifyCheckout(string orderId, string paymentId, string signature) =>
        VerifyHmac(Encoding.UTF8.GetBytes($"{orderId}|{paymentId}"), signature, Required("KeySecret"));
    public bool VerifyWebhook(ReadOnlyMemory<byte> body, string signature) => VerifyHmac(body.Span, signature, Setting("WebhookSecret"));
    public static bool VerifyHmac(ReadOnlySpan<byte> body, string? signature, string? secret)
    {
        if (signature is not { Length: 64 } || string.IsNullOrWhiteSpace(secret) || secret.StartsWith("CONFIGURE_", StringComparison.OrdinalIgnoreCase)) return false;
        try { return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body), Convert.FromHexString(signature)); }
        catch (FormatException) { return false; }
    }
    public async Task<GatewayOrder> CreateOrderAsync(long amount, string currency, string receipt, CancellationToken ct)
    {
        using var document = await Send(HttpMethod.Post, "orders", new { amount, currency, receipt, partial_payment = false }, ct);
        return Order(document.RootElement);
    }
    public async Task<GatewayOrder?> FindOrderAsync(string receipt, CancellationToken ct)
    {
        using var document = await Send(HttpMethod.Get, $"orders?receipt={Uri.EscapeDataString(receipt)}&count=2", null, ct);
        var matches = document.RootElement.GetProperty("items").EnumerateArray().Select(Order).Where(o => o.Receipt == receipt).ToArray();
        if (matches.Length > 1) throw new ConflictException("Multiple provider orders require manual reconciliation.");
        return matches.SingleOrDefault();
    }
    public async Task<GatewayPayment> GetPaymentAsync(string paymentId, CancellationToken ct)
    {
        using var document = await Send(HttpMethod.Get, $"payments/{Uri.EscapeDataString(paymentId)}", null, ct);
        var r = document.RootElement;
        return new(Text(r, "id"), Text(r, "order_id"), r.GetProperty("amount").GetInt64(), Text(r, "currency"), Text(r, "status"));
    }
    public async Task<GatewayRefund> CreateRefundAsync(string paymentId, long amount, string receipt, CancellationToken ct)
    {
        using var document = await Send(HttpMethod.Post, $"payments/{Uri.EscapeDataString(paymentId)}/refund", new { amount, receipt, speed = "normal" }, ct);
        return Refund(document.RootElement);
    }
    public async Task<GatewayPayment?> CapturedPaymentAsync(string orderId, CancellationToken ct)
    {
        using var document = await Send(HttpMethod.Get, $"orders/{Uri.EscapeDataString(orderId)}/payments", null, ct);
        var captured = document.RootElement.GetProperty("items").EnumerateArray().Where(r => Text(r, "status") == "captured")
            .Select(r => new GatewayPayment(Text(r, "id"), Text(r, "order_id"), r.GetProperty("amount").GetInt64(), Text(r, "currency"), Text(r, "status"))).ToArray();
        if (captured.Length > 1) throw new ConflictException("Multiple captured payments require administrator reconciliation.");
        return captured.SingleOrDefault();
    }
    public async Task<GatewayRefund?> FindRefundAsync(string paymentId, string receipt, CancellationToken ct)
    {
        using var document = await Send(HttpMethod.Get, $"payments/{Uri.EscapeDataString(paymentId)}/refunds?count=100", null, ct);
        var matches = document.RootElement.GetProperty("items").EnumerateArray().Select(Refund).Where(r => r.Receipt == receipt).ToArray();
        if (matches.Length > 1) throw new ConflictException("Multiple provider refunds require manual reconciliation.");
        return matches.SingleOrDefault(); // No match NEVER authorizes another POST.
    }
    public async Task<GatewayRefund> GetRefundAsync(string refundId, CancellationToken ct)
    {
        using var document = await Send(HttpMethod.Get, $"refunds/{Uri.EscapeDataString(refundId)}", null, ct);
        return Refund(document.RootElement);
    }
    private async Task<JsonDocument> Send(HttpMethod method, string path, object? payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{Required("KeySecret")}")));
        if (payload is not null) request.Content = JsonContent.Create(payload);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw ProviderFailure();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + read > 1024 * 1024) throw ProviderFailure();
                buffer.Write(chunk, 0, read);
            }
            return JsonDocument.Parse(buffer.ToArray());
        }
        catch (HttpRequestException) { throw ProviderFailure(); }
        catch (OperationCanceledException) { throw ProviderFailure(); }
        catch (JsonException) { throw ProviderFailure(); }
    }
    private static AppException ProviderFailure() => new("Provider result is unavailable; reconcile before retrying any financial operation.", 502, "provider_unavailable");
    private static string Text(JsonElement r, string name) => r.GetProperty(name).GetString() ?? "";
    private static GatewayOrder Order(JsonElement r) => new(Text(r, "id"), r.GetProperty("amount").GetInt64(), Text(r, "currency"), Text(r, "receipt"));
    private static GatewayRefund Refund(JsonElement r) => new(Text(r, "id"), Text(r, "payment_id"), r.GetProperty("amount").GetInt64(), Text(r, "currency"), Text(r, "status"), Text(r, "receipt"));
}
