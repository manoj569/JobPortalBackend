using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class NotificationDispatcherTests
{
    [Theory]
    [InlineData(true, NotificationDeliveryStatus.Sent, 1)]
    [InlineData(false, NotificationDeliveryStatus.Cancelled, 0)]
    public async Task ReferralEmailRequiresVerifiedAccountAndSuccessfulRetryDoesNotResend(bool verified, NotificationDeliveryStatus status, int calls)
    {
        var store = new Store(NotificationChannel.Email) { Source = NotificationSource.ReferralAccepted };
        store.User.EmailConfirmed = verified;
        var sender = new Email();
        var processor = Processor(store, sender);
        await processor.ProcessOneAsync(default);
        Assert.Equal(status, store.Status);
        Assert.False(await processor.ProcessOneAsync(default));
        Assert.Equal(calls, sender.Calls);
        Assert.NotNull(store.Inbox);
    }

    [Fact]
    public async Task AdminEmailCanBeDisabledWithoutAffectingInbox()
    {
        var store = new Store(NotificationChannel.Email) { Source = NotificationSource.ReferralJobSubmitted };
        store.User.EmailConfirmed = true;
        var sender = new Email();
        var processor = new NotificationDispatcher(store, sender, new Realtime(), new Clock(), Options.Create(new NotificationDeliveryOptions()),
            Options.Create(new JobPortal.Application.Features.Referrals.ReferralNotificationOptions { AdminEmailEnabled = false }));
        await processor.ProcessOneAsync(default);
        Assert.Equal("admin_email_disabled", store.Code);
        Assert.Equal(0, sender.Calls);
        Assert.NotNull(store.Inbox);
    }

    [Fact]
    public async Task AdministratorEmailRequiresConfiguredApprovalPageAndKeepsInbox()
    {
        var store = new Store(NotificationChannel.Email) { Source = NotificationSource.ReferralJobSubmitted };
        store.User.EmailConfirmed = true;
        var sender = new Email();
        await Processor(store, sender).ProcessOneAsync(default);
        Assert.Equal("admin_route_missing", store.Code);
        Assert.Equal(0, sender.Calls);
        Assert.NotNull(store.Inbox);
    }

    [Fact]
    public async Task AdministratorEmailWithConfiguredPageAndVerifiedAccountUsesExistingSender()
    {
        var store = new Store(NotificationChannel.Email)
        { Source = NotificationSource.ReferralJobSubmitted, ActionUrl = "/admin/referrals" };
        store.User.EmailConfirmed = true;
        var sender = new Email();
        await Processor(store, sender).ProcessOneAsync(default);
        Assert.Equal(NotificationDeliveryStatus.Sent, store.Status);
        Assert.Equal(1, sender.Calls);
    }
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(EmailDeliveryResult.Sent, 1, NotificationDeliveryStatus.Sent)]
    [InlineData(EmailDeliveryResult.Failed, 1, NotificationDeliveryStatus.Pending)]
    [InlineData(EmailDeliveryResult.Failed, 5, NotificationDeliveryStatus.Failed)]
    [InlineData(EmailDeliveryResult.Disabled, 5, NotificationDeliveryStatus.Failed)]
    [InlineData(EmailDeliveryResult.PermanentFailure, 1, NotificationDeliveryStatus.Failed)]
    public async Task EmailUsesRegisteredRecipientAndBoundedFailurePolicy(EmailDeliveryResult result, int attempts, NotificationDeliveryStatus expected)
    {
        var store = new Store(NotificationChannel.Email) { Attempts = attempts };
        var sender = new Email { Result = result };
        var processor = Processor(store, sender);
        Assert.True(await processor.ProcessOneAsync(default));
        Assert.Equal(store.User.Id, sender.Recipient!.Id);
        Assert.Equal("registered@example.test", sender.Recipient.Email);
        Assert.Equal(expected, store.Status);
        Assert.NotNull(store.Inbox);
        if (expected == NotificationDeliveryStatus.Pending) Assert.Equal(Now.AddMinutes(1), store.NextAttempt);
    }

    [Fact]
    public async Task OfflineRealtimeDoesNotUndoCommittedInboxOrRetryInApp()
    {
        var store = new Store(NotificationChannel.InApp);
        var realtime = new Realtime { Throw = true };
        var sender = new Email();
        var processor = Processor(store, sender, realtime);
        await processor.ProcessOneAsync(default);
        Assert.Equal(NotificationDeliveryStatus.Sent, store.Status);
        Assert.NotNull(store.Inbox);
        Assert.False(await processor.ProcessOneAsync(default));
        Assert.Equal(1, realtime.Calls);
        Assert.Null(sender.Recipient);
    }

    [Theory]
    [InlineData(NotificationChannel.InApp)]
    [InlineData(NotificationChannel.Email)]
    public async Task StaleSourceAfterClaimIsCancelledWithoutExternalDelivery(NotificationChannel channel)
    {
        var store = new Store(channel) { Eligible = false };
        var email = new Email(); var realtime = new Realtime();
        await Processor(store, email, realtime).ProcessOneAsync(default);
        Assert.Equal(NotificationDeliveryStatus.Cancelled, store.Status);
        Assert.Null(email.Recipient); Assert.Equal(0, realtime.Calls);
    }

    [Fact]
    public async Task CancellationBetweenRecipientLookupAndSendIsRevalidated()
    {
        var store = new Store(NotificationChannel.Email) { CancelOnRecipientLookup = true };
        var email = new Email();
        await Processor(store, email).ProcessOneAsync(default);
        Assert.Equal(NotificationDeliveryStatus.Cancelled, store.Status);
        Assert.Null(email.Recipient);
    }

    [Fact]
    public async Task ProviderExceptionIsSanitizedAndInboxSurvives()
    {
        var store = new Store(NotificationChannel.Email);
        await Processor(store, new Email { Throw = true }).ProcessOneAsync(default);
        Assert.Equal("delivery_failed", store.Code);
        Assert.Equal(NotificationDeliveryStatus.Pending, store.Status);
        Assert.NotNull(store.Inbox);
    }

    [Fact]
    public void BackoffAndConfigurationAreBounded()
    {
        var settings = new NotificationDeliveryOptions();
        Assert.True(settings.IsValid());
        Assert.Equal(TimeSpan.FromSeconds(60), settings.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(120), settings.Backoff(2));
        Assert.True(settings.Backoff(int.MaxValue) <= TimeSpan.FromDays(1));
        settings.LeaseSeconds = 40; Assert.False(settings.IsValid());
        settings.LeaseSeconds = 180; settings.BatchSize = 101; Assert.False(settings.IsValid());
    }

    private static NotificationDispatcher Processor(Store store, Email email, Realtime? realtime = null) =>
        new(store, email, realtime ?? new(), new Clock(), Options.Create(new NotificationDeliveryOptions()));
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class Realtime : INotificationRealtime
    {
        public bool Throw { get; init; }
        public int Calls { get; private set; }
        public Task PublishAsync(Notification notification, CancellationToken ct)
        { Calls++; return Throw ? Task.FromException(new InvalidOperationException("offline")) : Task.CompletedTask; }
    }
    private sealed class Email : IEmailService
    {
        public EmailDeliveryResult Result { get; init; } = EmailDeliveryResult.Sent;
        public bool Throw { get; init; }
        public User? Recipient { get; private set; }
        public int Calls { get; private set; }
        public Task<EmailDeliveryResult> SendNotificationAsync(User user, Notification notification, CancellationToken cancellationToken)
        { Calls++; Recipient = user; return Throw ? Task.FromException<EmailDeliveryResult>(new InvalidOperationException("secret provider response")) : Task.FromResult(Result); }
        public Task<EmailDeliveryResult> SendPasswordResetAsync(User user, string rawToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EmailDeliveryResult> SendApplicationStatusAsync(User user, string jobTitle, JobApplicationStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EmailDeliveryResult> SendRegistrationVerificationAsync(User user, string rawToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Store : INotificationDeliveryRepository
    {
        public User User { get; } = new() { Email = "registered@example.test", Status = UserStatus.Active };
        public Notification? Inbox { get; private set; }
        private readonly NotificationChannel channel;
        private bool claimed;
        public int Attempts { get; init; } = 1;
        public NotificationSource Source { get; init; }
        public string? ActionUrl { get; init; }
        public bool Eligible { get; set; } = true;
        public bool CancelOnRecipientLookup { get; init; }
        public NotificationDeliveryStatus Status { get; private set; }
        public DateTime NextAttempt { get; private set; }
        public string? Code { get; private set; }
        public Store(NotificationChannel channel)
        { this.channel = channel; Inbox = new() { UserId = User.Id, Title = "Server template", Message = "Safe body" }; }
        public Task<NotificationDelivery?> ClaimAsync(DateTime now, NotificationDeliveryOptions options, CancellationToken ct)
        {
            if (claimed) return Task.FromResult<NotificationDelivery?>(null);
            claimed = true;
            return Task.FromResult<NotificationDelivery?>(new() { UserId = User.Id, NotificationId = Inbox!.Id, Source = Source, ActionUrl = ActionUrl, Channel = channel, AttemptCount = Attempts });
        }
        public Task<bool> IsEligibleAsync(NotificationDelivery delivery, DateTime now, CancellationToken ct) => Task.FromResult(Eligible);
        public Task<User?> RecipientAsync(Guid userId, CancellationToken ct)
        { if (CancelOnRecipientLookup) Eligible = false; return Task.FromResult<User?>(User); }
        public Task<Notification?> MaterializeAsync(NotificationDelivery delivery, DateTime now, CancellationToken ct)
        { Status = NotificationDeliveryStatus.Sent; return Task.FromResult(Inbox); }
        public Task<Notification?> InboxAsync(Guid id, Guid userId, CancellationToken ct) => Task.FromResult(Inbox);
        public Task CompleteAsync(NotificationDelivery delivery, NotificationDeliveryStatus status, DateTime now, DateTime nextAttempt, string? failureCode, CancellationToken ct)
        { Status = status; NextAttempt = nextAttempt; Code = failureCode; return Task.CompletedTask; }
    }
}
