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
