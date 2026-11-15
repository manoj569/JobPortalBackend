using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.API.Health;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Application.Features.Notifications;
using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorPhase6Tests
{
    [Fact]
    public async Task ReadinessTreatsDisabledAiAsHealthyConfiguredFeatureState()
    {
        var settings = new AcceleratorOptions { Enabled = true, AIEnabled = false };
        var check = new AcceleratorConfigurationHealthCheck(Options.Create(settings), new Phase6PlanProvider());
        var result = await check.CheckHealthAsync(new HealthCheckContext(), default);
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("disabled", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhonePeServerVerificationActivatesOnceAndRejectsAnotherCandidate()
    {
        using var fixture = new AcceleratorFixture();
        var gateway = new Phase6PhonePeGateway();
        var payments = PaymentService(fixture, gateway);
        var key = Guid.NewGuid();
        var checkout = await payments.CreatePhonePeCheckoutAsync(fixture.Candidate.Id,
            new CreatePaymentOrderRequest(AcceleratorOptions.PlanCode, key), null, default);
        var replay = await payments.CreatePhonePeCheckoutAsync(fixture.Candidate.Id,
            new CreatePaymentOrderRequest(AcceleratorOptions.PlanCode, key), null, default);
        Assert.Equal(checkout, replay);
        Assert.Equal(1, gateway.CheckoutCalls);
        await Assert.ThrowsAsync<ConflictException>(() => payments.CreatePhonePeCheckoutAsync(
            fixture.Candidate.Id, new CreatePaymentOrderRequest("CareerHarborMembership", key), null, default));
        Assert.Equal(32804, checkout.AmountInMinorUnits);
        Assert.Equal("INR", checkout.CurrencyCode);
        Assert.Equal("NotSubscribed", (await fixture.Service.EntitlementAsync(fixture.Candidate.Id, default)).Status);
        await Assert.ThrowsAsync<NotFoundException>(() => payments.GetPhonePeStatusAsync(
            fixture.Other.Id, checkout.MerchantOrderId, default));
        gateway.State = PhonePeOrderStateKind.Completed;
        var verified = await payments.GetPhonePeStatusAsync(fixture.Candidate.Id, checkout.MerchantOrderId, default);
        Assert.Equal(PhonePeBrowserPaymentStatus.Completed, verified.Status);
        Assert.Equal("Active", (await fixture.Service.EntitlementAsync(fixture.Candidate.Id, default)).Status);
        var cycle = Assert.Single(fixture.Db.AcceleratorBillingCycles);
        Assert.Equal(checkout.MerchantOrderId, cycle.Payment.ProviderOrderId);
        await payments.GetPhonePeStatusAsync(fixture.Candidate.Id, checkout.MerchantOrderId, default);
        Assert.Equal(checkout, await payments.CreatePhonePeCheckoutAsync(fixture.Candidate.Id,
            new CreatePaymentOrderRequest(AcceleratorOptions.PlanCode, key), null, default));
        Assert.Equal(1, gateway.CheckoutCalls);
        Assert.Single(fixture.Db.AcceleratorBillingCycles);
        Assert.Single(fixture.Db.AcceleratorAllowances.Where(x => x.CycleId == cycle.Id &&
            x.Feature == AcceleratorFeature.Tutor));
    }

    [Fact]
    public async Task PendingAndFailedPhonePePaymentsNeverGrantPaidAccess()
    {
        using var fixture = new AcceleratorFixture();
        var gateway = new Phase6PhonePeGateway();
        var payments = PaymentService(fixture, gateway);
        var checkout = await payments.CreatePhonePeCheckoutAsync(fixture.Candidate.Id,
            new CreatePaymentOrderRequest(AcceleratorOptions.PlanCode), null, default);
        Assert.Equal(PhonePeBrowserPaymentStatus.Pending,
            (await payments.GetPhonePeStatusAsync(fixture.Candidate.Id, checkout.MerchantOrderId, default)).Status);
        gateway.State = PhonePeOrderStateKind.Failed;
        Assert.Equal(PhonePeBrowserPaymentStatus.Failed,
            (await payments.GetPhonePeStatusAsync(fixture.Candidate.Id, checkout.MerchantOrderId, default)).Status);
        Assert.Empty(fixture.Db.AcceleratorBillingCycles);
        await Assert.ThrowsAsync<AppException>(() => fixture.Quota.ReserveAsync(fixture.Candidate.Id,
            AcceleratorFixture.Reservation(), default));
    }

    [Fact]
    public async Task AuthenticatedCallbackStillRequiresMatchingServerVerifiedAmount()
    {
        using var fixture = new AcceleratorFixture();
        var gateway = new Phase6PhonePeGateway();
        var payments = PaymentService(fixture, gateway);
        var checkout = await payments.CreatePhonePeCheckoutAsync(fixture.Candidate.Id,
            new CreatePaymentOrderRequest(AcceleratorOptions.PlanCode), null, default);
        gateway.State = PhonePeOrderStateKind.Completed;
        var callback = new PhonePeWebhookRequest(Encoding.UTF8.GetBytes(checkout.MerchantOrderId), "test-auth");
        await Assert.ThrowsAsync<BadRequestException>(() => payments.ProcessPhonePeWebhookAsync(
            callback with { Authorization = "invalid" }, default));
        gateway.ReportedAmountOverride = checkout.AmountInMinorUnits - 100;
        await Assert.ThrowsAsync<ConflictException>(() => payments.ProcessPhonePeWebhookAsync(callback, default));
        Assert.Empty(fixture.Db.AcceleratorBillingCycles);
        gateway.ReportedAmountOverride = null;
        await payments.ProcessPhonePeWebhookAsync(callback, default);
        await payments.ProcessPhonePeWebhookAsync(callback, default);
        Assert.Single(fixture.Db.AcceleratorBillingCycles);
        Assert.Equal("Active", (await fixture.Service.EntitlementAsync(fixture.Candidate.Id, default)).Status);
    }

    [Fact]
    public async Task AdminPaymentReconciliationRequiresRoleReasonAndProviderSuccess()
    {
        using var fixture = new AcceleratorFixture();
        var gateway = new Phase6PhonePeGateway();
        var payments = PaymentService(fixture, gateway);
        var checkout = await payments.CreatePhonePeCheckoutAsync(fixture.Candidate.Id,
            new CreatePaymentOrderRequest(AcceleratorOptions.PlanCode), null, default);
        await Assert.ThrowsAsync<AppException>(() => payments.ReconcileAcceleratorPhonePeForAdminAsync(
            fixture.Candidate.Id, checkout.MerchantOrderId, new("Investigated order"), default));
        await Assert.ThrowsAsync<BadRequestException>(() => payments.ReconcileAcceleratorPhonePeForAdminAsync(
            fixture.Admin.Id, checkout.MerchantOrderId, new(""), default));
        var pending = await payments.ReconcileAcceleratorPhonePeForAdminAsync(fixture.Admin.Id,
            checkout.MerchantOrderId, new("Provider status reviewed"), default);
        Assert.Equal(PaymentStatus.Pending, pending.Status);
        Assert.Empty(fixture.Db.AcceleratorBillingCycles);
        gateway.State = PhonePeOrderStateKind.Completed;
        await payments.ReconcileAcceleratorPhonePeForAdminAsync(fixture.Admin.Id,
            checkout.MerchantOrderId, new("Verified provider status"), default);
        Assert.Single(fixture.Db.AcceleratorBillingCycles);
        Assert.Contains(fixture.Audit.Events, x => x.Metadata is not null &&
            x.Metadata.TryGetValue("operation", out var value) && value == "AdminPhonePeReconciliation");
    }

    [Fact]
    public async Task BackgroundRecoveryActivatesPersistedPaidOrderWithoutSecondCharge()
    {
        using var fixture = new AcceleratorFixture();
        var payment = await fixture.Payment();
        payment.DurationDays = 30;
        payment.PlanVersion = fixture.Settings.PlanVersion;
        await fixture.Db.SaveChangesAsync();
        var store = new AcceleratorPhase6OperationsStore(fixture.Db);
        Assert.Contains(payment.ProviderOrderId!, await store.ReconcilablePhonePeOrdersAsync(
            fixture.Clock.Utc.AddMinutes(-2), 25, default));
        var gateway = new Phase6PhonePeGateway();
        var payments = PaymentService(fixture, gateway);
        await payments.ReconcileAcceleratorPhonePeSystemAsync(payment.ProviderOrderId!, default);
        await payments.ReconcileAcceleratorPhonePeSystemAsync(payment.ProviderOrderId!, default);
        Assert.Single(fixture.Db.AcceleratorBillingCycles);
        Assert.DoesNotContain(payment.ProviderOrderId!, await store.ReconcilablePhonePeOrdersAsync(
            fixture.Clock.Utc.AddMinutes(-2), 25, default));
    }

    [Fact]
    public async Task RefundReviewIsAuditedAndNeverPretendsPhonePeRefunded()
    {
        using var fixture = new AcceleratorFixture();
        var payment = await fixture.Payment();
        var payments = PaymentService(fixture, new Phase6PhonePeGateway());
        await Assert.ThrowsAsync<AppException>(() => payments.FlagAcceleratorRefundReviewAsync(
            fixture.Candidate.Id, payment.ProviderOrderId!, new("Customer requested review"), default));
        await payments.FlagAcceleratorRefundReviewAsync(fixture.Admin.Id, payment.ProviderOrderId!,
            new("Customer requested review"), default);
        await payments.FlagAcceleratorRefundReviewAsync(fixture.Admin.Id, payment.ProviderOrderId!,
            new("Customer requested review"), default);
        await Assert.ThrowsAsync<ConflictException>(() => payments.FlagAcceleratorRefundReviewAsync(
            fixture.Admin.Id, payment.ProviderOrderId!, new("Different reason"), default));
        Assert.True(payment.AcceleratorRefundReviewRequired);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Single(fixture.Audit.Events, x => x.Metadata is not null &&
            x.Metadata.TryGetValue("operation", out var value) && value == "AcceleratorRefundReviewRequested");
    }

    [Fact]
    public async Task RenewalExtendsFromExistingEndAndDoesNotDuplicateCycleOnReplay()
    {
        using var fixture = new AcceleratorFixture();
        var gateway = new Phase6PhonePeGateway();
        var payments = PaymentService(fixture, gateway);
        var first = await payments.CreatePhonePeCheckoutAsync(fixture.Candidate.Id,
            new CreatePaymentOrderRequest(AcceleratorOptions.PlanCode), null, default);
        gateway.State = PhonePeOrderStateKind.Completed;
        await payments.GetPhonePeStatusAsync(fixture.Candidate.Id, first.MerchantOrderId, default);
        var original = Assert.Single(fixture.Db.AcceleratorBillingCycles);
        var renewal = await payments.CreatePhonePeCheckoutAsync(fixture.Candidate.Id,
            new CreatePaymentOrderRequest(AcceleratorOptions.PlanCode), null, default);
        await payments.GetPhonePeStatusAsync(fixture.Candidate.Id, renewal.MerchantOrderId, default);
        var second = Assert.Single(fixture.Db.AcceleratorBillingCycles.Where(x => x.Id != original.Id));
        Assert.Equal(original.EndsAtUtc, second.StartsAtUtc);
        Assert.Equal(original.EndsAtUtc.AddDays(30), second.EndsAtUtc);
        Assert.Equal("Active", (await fixture.Service.EntitlementAsync(fixture.Candidate.Id, default)).Status);
        await payments.GetPhonePeStatusAsync(fixture.Candidate.Id, renewal.MerchantOrderId, default);
        Assert.Equal(2, await fixture.Db.AcceleratorBillingCycles.CountAsync());
    }

    [Fact]
    public async Task KillSwitchesPreventNewReservationsWithoutDeletingHistoricalEvidence()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        fixture.Settings.Policies[AcceleratorFeature.Tutor].Enabled = false;
        await Assert.ThrowsAsync<AcceleratorProviderUnavailableException>(() => fixture.Quota.ReserveAsync(
            fixture.Candidate.Id, AcceleratorFixture.Reservation(), default));
        fixture.Settings.Policies[AcceleratorFeature.Tutor].Enabled = true;
        fixture.Settings.AIEnabled = false;
        await Assert.ThrowsAsync<AcceleratorProviderUnavailableException>(() => fixture.Quota.ReserveAsync(
            fixture.Candidate.Id, AcceleratorFixture.Reservation(), default));
        Assert.Equal("Active", (await fixture.Service.EntitlementAsync(fixture.Candidate.Id, default)).Status);
        Assert.Single(fixture.Db.AcceleratorBillingCycles);
        Assert.Empty(fixture.Db.AcceleratorUsageReservations);
    }

    [Fact]
    public async Task AdminOperationsAreAuthorizedAndCostExposureIsSeparated()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var reserved = await fixture.Quota.ReserveAsync(fixture.Candidate.Id,
            AcceleratorFixture.Reservation(), default);
        var another = await fixture.Quota.ReserveAsync(fixture.Candidate.Id,
            AcceleratorFixture.Reservation(), default);
        await fixture.Quota.MarkDispatchedAsync(fixture.Candidate.Id, another.Id, default);
        fixture.Db.AcceleratorAiDispatchRecords.Add(new AcceleratorAiDispatchRecord
        {
            UserId = fixture.Candidate.Id, ReservationId = another.Id, RequestKey = Guid.NewGuid(),
            RequestHash = new string('A', 64), Provider = fixture.Provider.Name,
            Model = "deterministic-test", Operation = "TutorMessage",
            Status = AcceleratorAiDispatchStatus.Uncertain
        });
        await fixture.Db.SaveChangesAsync();
        var completed = await fixture.Quota.ReserveAsync(fixture.Candidate.Id,
            AcceleratorFixture.Reservation(), default);
        await fixture.Quota.MarkDispatchedAsync(fixture.Candidate.Id, completed.Id, default);
        await fixture.Quota.CompleteAsync(fixture.Candidate.Id, completed.Id, 70, 15, .1m, default);
        var service = new AcceleratorPhase6OperationsService(new AcceleratorPhase6OperationsStore(fixture.Db),
            fixture.Service, Options.Create(fixture.Settings), fixture.Clock);
        await Assert.ThrowsAsync<AppException>(() => service.OverviewAsync(fixture.Candidate.Id, default));
        await Assert.ThrowsAsync<AppException>(() => service.DispatchesAsync(
            fixture.Candidate.Id, true, 1, 20, default));
        var summary = await service.AiCostsAsync(fixture.Admin.Id, default);
        var tutor = Assert.Single(summary, x => x.Feature == AcceleratorFeature.Tutor);
        Assert.True(tutor.ReservedExposureInr > 0);
        Assert.Equal(.1m, tutor.ConfirmedCostInr);
        Assert.True(tutor.UncertainExposureInr > 0);
        Assert.Equal(AcceleratorUsageState.Reserved,
            Assert.Single(fixture.Db.AcceleratorUsageReservations.Where(x => x.Id == reserved.Id)).State);
        var configuration = await service.ConfigurationAsync(fixture.Admin.Id, default);
        Assert.Equal(fixture.Settings.PlanVersion, configuration.PlanVersion);
        var dispatch = Assert.Single(await service.DispatchesAsync(fixture.Admin.Id, true, 1, 20, default));
        Assert.Equal(another.Id, dispatch.ReservationId);
        Assert.DoesNotContain("RequestHash", JsonSerializer.Serialize(dispatch), StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", JsonSerializer.Serialize(configuration), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CostSummaryCountsChargeOnCompletionDayAndCarriesOpenExposure()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var completed = await fixture.Quota.ReserveAsync(fixture.Candidate.Id,
            AcceleratorFixture.Reservation(), default);
        await fixture.Quota.MarkDispatchedAsync(fixture.Candidate.Id, completed.Id, default);
        var unresolved = await fixture.Quota.ReserveAsync(fixture.Candidate.Id,
            AcceleratorFixture.Reservation(), default);
        await fixture.Quota.MarkDispatchedAsync(fixture.Candidate.Id, unresolved.Id, default);
        fixture.Clock.Utc = fixture.Clock.Utc.AddDays(1);
        await fixture.Quota.CompleteAsync(fixture.Candidate.Id, completed.Id, 70, 15, .1m, default);
        var service = new AcceleratorPhase6OperationsService(new AcceleratorPhase6OperationsStore(fixture.Db),
            fixture.Service, Options.Create(fixture.Settings), fixture.Clock);
        var result = Assert.Single(await service.AiCostsAsync(fixture.Admin.Id, default));
        Assert.Equal(0, result.Requests);
        Assert.Equal(.1m, result.ConfirmedCostInr);
        Assert.True(result.ReservedExposureInr > 0);
    }

    private static PaymentService PaymentService(AcceleratorFixture fixture, Phase6PhonePeGateway gateway) =>
        new(new PaymentRepository(fixture.Db), new MembershipRepository(fixture.Db, fixture.Clock),
            new UserRepository(fixture.Db), new Phase6RazorpayGateway(), gateway,
            new Phase6PlanProvider(), new UnitOfWork(fixture.Db), fixture.Audit,
            new CreatePaymentOrderRequestValidator(), new ConfirmRazorpayPaymentRequestValidator(),
            fixture.Clock, new NotificationOutbox(new NotificationOutboxRepository(fixture.Db), fixture.Clock),
            fixture.Service);
}

internal sealed class Phase6PlanProvider : IMembershipPlanProvider
{
    public decimal GetGstRate() => 18m;
    public MembershipPlan GetDefaultPlan() => new("Job Application Access", 99m, "INR", 30);
    public MembershipPlan GetRequired(string planCode) => planCode == AcceleratorOptions.PlanCode
        ? new(AcceleratorOptions.PlanCode, "CareerHarbor AI Career Accelerator", 278m, "INR", 30, true, false, false)
        : GetDefaultPlan();
    public IReadOnlyList<MembershipPlan> GetPlans() => [GetDefaultPlan(), GetRequired(AcceleratorOptions.PlanCode)];
}

internal sealed class Phase6PhonePeGateway : IPhonePeGateway
{
    public PhonePeOrderStateKind State { get; set; } = PhonePeOrderStateKind.Pending;
    public long? ReportedAmountOverride { get; set; }
    public int CheckoutCalls { get; private set; }
    private long amount;
    public Task<PhonePeCheckout> CreateCheckoutAsync(string merchantOrderId, long amountInMinorUnits,
        CancellationToken cancellationToken = default)
    {
        CheckoutCalls++;
        amount = amountInMinorUnits;
        return Task.FromResult(new PhonePeCheckout("https://payments.example.invalid/checkout"));
    }
    public Task<PhonePeOrderState> GetOrderStatusAsync(string merchantOrderId,
        CancellationToken cancellationToken = default) => Task.FromResult(new PhonePeOrderState(State,
            merchantOrderId, State == PhonePeOrderStateKind.Completed ? $"verified-{merchantOrderId}" : null,
            ReportedAmountOverride ?? amount));
    public bool VerifyWebhookAuthorization(string authorization) => authorization == "test-auth";
    public PhonePeCallback ParseCallback(ReadOnlyMemory<byte> rawBody) =>
        new(Encoding.UTF8.GetString(rawBody.Span), State, "test-event-1");
}

internal sealed class Phase6RazorpayGateway : IRazorpayGateway
{
    public string KeyId => "unused";
    public Task<RazorpayOrder> CreateOrderAsync(long amountInMinorUnits, string currencyCode, string receipt,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature) => false;
    public bool VerifyWebhookSignature(ReadOnlyMemory<byte> payload, string signature) => false;
    public Task<RazorpayPaymentState> GetOrderPaymentStateAsync(string orderId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
