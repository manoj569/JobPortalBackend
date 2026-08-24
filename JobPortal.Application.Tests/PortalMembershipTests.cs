using System.Text;
using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PortalMembershipTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherCandidateId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ActiveMembershipGrantsJobsFromDifferentCompanies()
    {
        var repository = new FakeMembershipRepository
        {
            Membership = new Membership { UserId = UserId, Status = MembershipStatus.Active },
            Jobs =
            {
                ["first"] = new(Guid.NewGuid(), "https://example.test/first"),
                ["second"] = new(Guid.NewGuid(), "https://example.test/second")
            }
        };
        var service = new MembershipService(repository, new FakeUnitOfWork());

        var first = await service.GetApplicationAccessAsync(UserId, "first");
        var second = await service.GetApplicationAccessAsync(UserId, "second");

        Assert.Equal(ApplicationAccessStatus.Granted, first.Status);
        Assert.Equal(ApplicationAccessStatus.Granted, second.Status);
        Assert.Equal(2, repository.RecordedApplications.Count);
    }

    [Fact]
    public async Task MissingMembershipRequiresPaymentAndAnonymousUserRequiresLogin()
    {
        var repository = AvailableRepository();
        var service = new MembershipService(repository, new FakeUnitOfWork());

        Assert.Equal(ApplicationAccessStatus.LoginRequired,
            (await service.GetApplicationAccessAsync(null, "job")).Status);
        Assert.Equal(ApplicationAccessStatus.PaymentRequired,
            (await service.GetApplicationAccessAsync(UserId, "job")).Status);
    }

    [Fact]
    public async Task UnavailableJobNeverExposesApplicationUrl()
    {
        var service = new MembershipService(
            new FakeMembershipRepository
            {
                Membership = new Membership { UserId = UserId, Status = MembershipStatus.Active }
            },
            new FakeUnitOfWork());

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetApplicationAccessAsync(UserId, "hidden-archived-or-expired"));
    }

    [Fact]
    public async Task ExpiredMembershipDoesNotGrantAccess()
    {
        var repository = AvailableRepository();
        repository.Membership = new Membership
        {
            UserId = UserId,
            Status = MembershipStatus.Active,
            EndsAtUtc = Now.AddMinutes(-1)
        };
        var service = new MembershipService(repository, new FakeUnitOfWork());

        var result = await service.GetApplicationAccessAsync(UserId, "job");

        Assert.Equal(ApplicationAccessStatus.PaymentRequired, result.Status);
    }

    [Fact]
    public async Task ActiveOrPendingMembershipRejectsAnotherOrder()
    {
        var fixture = CreatePaymentFixture();
        fixture.Memberships.Membership = new Membership
        {
            UserId = UserId,
            Status = MembershipStatus.Active,
            StartsAtUtc = Now,
            EndsAtUtc = Now.AddDays(1)
        };
        await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.CreateOrderAsync(UserId, new()));

        fixture.Memberships.Membership.Status = MembershipStatus.Pending;
        await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.CreateOrderAsync(UserId, new()));
        Assert.Equal(0, fixture.Gateway.CreateOrderCalls);
    }

    [Fact]
    public async Task PurchaseRequiresActiveCandidateRole()
    {
        var fixture = CreatePaymentFixture();
        fixture.Users.IsEligible = false;

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => fixture.Service.CreateOrderAsync(UserId, new()));
        Assert.Equal(0, fixture.Gateway.CreateOrderCalls);
    }

    [Fact]
    public async Task ActiveCandidateMembershipAccessIgnoresHistoricalEmailFlag()
    {
        var fixture = CreatePaymentFixture();
        fixture.Users.EmailConfirmed = false;

        var order = await fixture.Service.CreateOrderAsync(UserId, new());
        var status = await fixture.Service.GetStatusAsync(UserId);

        Assert.Equal(9900, order.AmountInMinorUnits);
        Assert.Equal(MembershipStatus.Pending, status.Membership!.Status);
    }

    [Fact]
    public async Task OrderUsesOnlyTrustedPlanAndIsPersistedBeforeProviderCall()
    {
        var fixture = CreatePaymentFixture();
        fixture.Gateway.BeforeCreateOrder = () =>
            fixture.Payments.Payment?.Status == PaymentStatus.Created &&
            fixture.UnitOfWork.SaveCount > 0;

        var order = await fixture.Service.CreateOrderAsync(UserId, new());

        Assert.Equal(9900, fixture.Gateway.RequestedAmount);
        Assert.Equal("INR", fixture.Gateway.RequestedCurrency);
        Assert.True(fixture.Gateway.LocalOrderWasPersisted);
        Assert.Equal(9900, order.AmountInMinorUnits);
        Assert.Equal("INR", order.CurrencyCode);
        Assert.Equal(99m, fixture.Payments.Payment!.Amount);
        Assert.Equal(PaymentStatus.Pending, fixture.Payments.Payment.Status);
    }

    [Fact]
    public async Task ConfirmationEnforcesOwnershipAndStoredOrder()
    {
        var fixture = CreatePaymentFixture();
        var order = await fixture.Service.CreateOrderAsync(UserId, new());

        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.ConfirmAsync(
            OtherCandidateId, order.PaymentId,
            new(order.ProviderOrderId, "pay_1", new('a', 64))));
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.ConfirmAsync(
            UserId, order.PaymentId,
            new("order_other", "pay_1", new('a', 64))));
    }

    [Fact]
    public async Task BadSignatureDoesNotActivateMembership()
    {
        var fixture = CreatePaymentFixture();
        fixture.Gateway.PaymentSignatureIsValid = false;
        var order = await fixture.Service.CreateOrderAsync(UserId, new());

        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.ConfirmAsync(
            UserId, order.PaymentId,
            new(order.ProviderOrderId, "pay_1", new('a', 64))));

        Assert.Equal(PaymentStatus.Pending, fixture.Payments.Payment!.Status);
        Assert.NotEqual(MembershipStatus.Active, fixture.Memberships.Membership!.Status);
    }

    [Fact]
    public async Task ValidBrowserSignatureStillRequiresProviderCapture()
    {
        var fixture = CreatePaymentFixture();
        var order = await fixture.Service.CreateOrderAsync(UserId, new());
        fixture.Gateway.ReconciliationState = new(RazorpayPaymentStateKind.Pending);

        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.ConfirmAsync(
            UserId, order.PaymentId,
            new(order.ProviderOrderId, "pay_1", new('a', 64))));

        Assert.Equal(PaymentStatus.Pending, fixture.Payments.Payment!.Status);
        Assert.NotEqual(MembershipStatus.Active, fixture.Memberships.Membership!.Status);
    }

    [Fact]
    public async Task DuplicateConfirmationIsIdempotentAndActivatesOnlyThirtyDaysOnce()
    {
        var fixture = CreatePaymentFixture();
        var order = await fixture.Service.CreateOrderAsync(UserId, new());
        var request = new ConfirmRazorpayPaymentRequest(
            order.ProviderOrderId, "pay_1", new('a', 64));

        await fixture.Service.ConfirmAsync(UserId, order.PaymentId, request);
        var firstEnd = fixture.Memberships.Membership!.EndsAtUtc;
        var duplicate = await fixture.Service.ConfirmAsync(UserId, order.PaymentId, request);

        Assert.Equal(PaymentStatus.Paid, duplicate.Status);
        Assert.Equal(Now.AddDays(30), firstEnd);
        Assert.Equal(firstEnd, fixture.Memberships.Membership.EndsAtUtc);
        Assert.Single(fixture.Payments.Payment!.History, x => x.CurrentStatus == PaymentStatus.Paid);
        Assert.Single(
            fixture.Audit.Events,
            audit => audit.Action == AuditAction.Create &&
                audit.EntityType == "Payment");
        Assert.Single(
            fixture.Audit.Events,
            audit => audit.Action == AuditAction.Confirm &&
                audit.EntityType == "Payment");
        Assert.Single(
            fixture.Audit.Events,
            audit => audit.Action == AuditAction.Activate &&
                audit.EntityType == "Membership");
        var auditJson = JsonSerializer.Serialize(fixture.Audit.Events);
        Assert.DoesNotContain(request.RazorpaySignature, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(request.RazorpayPaymentId, auditJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebhookRejectsBadSignatureAndProcessesSuccessIdempotently()
    {
        var fixture = CreatePaymentFixture();
        var order = await fixture.Service.CreateOrderAsync(UserId, new());
        var body = Webhook("payment.captured", order.ProviderOrderId, "pay_webhook", "captured");
        fixture.Gateway.WebhookSignatureIsValid = false;
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.ProcessWebhookAsync(new(body, new('a', 64), "event_1")));

        fixture.Gateway.WebhookSignatureIsValid = true;
        await fixture.Service.ProcessWebhookAsync(new(body, new('b', 64), "event_1"));
        var firstEnd = fixture.Memberships.Membership!.EndsAtUtc;
        var duplicate = await fixture.Service.ProcessWebhookAsync(new(body, new('b', 64), "event_1"));

        Assert.Equal(PaymentStatus.Paid, fixture.Payments.Payment!.Status);
        Assert.Equal(firstEnd, fixture.Memberships.Membership.EndsAtUtc);
        Assert.Contains("Duplicate", duplicate.Outcome, StringComparison.Ordinal);
        Assert.Single(fixture.Payments.Payment.History, x => x.ProviderEventId == "event_1");
        Assert.Contains(
            fixture.Audit.Events,
            audit => audit.Action == AuditAction.WebhookSuccess &&
                audit.Actor?.Role == "RazorpayWebhook");
        Assert.DoesNotContain(
            "event_1",
            JsonSerializer.Serialize(fixture.Audit.Events),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedWebhookDoesNotActivateMembership()
    {
        var fixture = CreatePaymentFixture();
        var order = await fixture.Service.CreateOrderAsync(UserId, new());
        var body = Webhook("payment.failed", order.ProviderOrderId, "pay_failed", "failed");

        await fixture.Service.ProcessWebhookAsync(new(body, new('b', 64), "event_failed"));

        Assert.Equal(PaymentStatus.Failed, fixture.Payments.Payment!.Status);
        Assert.Equal(MembershipStatus.Cancelled, fixture.Memberships.Membership!.Status);
    }

    [Fact]
    public async Task ReconciliationNeverMarksPendingAsPaidWithoutProviderCapture()
    {
        var fixture = CreatePaymentFixture();
        var order = await fixture.Service.CreateOrderAsync(UserId, new());
        fixture.Gateway.ReconciliationState = new(RazorpayPaymentStateKind.Pending);

        var pending = await fixture.Service.ReconcileAsync(UserId, order.PaymentId);

        Assert.Equal(PaymentStatus.Pending, pending.Status);
        Assert.NotEqual(MembershipStatus.Active, fixture.Memberships.Membership!.Status);

        fixture.Gateway.ReconciliationState =
            new(RazorpayPaymentStateKind.Paid, "pay_reconciled", 9900, "INR");
        var paid = await fixture.Service.ReconcileAsync(UserId, order.PaymentId);
        Assert.Equal(PaymentStatus.Paid, paid.Status);
        Assert.Equal(MembershipStatus.Active, fixture.Memberships.Membership.Status);
    }

    private static byte[] Webhook(
        string eventName, string orderId, string paymentId, string status) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            @event = eventName,
            payload = new
            {
                payment = new
                {
                    entity = new
                    {
                        id = paymentId,
                        order_id = orderId,
                        amount = 9900,
                        currency = "INR",
                        status
                    }
                }
            }
        }));

    private static FakeMembershipRepository AvailableRepository() => new()
    {
        Jobs = { ["job"] = new(Guid.NewGuid(), "https://example.test/apply") }
    };

    [Fact]
    public async Task PhonePeCheckoutUsesAuthoritativePlanAndReturnsNoSecrets()
    {
        var fixture = CreatePaymentFixture();
        var checkout = await fixture.Service.CreatePhonePeCheckoutAsync(UserId);
        Assert.Equal(9900, fixture.PhonePe.RequestedAmount);
        Assert.Equal(9900, checkout.AmountInMinorUnits);
        Assert.Equal("INR", checkout.CurrencyCode);
        Assert.Equal(30, checkout.DurationDays);
        Assert.Equal(PaymentProvider.PhonePe, fixture.Payments.Payment!.Provider);
        Assert.Equal(PaymentStatus.Pending, fixture.Payments.Payment.Status);
        Assert.Equal(MembershipStatus.Pending, fixture.Memberships.Membership!.Status);
        var json = JsonSerializer.Serialize(checkout);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("webhook", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhonePeMerchantOrderIdsAreUnique()
    {
        var first = CreatePaymentFixture();
        var second = CreatePaymentFixture();
        var a = await first.Service.CreatePhonePeCheckoutAsync(UserId);
        var b = await second.Service.CreatePhonePeCheckoutAsync(UserId);
        Assert.NotEqual(a.MerchantOrderId, b.MerchantOrderId);
        Assert.StartsWith("ch_", a.MerchantOrderId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifiedCompletedPhonePeWebhookActivatesExactlyOnce()
    {
        var fixture = CreatePaymentFixture();
        var checkout = await fixture.Service.CreatePhonePeCheckoutAsync(UserId);
        fixture.PhonePe.VerificationState = new(PhonePeOrderStateKind.Completed,
            checkout.MerchantOrderId, "phonepe_txn_1", 9900);
        var request = new PhonePeWebhookRequest("{\"event\":\"pg.order.completed\"}"u8.ToArray(), "valid");
        await fixture.Service.ProcessPhonePeWebhookAsync(request);
        var endsAt = fixture.Memberships.Membership!.EndsAtUtc;
        await fixture.Service.ProcessPhonePeWebhookAsync(request);
        Assert.Equal(PaymentStatus.Paid, fixture.Payments.Payment!.Status);
        Assert.Equal(MembershipStatus.Active, fixture.Memberships.Membership.Status);
        Assert.Equal(Now.AddDays(30), endsAt);
        Assert.Equal(endsAt, fixture.Memberships.Membership.EndsAtUtc);
        Assert.Equal("phonepe_txn_1", fixture.Payments.Payment.ProviderPaymentId);
    }

    [Theory]
    [InlineData(PhonePeOrderStateKind.Failed, PaymentStatus.Failed)]
    [InlineData(PhonePeOrderStateKind.Cancelled, PaymentStatus.Cancelled)]
    public async Task FailedOrCancelledPhonePeDoesNotActivateMembership(
        PhonePeOrderStateKind providerState, PaymentStatus expected)
    {
        var fixture = CreatePaymentFixture();
        var checkout = await fixture.Service.CreatePhonePeCheckoutAsync(UserId);
        fixture.PhonePe.VerificationState = new(providerState, checkout.MerchantOrderId,
            "phonepe_txn_terminal", 9900);
        await fixture.Service.ProcessPhonePeWebhookAsync(new("{}"u8.ToArray(), "valid"));
        Assert.Equal(expected, fixture.Payments.Payment!.Status);
        Assert.NotEqual(MembershipStatus.Active, fixture.Memberships.Membership!.Status);
    }

    [Fact]
    public async Task InvalidPhonePeWebhookAuthenticationIsRejected()
    {
        var fixture = CreatePaymentFixture();
        await fixture.Service.CreatePhonePeCheckoutAsync(UserId);
        fixture.PhonePe.AuthorizationValid = false;
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.ProcessPhonePeWebhookAsync(new("{}"u8.ToArray(), "invalid")));
        Assert.Equal("invalid_webhook_authentication", error.Code);
        Assert.Equal(MembershipStatus.Pending, fixture.Memberships.Membership!.Status);
    }

    [Fact]
    public async Task PhonePeVerificationFailureNeverActivatesMembership()
    {
        var fixture = CreatePaymentFixture();
        await fixture.Service.CreatePhonePeCheckoutAsync(UserId);
        fixture.PhonePe.VerificationFails = true;
        await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.ProcessPhonePeWebhookAsync(new("{}"u8.ToArray(), "valid")));
        Assert.Equal(PaymentStatus.Pending, fixture.Payments.Payment!.Status);
        Assert.Equal(MembershipStatus.Pending, fixture.Memberships.Membership!.Status);
    }

    [Fact]
    public async Task CandidateCannotReadAnotherCandidatesPhonePeStatus()
    {
        var fixture = CreatePaymentFixture();
        var checkout = await fixture.Service.CreatePhonePeCheckoutAsync(UserId);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetPhonePeStatusAsync(OtherCandidateId, checkout.MerchantOrderId));
    }

    private static PaymentFixture CreatePaymentFixture()
    {
        var memberships = AvailableRepository();
        var payments = new FakePaymentRepository();
        var users = new FakeUserRepository();
        var gateway = new FakeRazorpayGateway();
        var phonePe = new FakePhonePeGateway();
        var unitOfWork = new FakeUnitOfWork();
        var audit = new AuditWriterTestDouble();
        var service = new PaymentService(
            payments, memberships, users, gateway, phonePe, new FakePlanProvider(), unitOfWork,
            audit,
            new CreatePaymentOrderRequestValidator(), new ConfirmRazorpayPaymentRequestValidator(),
            new FixedTimeProvider(Now));
        return new(service, memberships, payments, users, gateway, phonePe, unitOfWork, audit);
    }

    private sealed record PaymentFixture(
        PaymentService Service,
        FakeMembershipRepository Memberships,
        FakePaymentRepository Payments,
        FakeUserRepository Users,
        FakeRazorpayGateway Gateway,
        FakePhonePeGateway PhonePe,
        FakeUnitOfWork UnitOfWork,
        AuditWriterTestDouble Audit);

    private sealed class FakeMembershipRepository : IMembershipRepository
    {
        public Dictionary<string, AvailableJobAccess> Jobs { get; init; } = [];
        public List<Guid> RecordedApplications { get; } = [];
        public Membership? Membership { get; set; }

        public Task<AvailableJobAccess?> GetAvailableJobAsync(
            string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs.GetValueOrDefault(slug));
        public Task<Membership?> GetActiveForUserAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Membership is { Status: MembershipStatus.Active } membership &&
                membership.StartsAtUtc <= Now &&
                (!membership.EndsAtUtc.HasValue || membership.EndsAtUtc > Now)
                    ? membership
                    : null);
        public Task<Membership?> GetPortalMembershipForUserAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Membership?.UserId == userId ? Membership : null);
        public Task<Membership?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Membership?.Id == id ? Membership : null);
        public Task AddAsync(Membership membership, CancellationToken cancellationToken = default)
        {
            Membership = membership;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<MembershipResponse>> GetMembershipsForUserAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<MembershipResponse>>([]);
        public Task<(IReadOnlyCollection<MembershipHistoryResponse> Items, int TotalCount)> GetHistoryAsync(
            Guid userId, HistoryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<MembershipHistoryResponse>)[], 0));
        public Task RecordApplicationAsync(
            Guid userId, Guid jobId, CancellationToken cancellationToken = default)
        {
            RecordedApplications.Add(jobId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public Payment? Payment { get; private set; }
        public Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
        {
            Payment = payment;
            payment.MembershipId = payment.Membership?.Id;
            return Task.CompletedTask;
        }
        public Task<Payment?> GetOwnedAsync(
            Guid id, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payment?.Id == id && Payment.UserId == userId ? Payment : null);
        public Task<Payment?> GetByProviderOrderIdAsync(
            string providerOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payment?.ProviderOrderId == providerOrderId ? Payment : null);
        public Task<Payment?> GetOwnedByProviderOrderIdAsync(
            string providerOrderId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payment?.ProviderOrderId == providerOrderId && Payment.UserId == userId ? Payment : null);
        public Task<Payment?> GetLatestForUserAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payment?.UserId == userId ? Payment : null);
        public Task<bool> HasProcessedProviderEventAsync(
            string providerEventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payment?.History.Any(x => x.ProviderEventId == providerEventId) == true);
        public Task<(IReadOnlyCollection<PaymentResponse> Items, int TotalCount)> GetForUserAsync(
            Guid userId, HistoryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<PaymentResponse>)[], 0));
        public Task<(IReadOnlyCollection<PaymentHistoryResponse> Items, int TotalCount)> GetHistoryAsync(
            Guid userId, HistoryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<PaymentHistoryResponse>)[], 0));
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public bool IsEligible { get; set; } = true;
        public bool EmailConfirmed { get; set; } = true;
        public Task<User?> GetByNormalizedEmailAsync(
            string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);
        public Task<User?> GetByNormalizedPhoneAsync(
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);
        public Task<bool> RegistrationIdentityExistsAsync(
            string normalizedEmail, string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<User?> GetByIdWithRoleAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEligible && userId is var id && (id == UserId || id == OtherCandidateId)
                ? new User
                {
                    Id = id,
                    RoleId = SystemRoleIds.Candidate,
                    EmailConfirmed = EmailConfirmed,
                    Status = UserStatus.Active
                }
                : null);
        public Task AddAsync(User user, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public void Update(User user) { }
    }

    private sealed class FakeRazorpayGateway : IRazorpayGateway
    {
        public string KeyId => "rzp_test_key";
        public long RequestedAmount { get; private set; }
        public string? RequestedCurrency { get; private set; }
        public int CreateOrderCalls { get; private set; }
        public bool PaymentSignatureIsValid { get; set; } = true;
        public bool WebhookSignatureIsValid { get; set; } = true;
        public Func<bool>? BeforeCreateOrder { get; set; }
        public bool LocalOrderWasPersisted { get; private set; }
        public RazorpayPaymentState ReconciliationState { get; set; } =
            new(RazorpayPaymentStateKind.Paid, "pay_1", 9900, "INR");

        public Task<RazorpayOrder> CreateOrderAsync(
            long amountInMinorUnits, string currencyCode, string receipt,
            CancellationToken cancellationToken = default)
        {
            CreateOrderCalls++;
            RequestedAmount = amountInMinorUnits;
            RequestedCurrency = currencyCode;
            LocalOrderWasPersisted = BeforeCreateOrder?.Invoke() ?? true;
            return Task.FromResult(
                new RazorpayOrder("order_1", amountInMinorUnits, currencyCode, receipt));
        }
        public bool VerifyPaymentSignature(
            string orderId, string paymentId, string signature) =>
            PaymentSignatureIsValid;
        public bool VerifyWebhookSignature(
            ReadOnlyMemory<byte> payload, string signature) =>
            WebhookSignatureIsValid;
        public Task<RazorpayPaymentState> GetOrderPaymentStateAsync(
            string orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReconciliationState);
    }

    private sealed class FakePhonePeGateway : IPhonePeGateway
    {
        public List<string> MerchantOrderIds { get; } = [];
        public long RequestedAmount { get; private set; }
        public bool AuthorizationValid { get; set; } = true;
        public PhonePeOrderStateKind CallbackState { get; set; } = PhonePeOrderStateKind.Completed;
        public PhonePeOrderState? VerificationState { get; set; }
        public bool VerificationFails { get; set; }
        public Task<PhonePeCheckout> CreateCheckoutAsync(string merchantOrderId, long amountInMinorUnits,
            CancellationToken cancellationToken = default)
        {
            MerchantOrderIds.Add(merchantOrderId);
            RequestedAmount = amountInMinorUnits;
            return Task.FromResult(new PhonePeCheckout("https://mercury.phonepe.com/checkout"));
        }
        public Task<PhonePeOrderState> GetOrderStatusAsync(string merchantOrderId,
            CancellationToken cancellationToken = default)
        {
            if (VerificationFails) throw new AppException("verification unavailable", 503, "payment_verification_unavailable");
            return Task.FromResult(VerificationState ?? new PhonePeOrderState(
                PhonePeOrderStateKind.Pending, merchantOrderId, AmountInMinorUnits: 9900));
        }
        public bool VerifyWebhookAuthorization(string authorization) => AuthorizationValid;
        public PhonePeCallback ParseCallback(ReadOnlyMemory<byte> rawBody) =>
            new(MerchantOrderIds.Last(), CallbackState,
                $"phonepe-event-{Convert.ToHexString(rawBody.Span)}");
    }

    private sealed class FakePlanProvider : IMembershipPlanProvider
    {
        public MembershipPlan GetDefaultPlan() =>
            new("Job Application Access", 99m, "INR", 30);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
