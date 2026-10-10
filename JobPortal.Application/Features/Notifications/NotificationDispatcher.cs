using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.Notifications;

public sealed class NotificationDeliveryOptions
{
    public int PollSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 20;
    public int LeaseSeconds { get; set; } = 180;
    public int MaxAttempts { get; set; } = 5;
    public int RetrySeconds { get; set; } = 60;
    public bool IsValid() => PollSeconds is >= 1 and <= 3600 && BatchSize is >= 1 and <= 100 &&
        LeaseSeconds is >= 120 and <= 3600 && MaxAttempts is >= 1 and <= 10 && RetrySeconds is >= 1 and <= 3600;
    public TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(86400, RetrySeconds * Math.Pow(2, Math.Clamp(attempt - 1, 0, 10))));
}

public interface INotificationDeliveryRepository
{
    Task<NotificationDelivery?> ClaimAsync(DateTime now, NotificationDeliveryOptions options, CancellationToken ct);
    Task<bool> IsEligibleAsync(NotificationDelivery delivery, DateTime now, CancellationToken ct);
    Task<User?> RecipientAsync(Guid userId, CancellationToken ct);
    Task<Notification?> MaterializeAsync(NotificationDelivery delivery, DateTime now, CancellationToken ct);
    Task<Notification?> InboxAsync(Guid id, Guid userId, CancellationToken ct);
    Task CompleteAsync(NotificationDelivery delivery, NotificationDeliveryStatus status, DateTime now,
        DateTime nextAttempt, string? failureCode, CancellationToken ct);
}

public interface INotificationRealtime
{
    Task PublishAsync(Notification notification, CancellationToken ct);
}

public sealed class NotificationDispatcher(INotificationDeliveryRepository repository, IEmailService email,
    INotificationRealtime realtime, TimeProvider clock, IOptions<NotificationDeliveryOptions> options,
    IOptions<JobPortal.Application.Features.Referrals.ReferralNotificationOptions>? referralOptions = null)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.IsValid()) throw new InvalidOperationException("Invalid notification delivery settings.");
        var delivery = await repository.ClaimAsync(clock.GetUtcNow().UtcDateTime, settings, ct);
        if (delivery is null) return false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        var token = linked.Token;
        try
        {
            if (!await repository.IsEligibleAsync(delivery, clock.GetUtcNow().UtcDateTime, token))
            {
                await Finish(NotificationDeliveryStatus.Cancelled, "source_ineligible", token);
                return true;
            }
            if (delivery.Channel == NotificationChannel.InApp)
            {
                // Repository commits both inbox materialization and lease completion before realtime I/O.
                var notification = await repository.MaterializeAsync(delivery, clock.GetUtcNow().UtcDateTime, token);
                if (notification is not null)
                {
                    try { await realtime.PublishAsync(notification, token); }
                    catch (Exception) when (!ct.IsCancellationRequested) { /* Best effort; durable inbox remains authoritative. */ }
                }
            }
            else
            {
                var notification = await repository.InboxAsync(delivery.NotificationId, delivery.UserId, token);
                if (notification is null) { await Retry("inbox_not_ready", token); return true; }
                var recipient = await repository.RecipientAsync(delivery.UserId, token);
                if (recipient is null) { await Finish(NotificationDeliveryStatus.Cancelled, "recipient_ineligible", token); return true; }
                if (JobPortal.Application.Features.Referrals.ReferralNotifications.IsReferral(delivery.Source) && !recipient.EmailConfirmed)
                { await Finish(NotificationDeliveryStatus.Cancelled, "email_unverified", token); return true; }
                if (delivery.Source == NotificationSource.ReferralJobSubmitted && referralOptions?.Value.AdminEmailEnabled == false)
                { await Finish(NotificationDeliveryStatus.Cancelled, "admin_email_disabled", token); return true; }
                if (delivery.Source == NotificationSource.ReferralJobSubmitted && delivery.ActionUrl is null)
                { await Finish(NotificationDeliveryStatus.Cancelled, "admin_route_missing", token); return true; }
                // Last eligibility check immediately before the external side effect. Never hold a transaction over HTTP.
                if (!await repository.IsEligibleAsync(delivery, clock.GetUtcNow().UtcDateTime, token))
                { await Finish(NotificationDeliveryStatus.Cancelled, "source_ineligible", token); return true; }
                var result = await email.SendNotificationAsync(recipient, notification, token);
                if (result == EmailDeliveryResult.Sent) await Finish(NotificationDeliveryStatus.Sent, null, token);
                else if (result == EmailDeliveryResult.PermanentFailure)
                    await Finish(NotificationDeliveryStatus.Failed, "email_rejected", token);
                else await Retry(result == EmailDeliveryResult.Disabled ? "email_disabled" : "email_transient", token);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Do not persist/log provider exceptions or template/recipient content. A cancelled operation
            // leaves a recoverable lease if recording this bounded failure also fails.
            await Retry("delivery_failed", ct);
        }
        return true;

        Task Finish(NotificationDeliveryStatus status, string? code, CancellationToken cancellationToken) =>
            repository.CompleteAsync(delivery, status, clock.GetUtcNow().UtcDateTime, delivery.NextAttemptAtUtc, code, cancellationToken);
        Task Retry(string code, CancellationToken cancellationToken) => repository.CompleteAsync(delivery,
            delivery.AttemptCount >= settings.MaxAttempts ? NotificationDeliveryStatus.Failed : NotificationDeliveryStatus.Pending,
            clock.GetUtcNow().UtcDateTime, clock.GetUtcNow().UtcDateTime.Add(settings.Backoff(delivery.AttemptCount)), code, cancellationToken);
    }
}
