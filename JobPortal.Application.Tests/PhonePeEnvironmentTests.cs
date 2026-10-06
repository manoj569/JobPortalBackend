using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Infrastructure;
using JobPortal.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using JobPortal.Application.Common.Exceptions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PhonePeEnvironmentTests
{
    [Theory]
    [InlineData("Sandbox", "https://api-preprod.phonepe.com/apis/pg-sandbox/", "https://api-preprod.phonepe.com/apis/pg-sandbox/")]
    [InlineData("sandbox", "https://api-preprod.phonepe.com/apis/pg-sandbox/", "https://api-preprod.phonepe.com/apis/pg-sandbox/")]
    [InlineData("Production", "https://api.phonepe.com/apis/identity-manager/", "https://api.phonepe.com/apis/pg/")]
    [InlineData("pRoDuCtIoN", "https://api.phonepe.com/apis/identity-manager/", "https://api.phonepe.com/apis/pg/")]
    public async Task ActualDiRoutesOAuthCheckoutAndStatusToVerifiedEnvironment(string environment, string oauthBase, string apiBase)
    {
        var configuration = Configuration(environment);
        using var handler = new FakePhonePeHandler();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration).AddSingleton(TimeProvider.System);
        services.AddInfrastructure(configuration);
        // Same typed client registration, fake transport only. No network connection is possible.
        services.AddHttpClient<IPhonePeGateway, PhonePeGateway>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
        var gateway = provider.GetRequiredService<IPhonePeGateway>();
        await gateway.CreateCheckoutAsync("ch_fixture", 9900);
        var status = await gateway.GetOrderStatusAsync("ch_fixture");
        Assert.Equal(PhonePeOrderStateKind.Completed, status.State);
        Assert.Equal(9900, status.AmountInMinorUnits);
        var requests = handler.Requests.ToArray();
        Assert.Equal(3, requests.Length);
        Assert.Equal(oauthBase + "v1/oauth/token", requests[0].Uri);
        Assert.Equal(apiBase + "checkout/v2/pay", requests[1].Uri);
        Assert.Equal(apiBase + "checkout/v2/order/ch_fixture/status", requests[2].Uri);
        Assert.Contains("grant_type=client_credentials", requests[0].Body);
        Assert.Contains("client_version=1", requests[0].Body);
        Assert.Equal("", requests[0].Authorization);
        Assert.All(requests.Skip(1), x => Assert.Equal("O-Bearer fixture-token-1", x.Authorization));
        Assert.Contains("https://careerharbor.in/payment/phonepe/return?merchantOrderId=ch_fixture", requests[1].Body);
    }

    [Theory]
    [InlineData("Live")]
    [InlineData("Prod")]
    [InlineData("Staging")]
    [InlineData("")]
    public void InvalidEnvironmentFailsStartupAndGatewayWithoutPrintingValues(string environment)
    {
        var configuration = Configuration(environment);
        using var services = StartupServices(configuration);
        var error = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("PhonePe:Environment must be Sandbox or Production", error.Message);
        Assert.DoesNotContain("fixture-secret", error.ToString());
        using var client = new HttpClient(new FakePhonePeHandler()); using var cache = new PhonePeAccessTokenCache();
        Assert.Throws<InvalidOperationException>(() => new PhonePeGateway(client, configuration, TimeProvider.System, cache, NullLogger<PhonePeGateway>.Instance));
    }

    [Theory]
    [InlineData("ClientId")]
    [InlineData("ClientSecret")]
    [InlineData("ClientVersion")]
    [InlineData("WebhookUsername")]
    [InlineData("WebhookPassword")]
    [InlineData("RedirectBaseUrl")]
    public void ConfiguredPhonePeRequiresEveryExistingSetting(string key)
    {
        var configuration = Configuration("Production"); configuration["PhonePe:" + key] = "";
        using var services = StartupServices(configuration);
        var error = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("PhonePe:" + key, error.Message);
        Assert.DoesNotContain("fixture-secret", error.ToString());
        Assert.DoesNotContain("fixture-password", error.ToString());
    }

    [Theory]
    [InlineData("http://careerharbor.in")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:5173")]
    [InlineData("https://localhost.")]
    [InlineData("https://preview.localhost.")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://[::1]")]
    [InlineData("https://preview.localhost")]
    [InlineData("https://user:password@careerharbor.in")]
    [InlineData("https://careerharbor.in?secret=fixture-secret")]
    [InlineData("https://careerharbor.in#fragment")]
    public void ProductionRejectsUnsafeRedirectsWithoutEchoingTheirContents(string redirect)
    {
        var configuration = Configuration("Production"); configuration["PhonePe:RedirectBaseUrl"] = redirect;
        using var services = StartupServices(configuration);
        var error = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("RedirectBaseUrl", error.Message);
        Assert.DoesNotContain(redirect, error.Message);
        Assert.DoesNotContain("fixture-secret", error.Message);
    }

    [Fact]
    public void UnconfiguredPhonePeDoesNotPreventStartupAndSandboxKeepsLoopbackSupport()
    {
        using var services = StartupServices(new ConfigurationBuilder().Build());
        services.GetRequiredService<IStartupValidator>().Validate();
        var configuration = Configuration("Sandbox"); configuration["PhonePe:RedirectBaseUrl"] = "http://localhost:5173";
        using var configured = StartupServices(configuration);
        configured.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public async Task CachePartitionsEnvironmentClientVersionClientIdAndSecretButSharesIdenticalCredentials()
    {
        using var handler = new FakePhonePeHandler(); using var cache = new PhonePeAccessTokenCache();
        using var client = new HttpClient(handler) { BaseAddress = new("https://wrong-base.example/") };
        PhonePeGateway Gateway(IConfiguration configuration) => new(client, configuration, TimeProvider.System, cache, NullLogger<PhonePeGateway>.Instance);
        var sandbox = Configuration("Sandbox"); var production = Configuration("Production");
        await Gateway(sandbox).CreateCheckoutAsync("ch_one", 9900);
        await Gateway(production).CreateCheckoutAsync("ch_two", 9900);
        await Gateway(Configuration("sandbox")).CreateCheckoutAsync("ch_three", 9900);
        var changedSecret = Configuration("Production"); changedSecret["PhonePe:ClientSecret"] = "rotated-fixture-secret";
        await Gateway(changedSecret).CreateCheckoutAsync("ch_four", 9900);
        var changedId = Configuration("Production"); changedId["PhonePe:ClientId"] = "another-fixture-client";
        await Gateway(changedId).CreateCheckoutAsync("ch_five", 9900);
        var changedVersion = Configuration("Production"); changedVersion["PhonePe:ClientVersion"] = "2";
        await Gateway(changedVersion).CreateCheckoutAsync("ch_six", 9900);
        await Gateway(production).CreateCheckoutAsync("ch_seven", 9900);
        Assert.Equal(5, handler.TokenCalls);
        Assert.Equal(new[] { "O-Bearer fixture-token-1", "O-Bearer fixture-token-2", "O-Bearer fixture-token-1", "O-Bearer fixture-token-3", "O-Bearer fixture-token-4", "O-Bearer fixture-token-5", "O-Bearer fixture-token-2" },
            handler.Requests.Where(x => x.Uri.EndsWith("/pay", StringComparison.Ordinal)).Select(x => x.Authorization));
        Assert.DoesNotContain(handler.Requests, x => x.Uri.StartsWith("https://wrong-base.example", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Sandbox")]
    [InlineData("Production")]
    public void ShaWebhookAuthAndCanonicalCheckoutEventsRemainSupported(string environment)
    {
        using var client = new HttpClient(new FakePhonePeHandler()); using var cache = new PhonePeAccessTokenCache();
        var gateway = new PhonePeGateway(client, Configuration(environment), TimeProvider.System, cache, NullLogger<PhonePeGateway>.Instance);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture-user:fixture-password")));
        Assert.True(gateway.VerifyWebhookAuthorization(hash)); Assert.True(gateway.VerifyWebhookAuthorization("SHA256 " + hash.ToLowerInvariant()));
        Assert.False(gateway.VerifyWebhookAuthorization("Bearer " + hash)); Assert.False(gateway.VerifyWebhookAuthorization(new string('0', 64)));
        Assert.False(gateway.VerifyWebhookAuthorization("invalid"));
        foreach (var (name, state) in new[] { ("checkout.order.completed", "COMPLETED"), ("checkout.order.failed", "FAILED") })
        {
            var callback = gateway.ParseCallback(Encoding.UTF8.GetBytes($"{{\"event\":\"{name}\",\"payload\":{{\"merchantOrderId\":\"ch_fixture\",\"state\":\"{state}\"}}}}"));
            Assert.Equal("ch_fixture", callback.MerchantOrderId);
            Assert.Equal(state == "COMPLETED" ? PhonePeOrderStateKind.Completed : PhonePeOrderStateKind.Failed, callback.State);
        }
    }

    [Fact]
    public async Task UnauthorizedRefreshRemainsOnProductionOAuthAndApiEndpoints()
    {
        using var handler = new FakePhonePeHandler { RejectFirstCheckout = true }; using var cache = new PhonePeAccessTokenCache();
        using var client = new HttpClient(handler);
        var gateway = new PhonePeGateway(client, Configuration("Production"), TimeProvider.System, cache, NullLogger<PhonePeGateway>.Instance);
        await gateway.CreateCheckoutAsync("ch_fixture", 9900);
        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal(2, handler.Requests.Count(x => x.Uri == "https://api.phonepe.com/apis/pg/checkout/v2/pay"));
        Assert.All(handler.Requests.Where(x => x.Uri.EndsWith("/token", StringComparison.Ordinal)), x => Assert.Equal("https://api.phonepe.com/apis/identity-manager/v1/oauth/token", x.Uri));
    }

    [Fact]
    public async Task ConcurrentIdenticalCredentialsShareOneAccessToken()
    {
        using var handler = new FakePhonePeHandler(); using var cache = new PhonePeAccessTokenCache();
        using var client = new HttpClient(handler);
        var first = new PhonePeGateway(client, Configuration("Production"), TimeProvider.System, cache, NullLogger<PhonePeGateway>.Instance);
        var second = new PhonePeGateway(client, Configuration("Production"), TimeProvider.System, cache, NullLogger<PhonePeGateway>.Instance);
        await Task.WhenAll(first.CreateCheckoutAsync("ch_first", 9900), second.CreateCheckoutAsync("ch_second", 9900));
        Assert.Equal(1, handler.TokenCalls);
    }

    [Fact]
    public async Task ExpiredTokenRefreshesUsingTheConfiguredOAuthEndpoint()
    {
        var clock = new MutableClock();
        using var handler = new FakePhonePeHandler { TokenExpiresAt = clock.GetUtcNow().AddMinutes(2).ToUnixTimeSeconds() };
        using var cache = new PhonePeAccessTokenCache(); using var client = new HttpClient(handler);
        var gateway = new PhonePeGateway(client, Configuration("Production"), clock, cache, NullLogger<PhonePeGateway>.Instance);
        await gateway.CreateCheckoutAsync("ch_first", 9900);
        clock.Now = clock.Now.AddMinutes(3);
        handler.TokenExpiresAt = clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds();
        await gateway.CreateCheckoutAsync("ch_second", 9900);
        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal("O-Bearer fixture-token-2", handler.Requests.Last().Authorization);
    }

    [Theory]
    [InlineData("fixture-secret")]
    [InlineData("fixture-password")]
    [InlineData("fixture-token-1")]
    [InlineData("webhook-hash")]
    public async Task EvenProviderErrorCodesCannotLeakSecretsTokensOrWebhookAuthorization(string code)
    {
        if (code == "webhook-hash") code = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture-user:fixture-password")));
        using var handler = new FakePhonePeHandler { ErrorCode = code }; using var cache = new PhonePeAccessTokenCache();
        using var client = new HttpClient(handler); var logger = new CaptureLogger();
        var gateway = new PhonePeGateway(client, Configuration("Production"), TimeProvider.System, cache, logger);
        var error = await Assert.ThrowsAsync<AppException>(() => gateway.CreateCheckoutAsync("ch_fixture", 9900));
        Assert.Equal("payment_provider_unavailable", error.Code);
        Assert.Contains("503", logger.Messages);
        Assert.DoesNotContain(code, logger.Messages);
        Assert.DoesNotContain(code, error.ToString());
    }

    private static ServiceProvider StartupServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<PhonePeOptions>, PhonePeOptionsValidator>();
        services.AddOptions<PhonePeOptions>().Bind(configuration.GetSection("PhonePe")).ValidateOnStart();
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(string environment) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["PhonePe:Environment"] = environment, ["PhonePe:ClientId"] = "fixture-client", ["PhonePe:ClientSecret"] = "fixture-secret",
        ["PhonePe:ClientVersion"] = "1", ["PhonePe:RedirectBaseUrl"] = "https://careerharbor.in",
        ["PhonePe:WebhookUsername"] = "fixture-user", ["PhonePe:WebhookPassword"] = "fixture-password"
    }).Build();

    private sealed class FakePhonePeHandler : HttpMessageHandler
    {
        internal ConcurrentQueue<RecordedRequest> Requests { get; } = new();
        private int tokenCalls;
        internal int TokenCalls => tokenCalls;
        internal bool RejectFirstCheckout { get; init; }
        internal string? ErrorCode { get; init; }
        internal long? TokenExpiresAt { get; set; }
        private bool rejected;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.AbsoluteUri;
            Requests.Enqueue(new(uri, request.Headers.Authorization?.ToString() ?? "", request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (uri.EndsWith("/token", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, $"{{\"access_token\":\"fixture-token-{Interlocked.Increment(ref tokenCalls)}\",\"expires_at\":{TokenExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}");
            if (uri.EndsWith("/pay", StringComparison.Ordinal))
            {
                if (ErrorCode is not null) return Json(HttpStatusCode.ServiceUnavailable, System.Text.Json.JsonSerializer.Serialize(new { code = ErrorCode }));
                if (RejectFirstCheckout && !rejected) { rejected = true; return Json(HttpStatusCode.Unauthorized, "{}"); }
                return Json(HttpStatusCode.OK, "{\"redirectUrl\":\"https://mercury.phonepe.com/checkout/fixture\"}");
            }
            return Json(HttpStatusCode.OK, "{\"merchantOrderId\":\"ch_fixture\",\"state\":\"COMPLETED\",\"amount\":9900,\"paymentDetails\":[{\"state\":\"COMPLETED\",\"transactionId\":\"fixture-transaction\"}]}");
        }
        private static HttpResponseMessage Json(HttpStatusCode code, string content) => new(code) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }
    private sealed record RecordedRequest(string Uri, string Authorization, string Body);
    private sealed class MutableClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class CaptureLogger : ILogger<PhonePeGateway>
    {
        internal string Messages { get; private set; } = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages += formatter(state, exception);
    }
}
