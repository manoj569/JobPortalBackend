using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.Payments;

public sealed class PhonePeGateway(
    HttpClient client,
    IConfiguration configuration,
    TimeProvider timeProvider,
    PhonePeAccessTokenCache tokenCache,
    ILogger<PhonePeGateway> logger) : IPhonePeGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Action<ILogger, string, int, string?, string, Exception?> ProviderDiagnostic =
        LoggerMessage.Define<string, int, string?, string>(
            LogLevel.Warning, new EventId(4101, "PhonePeProviderDiagnostic"),
            "PhonePe operation {Operation} returned HTTP {HttpStatusCode}; provider category {ProviderCategory}; schema category {SchemaCategory}");
    private readonly string clientId = Required(configuration, "PhonePe:ClientId");
    private readonly string clientSecret = Required(configuration, "PhonePe:ClientSecret");
    private readonly string clientVersion = Required(configuration, "PhonePe:ClientVersion");
    private readonly string webhookUsername = Required(configuration, "PhonePe:WebhookUsername");
    private readonly string webhookPassword = Required(configuration, "PhonePe:WebhookPassword");
    private readonly PhonePeOptions settings = PhonePeOptions.Load(configuration);
    private PhonePeEndpoints Endpoints => settings.ResolveEndpoints();
    private Uri RedirectBaseUrl => new(settings.RedirectBaseUrl.Trim());

    public async Task<PhonePeCheckout> CreateCheckoutAsync(
        string merchantOrderId, long amountInMinorUnits, CancellationToken cancellationToken = default)
        => await CreateCheckoutAsync(merchantOrderId, amountInMinorUnits, null, cancellationToken);

    public async Task<PhonePeCheckout> CreateCheckoutAsync(
        string merchantOrderId, long amountInMinorUnits, string? returnTo,
        CancellationToken cancellationToken = default)
    {
        returnTo = PaymentReturnPath.Validate(returnTo);
        var query = $"merchantOrderId={Uri.EscapeDataString(merchantOrderId)}";
        if (returnTo is not null)
            query += $"&returnTo={Uri.EscapeDataString(returnTo)}";
        var redirectUrl = new UriBuilder(RedirectBaseUrl)
        {
            Path = RedirectBaseUrl.AbsolutePath.TrimEnd('/') + "/payment/phonepe/return",
            Query = query
        }.Uri.AbsoluteUri;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoints.ApiBaseUri, "checkout/v2/pay"))
        {
            Content = JsonContent.Create(new
            {
                merchantOrderId,
                amount = amountInMinorUnits,
                expireAfter = 1200,
                paymentFlow = new
                {
                    type = "PG_CHECKOUT",
                    message = "Career Harbor 30-day membership",
                    merchantUrls = new { redirectUrl }
                }
            }, options: JsonOptions)
        };
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogProviderFailure("CreateCheckout", response.StatusCode, SafeProviderCode(body), "provider_http_error");
            throw new AppException("PhonePe checkout is temporarily unavailable.", 503, "payment_provider_unavailable");
        }
        try
        {
            using var document = JsonDocument.Parse(body);
            var url = document.RootElement.GetProperty("redirectUrl").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
                throw new JsonException();
            DateTime? expires = null;
            if (document.RootElement.TryGetProperty("expireAt", out var expireAt) && expireAt.TryGetInt64(out var epoch))
                expires = DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime;
            return new(parsed.AbsoluteUri, expires);
        }
        catch (JsonException)
        {
            LogProviderFailure("CreateCheckout", response.StatusCode, null, "invalid_checkout_schema");
            throw new AppException("PhonePe returned an invalid checkout response.", 503, "payment_provider_invalid_response");
        }
    }

    public async Task<PhonePeOrderState> GetOrderStatusAsync(
        string merchantOrderId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(Endpoints.ApiBaseUri, $"checkout/v2/order/{Uri.EscapeDataString(merchantOrderId)}/status"));
        using var response = await SendAuthorizedAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogProviderFailure("GetOrderStatus", response.StatusCode, SafeProviderCode(body), "provider_http_error");
            throw new AppException("PhonePe payment verification is temporarily unavailable.", 503, "payment_verification_unavailable");
        }
        try
        {
            var status = JsonSerializer.Deserialize<OrderStatusDto>(body, JsonOptions)
                ?? throw SchemaMismatch("empty_response");
            if (!string.IsNullOrWhiteSpace(status.MerchantOrderId) &&
                !string.Equals(status.MerchantOrderId, merchantOrderId, StringComparison.Ordinal))
                throw SchemaMismatch("merchant_order_mismatch");
            var state = ParseState(status.State);
            if (status.Amount <= 0) throw SchemaMismatch("invalid_amount");
            var transactionId = status.PaymentDetails?
                .Where(x => string.Equals(x.State, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.TransactionId).LastOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? status.PaymentDetails?.Select(x => x.TransactionId)
                    .LastOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (state == PhonePeOrderStateKind.Completed && string.IsNullOrWhiteSpace(transactionId))
                throw SchemaMismatch("missing_completed_transaction");
            return new(state, merchantOrderId, transactionId, status.Amount);
        }
        catch (JsonException exception)
        {
            var category = exception.Data["PhonePeSchemaCategory"] as string ?? "malformed_json";
            LogProviderFailure("GetOrderStatus", response.StatusCode, SafeProviderCode(body), category);
            throw new AppException("PhonePe returned an invalid verification response.", 503, "payment_provider_invalid_response");
        }
    }

    public bool VerifyWebhookAuthorization(string authorization)
    {
        var supplied = authorization.Trim();
        if (supplied.StartsWith("SHA256 ", StringComparison.OrdinalIgnoreCase)) supplied = supplied[7..];
        if (supplied.Length != 64) return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes($"{webhookUsername}:{webhookPassword}"));
        try
        {
            var actual = Convert.FromHexString(supplied);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }

    public PhonePeCallback ParseCallback(ReadOnlyMemory<byte> rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var payload = root.GetProperty("payload");
            var merchantOrderId = payload.TryGetProperty("merchantOrderId", out var merchantId)
                ? merchantId.GetString() : payload.GetProperty("orderId").GetString();
            var state = ParseState(payload.GetProperty("state").GetString());
            if (string.IsNullOrWhiteSpace(merchantOrderId) || merchantOrderId.Length > 200)
                throw new JsonException();
            var eventId = $"phonepe_{Convert.ToHexString(SHA256.HashData(rawBody.Span)).ToLowerInvariant()}";
            return new(merchantOrderId, state, eventId);
        }
        catch (JsonException)
        {
            throw new BadRequestException("Invalid PhonePe webhook payload.", "invalid_webhook");
        }
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(false, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("O-Bearer", token);
        var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
        response.Dispose();
        token = await GetTokenAsync(true, cancellationToken);
        using var retry = await CloneAsync(request, cancellationToken);
        retry.Headers.Authorization = new AuthenticationHeaderValue("O-Bearer", token);
        return await client.SendAsync(retry, cancellationToken);
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var partition = tokenCache.For(Endpoints.Environment, clientId, clientVersion, clientSecret);
        await partition.Lock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && partition.Token is not null && partition.ExpiresAtUtc > timeProvider.GetUtcNow().UtcDateTime.AddMinutes(1))
                return partition.Token;
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoints.OAuthBaseUri, "v1/oauth/token"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_version"] = clientVersion,
                    ["client_secret"] = clientSecret,
                    ["grant_type"] = "client_credentials"
                })
            };
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogProviderFailure("GetAccessToken", response.StatusCode, SafeProviderCode(body), "provider_http_error");
                throw new AppException("PhonePe authentication is temporarily unavailable.", 503, "payment_provider_unavailable");
            }
            using var document = JsonDocument.Parse(body);
            var token = document.RootElement.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(token)) throw new JsonException();
            partition.ExpiresAtUtc = TokenExpiry(document.RootElement);
            partition.Token = token;
            return token;
        }
        catch (JsonException)
        {
            LogProviderFailure("GetAccessToken", HttpStatusCode.OK, null, "invalid_oauth_schema");
            throw new AppException("PhonePe returned an invalid authentication response.", 503, "payment_provider_invalid_response");
        }
        finally { partition.Lock.Release(); }
    }

    private DateTime TokenExpiry(JsonElement root)
    {
        if (root.TryGetProperty("expires_at", out var expiry) && expiry.TryGetInt64(out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        if (root.TryGetProperty("expires_in", out var duration) && duration.TryGetInt64(out var seconds))
            return timeProvider.GetUtcNow().UtcDateTime.AddSeconds(seconds);
        return timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5);
    }

    private static PhonePeOrderStateKind ParseState(string? state) => state?.ToUpperInvariant() switch
    {
        "COMPLETED" => PhonePeOrderStateKind.Completed,
        "FAILED" or "EXPIRED" => PhonePeOrderStateKind.Failed,
        "CANCELLED" => PhonePeOrderStateKind.Cancelled,
        "PENDING" => PhonePeOrderStateKind.Pending,
        _ => throw SchemaMismatch("unknown_state")
    };

    private static JsonException SchemaMismatch(string category)
    {
        var exception = new JsonException();
        exception.Data["PhonePeSchemaCategory"] = category;
        return exception;
    }

    private void LogProviderFailure(
        string operation, HttpStatusCode statusCode, string? providerCode, string schemaCategory)
    {
        if (providerCode is not null)
        {
            var token = tokenCache.For(Endpoints.Environment, clientId, clientVersion, clientSecret).Token;
            var webhookHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{webhookUsername}:{webhookPassword}")));
            if (new[] { clientId, clientSecret, webhookUsername, webhookPassword, token, webhookHash }
                .Any(value => !string.IsNullOrEmpty(value) && providerCode.Contains(value, StringComparison.OrdinalIgnoreCase)))
                providerCode = null;
        }
        ProviderDiagnostic(logger, operation, (int)statusCode, providerCode, schemaCategory, null);
    }

    private static string? SafeProviderCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            string? code = null;
            if (root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String)
                code = codeElement.GetString();
            else if (root.TryGetProperty("errorCode", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
                code = errorElement.GetString();
            if (string.IsNullOrWhiteSpace(code) || code.Length > 64 ||
                code.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '_' or '-' or '.')))
                return null;
            return code;
        }
        catch (JsonException) { return null; }
    }

    private sealed record OrderStatusDto(
        string? MerchantOrderId, string? OrderId, string? State, long Amount,
        IReadOnlyList<PaymentDetailDto>? PaymentDetails);
    private sealed record PaymentDetailDto(string? TransactionId, string? State, long Amount);

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        if (source.Content is not null)
        {
            var bytes = await source.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in source.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("CONFIGURE_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{key} must be configured through environment variables.");
        return value;
    }

}

public sealed class PhonePeAccessTokenCache : IDisposable
{
    private readonly ConcurrentDictionary<string, TokenPartition> partitions = new(StringComparer.Ordinal);
    internal TokenPartition For(string environment, string clientId, string clientVersion, string clientSecret)
    {
        // Fingerprint credentials without retaining raw secrets as dictionary keys or logging the key.
        var key = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { environment, clientId, clientVersion, clientSecret })));
        return partitions.GetOrAdd(key, _ => new());
    }
    public void Dispose()
    {
        foreach (var partition in partitions.Values) partition.Dispose();
        partitions.Clear();
    }
    internal sealed class TokenPartition : IDisposable
    {
        internal SemaphoreSlim Lock { get; } = new(1, 1);
        internal string? Token { get; set; }
        internal DateTime ExpiresAtUtc { get; set; }
        public void Dispose() => Lock.Dispose();
    }
}
