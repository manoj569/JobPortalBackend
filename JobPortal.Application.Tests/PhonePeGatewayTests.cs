using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.Payments;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PhonePeGatewayTests
{
    [Fact]
    public async Task OAuthTokenIsSharedAndCheckoutRequestDoesNotLeakCredentials()
    {
        var handler = new RecordingHandler();
        using var cache = new PhonePeAccessTokenCache();
        using var firstClient = Client(handler);
        using var secondClient = Client(handler);
        var first = new PhonePeGateway(firstClient, Configuration(), TimeProvider.System, cache, NullLogger<PhonePeGateway>.Instance);
        var second = new PhonePeGateway(secondClient, Configuration(), TimeProvider.System, cache, NullLogger<PhonePeGateway>.Instance);
        await first.CreateCheckoutAsync("ch_first", 9900);
        await second.CreateCheckoutAsync("ch_second", 9900);
        Assert.Equal(1, handler.TokenCalls);
        Assert.Equal(2, handler.CheckoutCalls);
        Assert.All(handler.CheckoutBodies, body =>
        {
            Assert.DoesNotContain("test-client-secret", body, StringComparison.Ordinal);
            Assert.DoesNotContain("webhook-password", body, StringComparison.Ordinal);
            Assert.Contains("9900", body, StringComparison.Ordinal);
        });
        Assert.All(handler.AuthorizationHeaders, value => Assert.StartsWith("O-Bearer ", value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckoutUsesCanonicalEncodedFrontendReturnUrl()
    {
        var handler = new RecordingHandler();
        using var cache = new PhonePeAccessTokenCache();
        using var client = Client(handler);
        var gateway = new PhonePeGateway(client, Configuration(), TimeProvider.System, cache,
            NullLogger<PhonePeGateway>.Instance);

        await gateway.CreateCheckoutAsync("ch_order/value + 1", 9900);

        using var request = JsonDocument.Parse(Assert.Single(handler.CheckoutBodies));
        var redirectUrl = request.RootElement.GetProperty("paymentFlow")
            .GetProperty("merchantUrls").GetProperty("redirectUrl").GetString();
        Assert.Equal(
            "https://career-harbor.example/payment/phonepe/return?merchantOrderId=ch_order%2Fvalue%20%2B%201",
            redirectUrl);
        Assert.DoesNotContain("status", redirectUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("amount", redirectUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", redirectUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckoutSafelyCarriesEncodedInterviewInsightsReturnPath()
    {
        var handler = new RecordingHandler();
        using var cache = new PhonePeAccessTokenCache();
        using var client = Client(handler);
        var gateway = new PhonePeGateway(client, Configuration(), TimeProvider.System, cache,
            NullLogger<PhonePeGateway>.Instance);

        await gateway.CreateCheckoutAsync("ch_return", 9900, PaymentReturnPath.InterviewInsights);

        using var request = JsonDocument.Parse(Assert.Single(handler.CheckoutBodies));
        var redirectUrl = request.RootElement.GetProperty("paymentFlow")
            .GetProperty("merchantUrls").GetProperty("redirectUrl").GetString();
        Assert.Equal("https://career-harbor.example/payment/phonepe/return?merchantOrderId=ch_return&returnTo=%2Fdashboard%2Finterview-insights", redirectUrl);
    }

    [Fact]
    public void WebhookAuthorizationUsesPhonePeSha256CredentialContract()
    {
        using var cache = new PhonePeAccessTokenCache();
        using var client = Client(new RecordingHandler());
        var gateway = new PhonePeGateway(client, Configuration(), TimeProvider.System, cache, NullLogger<PhonePeGateway>.Instance);
        var expected = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("webhook-user:webhook-password"))).ToLowerInvariant();
        Assert.True(gateway.VerifyWebhookAuthorization(expected));
        Assert.False(gateway.VerifyWebhookAuthorization(new string('0', 64)));
    }

    [Fact]
    public async Task SandboxPendingStatusUsesMerchantPathAndDoesNotConfuseProviderOrderId()
    {
        const string body = """
            {"merchantId":"PGTEST","orderId":"OMO2408241234567890123456","state":"PENDING",
             "amount":9900,"payableAmount":9900,"expireAt":1787558400000,"paymentDetails":[]}
            """;
        var fixture = StatusGateway(HttpStatusCode.OK, body);

        var result = await fixture.Gateway.GetOrderStatusAsync("ch_membership_1");

        Assert.Equal(PhonePeOrderStateKind.Pending, result.State);
        Assert.Equal("ch_membership_1", result.MerchantOrderId);
        Assert.Equal(9900, result.AmountInMinorUnits);
        Assert.Equal("/apis/pg-sandbox/checkout/v2/order/ch_membership_1/status", fixture.Handler.StatusPath);
        Assert.DoesNotContain("details=", fixture.Handler.StatusPath, StringComparison.Ordinal);
        Assert.Equal("O-Bearer sandbox-token", fixture.Handler.StatusAuthorization);
    }

    [Fact]
    public async Task SandboxCompletedStatusExtractsCompletedTransaction()
    {
        const string body = """
            {"merchantId":"PGTEST","merchantOrderId":"ch_membership_2","orderId":"OMO2408242",
             "state":"COMPLETED","amount":9900,"payableAmount":9900,
             "paymentDetails":[{"transactionId":"T240824AUTH","state":"PENDING","amount":9900},
             {"transactionId":"T240824CAPTURED","state":"COMPLETED","amount":9900}]}
            """;
        var fixture = StatusGateway(HttpStatusCode.OK, body);

        var result = await fixture.Gateway.GetOrderStatusAsync("ch_membership_2");

        Assert.Equal(PhonePeOrderStateKind.Completed, result.State);
        Assert.Equal("T240824CAPTURED", result.TransactionId);
        Assert.Equal(9900, result.AmountInMinorUnits);
    }

    [Theory]
    [InlineData("FAILED", PhonePeOrderStateKind.Failed)]
    [InlineData("EXPIRED", PhonePeOrderStateKind.Failed)]
    [InlineData("CANCELLED", PhonePeOrderStateKind.Cancelled)]
    public async Task SandboxTerminalStatesAreMappedSafely(
        string providerState, PhonePeOrderStateKind expected)
    {
        var fixture = StatusGateway(HttpStatusCode.OK,
            $"{{\"merchantOrderId\":\"ch_terminal\",\"orderId\":\"OMO1\",\"state\":\"{providerState}\",\"amount\":9900,\"paymentDetails\":[]}}");

        var result = await fixture.Gateway.GetOrderStatusAsync("ch_terminal");

        Assert.Equal(expected, result.State);
    }

    [Theory]
    [InlineData("not-json", "malformed_json")]
    [InlineData("{\"merchantOrderId\":\"ch_invalid\",\"state\":\"UNKNOWN\",\"amount\":9900}", "unknown_state")]
    [InlineData("{\"merchantOrderId\":\"another\",\"state\":\"PENDING\",\"amount\":9900}", "merchant_order_mismatch")]
    public async Task InvalidStatusResponseStaysRecoverableAndLogsOnlySchemaCategory(
        string body, string category)
    {
        var fixture = StatusGateway(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Gateway.GetOrderStatusAsync("ch_invalid"));

        Assert.Equal("payment_provider_invalid_response", error.Code);
        Assert.Contains(category, fixture.Logger.Messages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonSuccessStatusLogsSafeCategoryWithoutProviderPayloadOrSecrets()
    {
        const string sensitive = "do-not-log-token-or-secret";
        var fixture = StatusGateway(HttpStatusCode.BadGateway,
            $"{{\"code\":\"INTERNAL_SERVER_ERROR\",\"accessToken\":\"{sensitive}\"}}");

        var error = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Gateway.GetOrderStatusAsync("ch_http_error"));

        Assert.Equal("payment_verification_unavailable", error.Code);
        Assert.Contains("GetOrderStatus", fixture.Logger.Messages, StringComparison.Ordinal);
        Assert.Contains("502", fixture.Logger.Messages, StringComparison.Ordinal);
        Assert.Contains("INTERNAL_SERVER_ERROR", fixture.Logger.Messages, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitive, fixture.Logger.Messages, StringComparison.Ordinal);
        Assert.DoesNotContain("sandbox-token", fixture.Logger.Messages, StringComparison.Ordinal);
        Assert.DoesNotContain("test-client-secret", fixture.Logger.Messages, StringComparison.Ordinal);
    }

    private static StatusFixture StatusGateway(HttpStatusCode status, string body)
    {
        var handler = new StatusHandler(status, body);
        var logger = new CollectingLogger<PhonePeGateway>();
        var cache = new PhonePeAccessTokenCache();
        return new(new PhonePeGateway(Client(handler), Configuration(), TimeProvider.System, cache, logger),
            handler, logger, cache);
    }

    private static HttpClient Client(HttpMessageHandler handler) => new(handler, disposeHandler: false)
    {
        BaseAddress = new Uri("https://api-preprod.phonepe.com/apis/pg-sandbox/"),
        Timeout = TimeSpan.FromSeconds(2)
    };

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PhonePe:Environment"] = "Sandbox",
            ["PhonePe:ClientId"] = "test-client-id",
            ["PhonePe:ClientSecret"] = "test-client-secret",
            ["PhonePe:ClientVersion"] = "1",
            ["PhonePe:WebhookUsername"] = "webhook-user",
            ["PhonePe:WebhookPassword"] = "webhook-password",
            ["PhonePe:RedirectBaseUrl"] = "https://career-harbor.example"
        }).Build();

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int TokenCalls { get; private set; }
        public int CheckoutCalls { get; private set; }
        public List<string> CheckoutBodies { get; } = [];
        public List<string> AuthorizationHeaders { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/v1/oauth/token", StringComparison.Ordinal))
            {
                TokenCalls++;
                return Json(HttpStatusCode.OK,
                    $"{{\"access_token\":\"sandbox-token\",\"expires_at\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}");
            }
            CheckoutCalls++;
            CheckoutBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
            return Json(HttpStatusCode.OK, "{\"redirectUrl\":\"https://mercury.phonepe.com/checkout/test\"}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StatusHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string StatusPath { get; private set; } = string.Empty;
        public string StatusAuthorization { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/v1/oauth/token", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK,
                    $"{{\"access_token\":\"sandbox-token\",\"expires_at\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"));
            StatusPath = request.RequestUri.PathAndQuery;
            StatusAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            return Task.FromResult(Json(status, body));
        }

        private static HttpResponseMessage Json(HttpStatusCode responseStatus, string responseBody) =>
            new(responseStatus) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public string Messages { get; private set; } = string.Empty;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages += formatter(state, exception) + Environment.NewLine;
    }

    private sealed record StatusFixture(
        PhonePeGateway Gateway, StatusHandler Handler,
        CollectingLogger<PhonePeGateway> Logger, PhonePeAccessTokenCache Cache) : IDisposable
    {
        public void Dispose() => Cache.Dispose();
    }
}
