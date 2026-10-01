using System.Net;
using System.Globalization;
using System.Text.Json;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Domain.Entities;
using JobPortal.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class BrevoEmailServiceTests
{
    [Theory]
    [InlineData(null, "Create your CareerHarbor password", "create a password")]
    [InlineData("existing-hash", "Reset your Career Portal password", "reset your Career Portal password")]
    public async Task PasswordEmailUsesSetupOrResetWording(string? hash, string subject, string wording)
    {
        string? payload = null;
        var logger = new CollectingLogger<BrevoEmailService>();
        var service = CreateService(new DelegateHandler(async (request, ct) =>
        {
            payload = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.Created);
        }), logger, "test-key");
        var result = await service.SendPasswordResetAsync(new User
        {
            Email = "user@example.test", FirstName = "User", PasswordHash = hash
        }, "secret-setup-token");
        Assert.Equal(EmailDeliveryResult.Sent, result);
        using var json = JsonDocument.Parse(payload!);
        Assert.Equal(subject, json.RootElement.GetProperty("subject").GetString());
        Assert.Contains(wording, json.RootElement.GetProperty("textContent").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, x => x.Contains("secret-setup-token", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(HttpStatusCode.Created, EmailDeliveryResult.Sent)]
    [InlineData(HttpStatusCode.BadRequest, EmailDeliveryResult.PermanentFailure)]
    [InlineData(HttpStatusCode.TooManyRequests, EmailDeliveryResult.Failed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, EmailDeliveryResult.Failed)]
    public async Task NotificationUsesExistingSenderRegisteredRecipientAndPlaintext(HttpStatusCode status, EmailDeliveryResult expected)
    {
        string? payload = null;
        var logger = new CollectingLogger<BrevoEmailService>();
        var service = CreateService(new DelegateHandler(async (request, ct) =>
        { payload = await request.Content!.ReadAsStringAsync(ct); return new(status); }), logger, "test-only-key");
        var user = new User { Email = "registered@example.test" };
        var result = await service.SendNotificationAsync(user, new Notification { UserId = user.Id, Title = "Reminder\r\nSubject", Message = "Plain <b>text</b>", ActionUrl = "/dashboard/referrals" });
        Assert.Equal(expected, result);
        using var json = JsonDocument.Parse(payload!);
        Assert.Equal("Career Harbor", json.RootElement.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal("no-reply@careerharbor.in", json.RootElement.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal(user.Email, json.RootElement.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.False(json.RootElement.TryGetProperty("htmlContent", out _));
        Assert.DoesNotContain("\r", json.RootElement.GetProperty("subject").GetString());
        Assert.DoesNotContain(logger.Messages, x => x.Contains(user.Email, StringComparison.Ordinal) || x.Contains("Plain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotificationRejectsMismatchedRecipientAndUnsafeRouteWithoutHttp()
    {
        var service = CreateService(new DelegateHandler((_, _) => throw new InvalidOperationException("Must not send")), new CollectingLogger<BrevoEmailService>(), "test-only-key");
        var user = new User();
        Assert.Equal(EmailDeliveryResult.PermanentFailure, await service.SendNotificationAsync(user, new() { UserId = Guid.NewGuid() }));
        Assert.Equal(EmailDeliveryResult.PermanentFailure, await service.SendNotificationAsync(user, new() { UserId = user.Id, ActionUrl = "https://evil.example" }));
    }
    [Fact]
    public void PasswordResetUrlIsTokenOnlyAndProductionSafe()
    {
        const string token = "token/with+reserved=characters";
        var result = BrevoEmailService.BuildPasswordResetUrl(
            "https://careerharbor.in/", token);

        Assert.NotNull(result);
        Assert.Equal(
            "https://careerharbor.in/reset-password?token=token%2Fwith%2Breserved%3Dcharacters",
            result.AbsoluteUri);
        Assert.DoesNotContain("email=", result.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Null(BrevoEmailService.BuildPasswordResetUrl("javascript:alert(1)", token));
    }

    [Fact]
    public async Task SuccessfulPasswordResetMapsBrevoRequestAndKeepsSecretsOutOfLogs()
    {
        const string apiKey = "brevo-secret-api-key";
        const string token = "raw-password-reset-token";
        const string recipient = "candidate@example.test";
        HttpRequestMessage? captured = null;
        string? capturedJson = null;
        var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            captured = request;
            capturedJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.Created);
        });
        var logger = new CollectingLogger<BrevoEmailService>();
        var service = CreateService(handler, logger, apiKey);

        var result = await service.SendPasswordResetAsync(
            new User { Email = recipient, FirstName = "Casey" }, token);

        Assert.Equal(EmailDeliveryResult.Sent, result);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", captured!.RequestUri!.AbsoluteUri);
        Assert.Equal(apiKey, Assert.Single(captured.Headers.GetValues("api-key")));
        using var json = JsonDocument.Parse(capturedJson!);
        var root = json.RootElement;
        Assert.Equal("Career Harbor", root.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal("no-reply@careerharbor.in", root.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal(recipient, root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Contains("https://careerharbor.in/reset-password?token=", root.GetProperty("textContent").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains(apiKey, StringComparison.Ordinal) ||
            message.Contains(token, StringComparison.Ordinal) ||
            message.Contains(recipient, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task NonSuccessResponseIsHandledAndLoggedSafely(HttpStatusCode statusCode)
    {
        const string apiKey = "brevo-secret-api-key";
        const string token = "raw-password-reset-token";
        var logger = new CollectingLogger<BrevoEmailService>();
        var service = CreateService(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode))), logger, apiKey);

        var result = await service.SendPasswordResetAsync(
            new User { Email = "candidate@example.test", FirstName = "Casey" }, token);

        Assert.Equal(EmailDeliveryResult.Failed, result);
        Assert.Contains(logger.Messages, message => message.Contains(
            ((int)statusCode).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains(apiKey, StringComparison.Ordinal) || message.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TimeoutIsHandledAndLoggedSafely()
    {
        const string apiKey = "brevo-secret-api-key";
        const string token = "raw-password-reset-token";
        var logger = new CollectingLogger<BrevoEmailService>();
        var service = CreateService(new DelegateHandler((_, _) =>
            throw new TaskCanceledException("simulated timeout")), logger, apiKey);

        var result = await service.SendPasswordResetAsync(
            new User { Email = "candidate@example.test", FirstName = "Casey" }, token);

        Assert.Equal(EmailDeliveryResult.Failed, result);
        Assert.Contains(logger.Messages, message => message.Contains("password-reset", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains(apiKey, StringComparison.Ordinal) || message.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisabledDeliveryDoesNotCallBrevo()
    {
        var called = false;
        var handler = new DelegateHandler((_, _) =>
        {
            called = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        });
        var logger = new CollectingLogger<BrevoEmailService>();
        var service = CreateService(handler, logger, "unused", enabled: false);

        var result = await service.SendPasswordResetAsync(
            new User { Email = "candidate@example.test", FirstName = "Casey" }, "token");

        Assert.Equal(EmailDeliveryResult.Disabled, result);
        Assert.False(called);
    }

    private static BrevoEmailService CreateService(
        HttpMessageHandler handler, CollectingLogger<BrevoEmailService> logger,
        string apiKey, bool enabled = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Email:Enabled"] = enabled.ToString(),
                ["Email:FromName"] = "Career Harbor",
                ["Email:FromAddress"] = "no-reply@careerharbor.in",
                ["Email:Brevo:ApiKey"] = apiKey,
                ["AppUrls:FrontendBaseUrl"] = "https://careerharbor.in"
            }).Build();
        var client = new HttpClient(handler) { BaseAddress = new("https://api.brevo.com/") };
        return new(configuration, new HttpClientFactoryFake(client), logger);
    }

    private sealed class HttpClientFactoryFake(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(BrevoEmailService.HttpClientName, name);
            return client;
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
