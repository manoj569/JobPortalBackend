using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Payments;
using Microsoft.Extensions.Configuration;

namespace JobPortal.Infrastructure.Payments;

public sealed class RazorpayGateway : IRazorpayGateway
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("https://api.razorpay.com/v1/"),
        Timeout = TimeSpan.FromSeconds(15)
    };
    private readonly string _keySecret;
    private readonly string _webhookSecret;

    public RazorpayGateway(IConfiguration configuration)
    {
        KeyId = RequiredSecret(configuration, "Razorpay:KeyId");
        _keySecret = RequiredSecret(configuration, "Razorpay:KeySecret");
        _webhookSecret = RequiredSecret(configuration, "Razorpay:WebhookSecret");
        if (!KeyId.StartsWith("rzp_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Only a Razorpay Test Mode KeyId is permitted.");
    }

    public string KeyId { get; }

    public async Task<RazorpayOrder> CreateOrderAsync(
        long amountInMinorUnits, string currencyCode, string receipt,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "orders");
        AddAuthorization(request);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            amount = amountInMinorUnits,
            currency = currencyCode,
            receipt
        }), Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new RazorpayOrder(
            root.GetProperty("id").GetString()!,
            root.GetProperty("amount").GetInt64(),
            root.GetProperty("currency").GetString()!,
            root.GetProperty("receipt").GetString()!);
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
    {
        var payload = Encoding.UTF8.GetBytes($"{orderId}|{paymentId}");
        return VerifySignature(payload, signature, _keySecret);
    }

    public bool VerifyWebhookSignature(ReadOnlyMemory<byte> payload, string signature) =>
        VerifySignature(payload.Span, signature, _webhookSecret);

    public async Task<RazorpayPaymentState> GetOrderPaymentStateAsync(
        string orderId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"orders/{Uri.EscapeDataString(orderId)}/payments");
        AddAuthorization(request);
        using var response = await Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        RazorpayPaymentState? failed = null;
        var hasPendingPayment = false;
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(item.GetProperty("order_id").GetString(), orderId, StringComparison.Ordinal))
                continue;
            var id = item.GetProperty("id").GetString();
            var status = item.GetProperty("status").GetString();
            var amount = item.GetProperty("amount").GetInt64();
            var currency = item.GetProperty("currency").GetString();
            if (status is "captured")
                return new(RazorpayPaymentStateKind.Paid, id, amount, currency);
            if (status is "failed")
                failed = new(RazorpayPaymentStateKind.Failed, id, amount, currency);
            else if (status is "cancelled")
                failed = new(RazorpayPaymentStateKind.Cancelled, id, amount, currency);
            else if (status is "expired")
                failed = new(RazorpayPaymentStateKind.Expired, id, amount, currency);
            else
                hasPendingPayment = true;
        }
        return hasPendingPayment ? new(RazorpayPaymentStateKind.Pending) :
            failed ?? new(RazorpayPaymentStateKind.Pending);
    }

    private void AddAuthorization(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{_keySecret}")));

    private static bool VerifySignature(
        ReadOnlySpan<byte> payload, string signature, string secret)
    {
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return TryDecodeHex(signature, out var supplied) &&
               CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        try { bytes = Convert.FromHexString(value); return true; }
        catch (FormatException) { bytes = []; return false; }
    }

    private static string RequiredSecret(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("CONFIGURE_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{key} must be configured through User Secrets or an environment variable.");
        return value;
    }
}

public sealed class ConfigurationMembershipPlanProvider(IConfiguration configuration) : IMembershipPlanProvider
{
    public MembershipPlan GetDefaultPlan() => GetRequired("CareerHarborMembership");
    public MembershipPlan GetRequired(string planCode) => GetPlans().SingleOrDefault(x => string.Equals(x.Code, planCode, StringComparison.OrdinalIgnoreCase) && x.IsActive)
        ?? throw new JobPortal.Application.Common.Exceptions.NotFoundException("Membership plan was not found.");
    public MembershipPlan GetByPayment(decimal amount, string currencyCode) => GetPlans().SingleOrDefault(x => x.Amount == amount && string.Equals(x.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Payment does not match a configured membership plan.");
    public MembershipPlan? FindByName(string planName) => GetPlans().SingleOrDefault(x => string.Equals(x.Name, planName, StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<MembershipPlan> GetPlans()
    {
        var plans = configuration.GetSection("Membership:Plans").GetChildren().Select(x => new MembershipPlan(x.Key, x["DisplayName"] ?? "", decimal.Parse(x["Price"] ?? "0", System.Globalization.CultureInfo.InvariantCulture), x["Currency"] ?? "", int.Parse(x["DurationDays"] ?? "0", System.Globalization.CultureInfo.InvariantCulture), !bool.TryParse(x["IsActive"], out var active) || active, bool.TryParse(x["AIApplyEnabled"], out var ai) && ai, bool.TryParse(x["AIApplyProEnabled"], out var pro) && pro)).ToList();
        if (plans.Count != 3 || plans.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.CurrencyCode != "INR" || x.DurationDays != 30) || plans.Single(x => x.Code == "CareerHarborMembership").Amount != 99m || plans.Single(x => x.Code == "AIApply").Amount != 999m || plans.Single(x => x.Code == "AIApplyPro").Amount != 1499m) throw new InvalidOperationException("Membership plan configuration is invalid.");
        return plans;
    }
}
