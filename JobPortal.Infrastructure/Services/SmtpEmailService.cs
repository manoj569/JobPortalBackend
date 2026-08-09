using System.Net;
using System.Net.Mail;
using System.Diagnostics;
using System.Text;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace JobPortal.Infrastructure.Services;

public sealed class SmtpEmailService(
    IConfiguration configuration,
    ILogger<SmtpEmailService> logger,
    IHttpContextAccessor? httpContextAccessor = null) : IEmailService
{
    private static readonly Action<ILogger, string, Exception?> DeliveryDisabled =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1001, nameof(DeliveryDisabled)),
            "Transactional email delivery is disabled; correlation ID {CorrelationId}.");

    private static readonly Action<ILogger, string, string, Exception?> DeliveryFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1002, nameof(DeliveryFailed)),
            "Transactional email delivery failed for message type {MessageType}; correlation ID {CorrelationId}.");

    private static readonly Action<ILogger, string, Exception?> PasswordResetUrlInvalid =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1003, nameof(PasswordResetUrlInvalid)),
            "Password reset email delivery failed because AppUrls:FrontendBaseUrl is invalid; correlation ID {CorrelationId}.");

    public Task<EmailDeliveryResult> SendPasswordResetAsync(
        User user,
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var configuredUrl = configuration["AppUrls:FrontendBaseUrl"];
        var resetUrl = BuildPasswordResetUrl(
            configuredUrl,
            rawToken);
        if (resetUrl is null)
        {
            PasswordResetUrlInvalid(logger, CorrelationId, null);
            return Task.FromResult(EmailDeliveryResult.Failed);
        }

        var safeFirstName = user.FirstName.Replace('\r', ' ').Replace('\n', ' ').Trim();

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

    internal static Uri? BuildPasswordResetUrl(
        string? configuredUrl,
        string rawToken)
    {
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var resetUrl) ||
            (resetUrl.Scheme != Uri.UriSchemeHttp &&
                resetUrl.Scheme != Uri.UriSchemeHttps))
            return null;

        var resetUrlBuilder = new UriBuilder(resetUrl)
        {
            Path = $"{resetUrl.AbsolutePath.TrimEnd('/')}/reset-password",
            Fragment = string.Empty
        };
        resetUrlBuilder.Query = $"token={Uri.EscapeDataString(rawToken)}";
        return resetUrlBuilder.Uri;
    }

    public Task<EmailDeliveryResult> SendApplicationStatusAsync(
        User user,
        string jobTitle,
        JobApplicationStatus status,
        CancellationToken cancellationToken = default)
    {
        var safeJobTitle = jobTitle.Replace('\r', ' ').Replace('\n', ' ').Trim();

        var statusText = status switch
        {
            JobApplicationStatus.Shortlisted => "shortlisted",
            JobApplicationStatus.Rejected => "not selected",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Only terminal review statuses are emailed.")
        };

        return SendAsync(
            user.Email,
            $"Application update - {safeJobTitle}",
            $"Hello {user.FirstName}, your application for {safeJobTitle} has been {statusText}.",
            $"application-{status.ToString().ToLowerInvariant()}",
            cancellationToken);
    }

    private async Task<EmailDeliveryResult> SendAsync(
        string recipient,
        string subject,
        string body,
        string messageType,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Email:Enabled", false))
        {
            DeliveryDisabled(logger, CorrelationId, null);
            return EmailDeliveryResult.Disabled;
        }

        try
        {
            using var client = new SmtpClient(
                configuration["Email:Smtp:Host"],
                configuration.GetValue<int>("Email:Smtp:Port"))
            {
                EnableSsl = configuration.GetValue("Email:Smtp:EnableSsl", true),
                Credentials = new NetworkCredential(
                    configuration["Email:Smtp:Username"],
                    configuration["Email:Smtp:Password"])
            };

            var fromName = configuration["Email:FromName"]!
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            var sender = new MailAddress(
                configuration["Email:FromAddress"]!,
                fromName,
                Encoding.UTF8);
            using var message = new MailMessage(sender, new MailAddress(recipient))
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };

            await client.SendMailAsync(message, cancellationToken);
            return EmailDeliveryResult.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DeliveryFailed(logger, messageType, CorrelationId, exception);
            return EmailDeliveryResult.Failed;
        }
    }

    private string CorrelationId =>
        httpContextAccessor?.HttpContext?.TraceIdentifier ??
        Activity.Current?.TraceId.ToString() ??
        "unavailable";

}
