using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.Services;

public sealed class BrevoEmailService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<BrevoEmailService> logger,
    IHttpContextAccessor? httpContextAccessor = null) : IEmailService
{
    public const string HttpClientName = "BrevoTransactionalEmail";

    private static readonly Action<ILogger, string, Exception?> DeliveryDisabled =
        LoggerMessage.Define<string>(LogLevel.Warning, new(1001, nameof(DeliveryDisabled)),
            "Transactional email delivery is disabled; correlation ID {CorrelationId}.");

    private static readonly Action<ILogger, string, int?, string, Exception?> DeliveryFailed =
        LoggerMessage.Define<string, int?, string>(LogLevel.Error, new(1002, nameof(DeliveryFailed)),
            "Transactional email delivery failed for message type {MessageType}, status code {StatusCode}; correlation ID {CorrelationId}.");

    private static readonly Action<ILogger, string, Exception?> PasswordResetUrlInvalid =
        LoggerMessage.Define<string>(LogLevel.Error, new(1003, nameof(PasswordResetUrlInvalid)),
            "Password reset email delivery failed because AppUrls:FrontendBaseUrl is invalid; correlation ID {CorrelationId}.");

    public Task<EmailDeliveryResult> SendPasswordResetAsync(
        User user, string rawToken, CancellationToken cancellationToken = default)
    {
        var resetUrl = BuildPasswordResetUrl(
            configuration["AppUrls:FrontendBaseUrl"], rawToken);
        if (resetUrl is null)
        {
            PasswordResetUrlInvalid(logger, CorrelationId, null);
            return Task.FromResult(EmailDeliveryResult.Failed);
        }

        var safeFirstName = SanitizeHeaderValue(user.FirstName);
        return SendAsync(
            user.Email,
            "Reset your Career Portal password",
            $"Hello {safeFirstName},{Environment.NewLine}{Environment.NewLine}" +
            "Use the secure link below to reset your Career Portal password. " +
            $"The link expires in 30 minutes.{Environment.NewLine}{Environment.NewLine}" +
            $"{resetUrl.AbsoluteUri}{Environment.NewLine}{Environment.NewLine}" +
            "If you did not request this change, you can ignore this email.",
            "password-reset",
            cancellationToken);
    }

    internal static Uri? BuildPasswordResetUrl(string? configuredUrl, string rawToken)
    {
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var resetUrl) ||
            (resetUrl.Scheme != Uri.UriSchemeHttp && resetUrl.Scheme != Uri.UriSchemeHttps))
            return null;

        var builder = new UriBuilder(resetUrl)
        {
            Path = $"{resetUrl.AbsolutePath.TrimEnd('/')}/reset-password",
            Query = $"token={Uri.EscapeDataString(rawToken)}",
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    public Task<EmailDeliveryResult> SendApplicationStatusAsync(
        User user, string jobTitle, JobApplicationStatus status,
        CancellationToken cancellationToken = default)
    {
        var safeJobTitle = SanitizeHeaderValue(jobTitle);
        var statusText = status switch
        {
            JobApplicationStatus.Shortlisted => "shortlisted",
            JobApplicationStatus.Rejected => "not selected",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status,
                "Only terminal review statuses are emailed.")
        };
        return SendAsync(user.Email, $"Application update - {safeJobTitle}",
            $"Hello {user.FirstName}, your application for {safeJobTitle} has been {statusText}.",
            $"application-{status.ToString().ToLowerInvariant()}", cancellationToken);
    }

    public Task<EmailDeliveryResult> SendRegistrationVerificationAsync(
        User user, string rawToken, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(configuration["AppUrls:FrontendBaseUrl"], UriKind.Absolute, out var frontend) ||
            frontend.Scheme != Uri.UriSchemeHttp && frontend.Scheme != Uri.UriSchemeHttps)
        {
            DeliveryFailed(logger, "registration-verification", null, CorrelationId, null);
            return Task.FromResult(EmailDeliveryResult.Failed);
        }
        var link = new UriBuilder(frontend)
        {
            Path = $"{frontend.AbsolutePath.TrimEnd('/')}/verify-email",
            Query = $"token={Uri.EscapeDataString(rawToken)}",
            Fragment = string.Empty
        }.Uri;
        return SendAsync(user.Email, "Verify your Career Harbor email",
            $"Hello {SanitizeHeaderValue(user.FirstName)},{Environment.NewLine}{Environment.NewLine}" +
            $"Verify your email using this link: {link.AbsoluteUri}{Environment.NewLine}" +
            "This link expires in 24 hours.", "registration-verification", cancellationToken);
    }

    private async Task<EmailDeliveryResult> SendAsync(
        string recipient, string subject, string body, string messageType,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Email:Enabled", false))
        {
            DeliveryDisabled(logger, CorrelationId, null);
            return EmailDeliveryResult.Disabled;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email");
            request.Headers.Add("api-key", configuration["Email:Brevo:ApiKey"]);
            request.Content = JsonContent.Create(new BrevoEmailRequest(
                new(SanitizeHeaderValue(configuration["Email:FromName"]!),
                    configuration["Email:FromAddress"]!),
                [new(recipient)], subject, body));

            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
                return EmailDeliveryResult.Sent;

            DeliveryFailed(logger, messageType, (int)response.StatusCode, CorrelationId, null);
            return EmailDeliveryResult.Failed;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            DeliveryFailed(logger, messageType, null, CorrelationId, exception);
            return EmailDeliveryResult.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DeliveryFailed(logger, messageType, null, CorrelationId, exception);
            return EmailDeliveryResult.Failed;
        }
    }

    private static string SanitizeHeaderValue(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private string CorrelationId =>
        httpContextAccessor?.HttpContext?.TraceIdentifier ??
        Activity.Current?.TraceId.ToString() ?? "unavailable";

    private sealed record BrevoEmailRequest(
        [property: JsonPropertyName("sender")] BrevoSender Sender,
        [property: JsonPropertyName("to")] IReadOnlyCollection<BrevoRecipient> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("textContent")] string TextContent);

    private sealed record BrevoSender(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email);

    private sealed record BrevoRecipient(
        [property: JsonPropertyName("email")] string Email);
}
