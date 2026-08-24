using System.Net;
using System.Security.Cryptography;
using System.Text;
using JobPortal.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
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
        var first = new PhonePeGateway(firstClient, Configuration(), TimeProvider.System, cache);
        var second = new PhonePeGateway(secondClient, Configuration(), TimeProvider.System, cache);
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
    public void WebhookAuthorizationUsesPhonePeSha256CredentialContract()
    {
        using var cache = new PhonePeAccessTokenCache();
        using var client = Client(new RecordingHandler());
        var gateway = new PhonePeGateway(client, Configuration(), TimeProvider.System, cache);
        var expected = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("webhook-user:webhook-password"))).ToLowerInvariant();
        Assert.True(gateway.VerifyWebhookAuthorization(expected));
        Assert.False(gateway.VerifyWebhookAuthorization(new string('0', 64)));
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
}
