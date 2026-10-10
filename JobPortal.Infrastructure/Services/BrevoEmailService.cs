using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using JobPortal.Application.Features.Referrals;
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
        var isPasswordSetup = string.IsNullOrWhiteSpace(user.PasswordHash);
        return SendAsync(
            user.Email,
            isPasswordSetup ? "Create your CareerHarbor password" : "Reset your Career Portal password",
            $"Hello {safeFirstName},{Environment.NewLine}{Environment.NewLine}" +
            (isPasswordSetup
                ? "You requested password access for your CareerHarbor account. Use the secure link below to create a password. "
                : "Use the secure link below to reset your Career Portal password. ") +
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
        CancellationToken cancellationToken, bool classifyFailure = false, string? html = null, Guid? idempotencyKey = null)
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
                [new(recipient)], subject, body, html,
                idempotencyKey.HasValue ? new Dictionary<string, string> { ["idempotencyKey"] = idempotencyKey.Value.ToString("D") } : null));

            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
                return EmailDeliveryResult.Sent;
            if (idempotencyKey.HasValue && response.StatusCode == HttpStatusCode.BadRequest &&
                await IsIdempotencyDuplicateAsync(response, cancellationToken)) return EmailDeliveryResult.Sent;

            DeliveryFailed(logger, messageType, (int)response.StatusCode, CorrelationId, null);
            if (classifyFailure && (int)response.StatusCode is >= 400 and < 500 &&
                response.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))
                return EmailDeliveryResult.PermanentFailure;
            return EmailDeliveryResult.Failed;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            DeliveryFailed(logger, messageType, null, CorrelationId, classifyFailure ? null : exception);
            return EmailDeliveryResult.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DeliveryFailed(logger, messageType, null, CorrelationId, classifyFailure ? null : exception);
            return EmailDeliveryResult.Failed;
        }
    }

    private static string SanitizeHeaderValue(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    public Task<EmailDeliveryResult> SendNotificationAsync(User user, Notification notification,
        CancellationToken cancellationToken = default)
    {
        if (notification.UserId != user.Id ||
            !JobPortal.Application.Features.Notifications.NotificationOutbox.IsSafeActionUrl(notification.ActionUrl))
            return Task.FromResult(EmailDeliveryResult.PermanentFailure);
        var referral = ReferralNotifications.IsReferral(notification);
        if (referral && (!user.EmailConfirmed || notification.ActionUrl is null)) return Task.FromResult(EmailDeliveryResult.PermanentFailure);
        var body = notification.Message;
        Uri? action = null;
        if (notification.ActionUrl is { } route)
        {
            if (!Uri.TryCreate(configuration["AppUrls:FrontendBaseUrl"], UriKind.Absolute, out var frontend) ||
                frontend.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(frontend.UserInfo))
                return Task.FromResult(EmailDeliveryResult.PermanentFailure);
            var link = new UriBuilder(frontend) { Path = frontend.AbsolutePath.TrimEnd('/') + route, Query = "", Fragment = "" };
            action = link.Uri;
            body += $"{Environment.NewLine}{Environment.NewLine}{link.Uri.AbsoluteUri}";
        }
        return SendAsync(user.Email, SanitizeHeaderValue(notification.Title), body, "notification", cancellationToken, true,
            referral ? ReferralHtml(notification, action) : null, referral ? notification.Id : null);
    }

    private static string ReferralHtml(Notification notification, Uri? action)
    {
        var title = WebUtility.HtmlEncode(notification.Title);
        var message = WebUtility.HtmlEncode(notification.Message);
        var cta = action is null ? string.Empty : $"<p style=\"margin:28px 0\"><a href=\"{WebUtility.HtmlEncode(action.AbsoluteUri)}\" style=\"background:#155e75;color:#fff;padding:14px 22px;text-decoration:none;border-radius:6px;display:inline-block\">Sign in to CareerHarbor</a></p>";
        return $"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;background:#f1f5f9;font-family:Arial,sans-serif;color:#0f172a">
            <table role="presentation" style="width:100%;border:0"><tr><td style="padding:24px 12px">
            <div style="max-width:600px;margin:auto;background:#fff;padding:28px;border-radius:10px">
            <p style="color:#155e75;font-size:22px;font-weight:bold">CareerHarbor</p>
            <h1 style="font-size:24px">{title}</h1><p style="line-height:1.6">{message}</p>{cta}
            <p style="font-size:12px;color:#64748b;border-top:1px solid #e2e8f0;padding-top:20px">CareerHarbor · This update concerns your referral activity. Sign in to your account to review details.</p>
            </div></td></tr></table></body></html>
            """;
    }

    private static async Task<bool> IsIdempotencyDuplicateAsync(HttpResponseMessage response, CancellationToken ct)
    {
        // Bounded parsing only; never log the provider's response or recipient/template data.
        await response.Content.LoadIntoBufferAsync(4096, ct);
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String &&
                code.GetString() == "duplicate_parameter" && json.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String && message.GetString()!.Contains("idempotency", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    private string CorrelationId =>
        httpContextAccessor?.HttpContext?.TraceIdentifier ??
        Activity.Current?.TraceId.ToString() ?? "unavailable";

    private sealed record BrevoEmailRequest(
        [property: JsonPropertyName("sender")] BrevoSender Sender,
        [property: JsonPropertyName("to")] IReadOnlyCollection<BrevoRecipient> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("textContent")] string TextContent,
        [property: JsonPropertyName("htmlContent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HtmlContent,
        [property: JsonPropertyName("headers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Headers);

    private sealed record BrevoSender(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email);

    private sealed record BrevoRecipient(
        [property: JsonPropertyName("email")] string Email);
}
