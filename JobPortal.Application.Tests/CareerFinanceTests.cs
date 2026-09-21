using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.API.Controllers;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Payments;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerFinanceTests
{
    [Theory]
    [InlineData(1000, 10, 100, 900)]
    [InlineData(0.05, 10, 0.01, 0.04)]
    [InlineData(100, 0, 0, 100)]
    [InlineData(100, 100, 100, 0)]
    public void CommissionUsesDecimalAndDeterministicRounding(decimal gross, decimal rate, decimal commission, decimal net)
    {
        Assert.Equal((commission, net), CareerFinanceService.Split(gross, rate));
        Assert.Equal(gross, commission + net);
    }

    [Fact]
    public async Task OrderUsesSnapshotsAndIsIdempotent()
    {
        using var f = new Fixture();
        var b = await f.Setup();
        f.Scheduling.Offering.Price = 3000;
        var first = await f.Order(b.Id);
        var second = await f.Order(b.Id);
        Assert.Equal(first, second); Assert.Equal(1, f.Gateway.OrderCalls); Assert.Equal(99900, first.Amount);
        var payment = await f.Service.GetAsync(f.Candidate, b.Id, false, default);
        Assert.Equal(99.90m, payment.Commission); Assert.Equal(899.10m, payment.Net);
        Assert.Null(typeof(CareerVerifyRequest).GetProperty("Amount"));
        Assert.Null(typeof(CareerCheckout).GetProperty("KeySecret"));
    }

    [Theory]
    [InlineData(CareerBookingStatus.CancelledByCandidate)]
    [InlineData(CareerBookingStatus.CancelledByConsultant)]
    [InlineData(CareerBookingStatus.Completed)]
    [InlineData(CareerBookingStatus.NoShowCandidate)]
    [InlineData(CareerBookingStatus.NoShowConsultant)]
    [InlineData(CareerBookingStatus.Confirmed)]
    public async Task IneligibleBookingCannotCreateOrder(CareerBookingStatus status)
    {
        using var f = new Fixture(); var b = await f.Setup();
        (await f.Db.CareerGuidanceBookings.SingleAsync()).Status = status; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => f.Order(b.Id)); Assert.Equal(0, f.Gateway.OrderCalls);
    }

    [Fact]
    public async Task OwnershipRolesAndInactiveCandidatesAreEnforced()
    {
        using var f = new Fixture(); var b = await f.Setup();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.OrderAsync(f.Scheduling.Other.Id, b.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.OrderAsync(f.Scheduling.Owner.Id, b.Id, default));
        await Assert.ThrowsAsync<AppException>(() => f.Service.OrderAsync(f.Admin.Id, b.Id, default));
        f.Scheduling.Candidate.Status = UserStatus.Suspended; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Order(b.Id));
    }

    [Fact]
    public async Task CaptureConfirmsBookingOnceAndPreservesFinancialSnapshots()
    {
        using var f = new Fixture(); var b = await f.Setup();
        await Assert.ThrowsAsync<ConflictException>(() => f.Scheduling.Service.SetStatusAsync(f.Scheduling.Owner.Id, b.Id,
            new(b.Revision, CareerBookingStatus.Confirmed), default));
        var order = await f.Order(b.Id);
        var paid = await f.Verify(b.Id, order);
        Assert.Equal(CareerPaymentStatus.Captured, paid.Status);
        Assert.Equal(CareerBookingStatus.Confirmed, (await f.Db.CareerGuidanceBookings.SingleAsync()).Status);
        await f.Verify(b.Id, order);
        Assert.Single(await f.Db.Set<CareerGuidanceEarning>().ToArrayAsync());
        f.Options.PlatformCommissionPercent = 50;
        Assert.Equal(10, (await f.Service.GetAsync(f.Candidate, b.Id, false, default)).CommissionPercent);
        var entity = await f.Db.Set<CareerGuidancePayment>().SingleAsync(); entity.AmountGross = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("order")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("payment")]
    [InlineData("authorized")]
    public async Task VerificationRejectsTampering(string mismatch)
    {
        using var f = new Fixture(); var b = await f.Setup(); var o = await f.Order(b.Id);
        var request = new CareerVerifyRequest(o.OrderId, "pay_test", "accepted");
        if (mismatch == "signature") f.Gateway.ValidSignature = false;
        if (mismatch == "order") request = request with { OrderId = "order_wrong" };
        if (mismatch == "amount") f.Gateway.Payment = f.Gateway.Payment! with { Amount = 1 };
        if (mismatch == "currency") f.Gateway.Payment = f.Gateway.Payment! with { Currency = "USD" };
        if (mismatch == "payment") f.Gateway.Payment = f.Gateway.Payment! with { Id = "pay_wrong" };
        if (mismatch == "authorized") f.Gateway.Payment = f.Gateway.Payment! with { Status = "authorized" };
        await Assert.ThrowsAnyAsync<AppException>(() => f.Service.VerifyAsync(f.Candidate, b.Id, request, default));
        Assert.Empty(await f.Db.Set<CareerGuidanceEarning>().ToArrayAsync());
    }

    [Fact]
    public async Task PrivateFinancialQueriesAreScopedAndCannotForceRefund()
    {
        using var f = new Fixture(); var b = await f.Setup(); var o = await f.Order(b.Id); await f.Verify(b.Id, o);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.Scheduling.Other.Id, b.Id, false, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.VerifyAsync(f.Scheduling.Other.Id, b.Id, new(o.OrderId, "pay_test", "accepted"), default));
        await Assert.ThrowsAsync<AppException>(() => f.Service.RefundAsync(f.Candidate, o.PaymentId, new(CareerRefundReason.AdminCorrection), default));
        Assert.Empty((await f.Service.EarningsAsync(f.Candidate, false, new(), default)).Items);
        Assert.Single((await f.Service.EarningsAsync(f.Scheduling.Owner.Id, false, new(), default)).Items);
        Assert.Empty((await f.Service.EarningsAsync(f.Scheduling.Other.Id, false, new(), default)).Items);
        Assert.Single((await f.Service.EarningsAsync(f.Admin.Id, true, new(), default)).Items);
        Assert.Single(await f.Service.SummaryAsync(f.Scheduling.Owner.Id, default));
    }

    [Fact]
    public async Task WebhooksAreIdempotentAndLateFailureCannotRegressCapture()
    {
        using var f = new Fixture(); var b = await f.Setup(); var o = await f.Order(b.Id);
        var body = Payload("payment.captured", o.OrderId);
        Assert.True(await f.Service.TryWebhookAsync(body, "accepted", default));
        Assert.True(await f.Service.TryWebhookAsync(body, "accepted", default));
        f.Gateway.Payment = f.Gateway.Payment! with { Status = "failed" };
        Assert.True(await f.Service.TryWebhookAsync(Payload("payment.failed", o.OrderId), "accepted", default));
        Assert.Equal(CareerPaymentStatus.Captured, (await f.Service.GetAsync(f.Candidate, b.Id, false, default)).Status);
        Assert.Single(await f.Db.Set<CareerGuidanceEarning>().ToArrayAsync());
        Assert.Equal(2, await f.Db.Set<CareerGuidancePaymentEvent>().CountAsync());
        f.Gateway.ValidSignature = false;
        Assert.False(await f.Service.TryWebhookAsync(body, "wrong", default));
    }

    [Fact]
    public async Task FailedAttemptCanLaterCaptureButUnknownEventsHaveNoMutation()
    {
        using var f = new Fixture(); var b = await f.Setup(); var o = await f.Order(b.Id);
        f.Gateway.Payment = f.Gateway.Payment! with { Status = "failed" };
        await f.Service.TryWebhookAsync(Payload("payment.failed", o.OrderId), "accepted", default);
        Assert.Equal("provider_attempt_failed", (await f.Service.GetAsync(f.Candidate, b.Id, false, default)).FailureCode);
        f.Gateway.Payment = f.Gateway.Payment! with { Status = "captured" };
        await f.Service.TryWebhookAsync(Payload("payment.captured", o.OrderId), "accepted", default);
        Assert.False(await f.Service.TryWebhookAsync(Payload("unknown.event", o.OrderId), "accepted", default));
        Assert.Equal(CareerPaymentStatus.Captured, (await f.Service.GetAsync(f.Candidate, b.Id, false, default)).Status);
    }

    [Fact]
    public async Task LateCaptureDoesNotResurrectCancelledBooking()
    {
        using var f = new Fixture(); var b = await f.Setup(); var o = await f.Order(b.Id);
        b = await f.Scheduling.Service.GetAsync(f.Candidate, b.Id, false, false, default);
        await f.Scheduling.Service.CancelAsync(f.Candidate, b.Id, false, new(b.Revision), default);
        var paid = await f.Verify(b.Id, o);
        Assert.True(paid.RequiresRefundReview);
        Assert.Equal(CareerBookingStatus.CancelledByCandidate, (await f.Db.CareerGuidanceBookings.SingleAsync()).Status);
    }

    [Fact]
    public async Task LostOrderResponseIsReconciledWithoutSecondPost()
    {
        using var f = new Fixture(); var b = await f.Setup(); f.Gateway.LoseOrderResponse = true;
        await Assert.ThrowsAsync<ConflictException>(() => f.Order(b.Id));
        var recovered = await f.Order(b.Id);
        Assert.Equal(1, f.Gateway.OrderCalls); Assert.Equal(f.Gateway.Order!.Id, recovered.OrderId);
        var paid = await f.Service.ReconcileAsync(f.Candidate, b.Id, default);
        Assert.Equal(CareerPaymentStatus.Captured, paid.Status);
    }

    [Theory]
    [InlineData("processed", CareerRefundStatus.Processed, CareerEarningStatus.Reversed)]
    [InlineData("pending", CareerRefundStatus.ProviderPending, CareerEarningStatus.Pending)]
    [InlineData("failed", CareerRefundStatus.Failed, CareerEarningStatus.Pending)]
    public async Task ApprovedRefundOnlyReversesOnProviderProcessed(string state, CareerRefundStatus refundStatus, CareerEarningStatus earningStatus)
    {
        using var f = new Fixture(); var b = await f.Setup(); var o = await f.Order(b.Id); await f.Verify(b.Id, o);
        f.Gateway.RefundState = state;
        var r = await f.Service.RefundAsync(f.Admin.Id, o.PaymentId, new(CareerRefundReason.AdminCorrection), default);
        Assert.Equal(refundStatus, r.Status);
        await f.Service.RefundAsync(f.Admin.Id, o.PaymentId, new(CareerRefundReason.AdminCorrection), default);
        Assert.Equal(1, f.Gateway.RefundCalls);
        Assert.Equal(earningStatus, (await f.Db.Set<CareerGuidanceEarning>().SingleAsync()).Status);
        Assert.Equal(state == "processed" ? CareerPaymentStatus.Refunded : CareerPaymentStatus.RefundPending,
            (await f.Service.GetAsync(f.Candidate, b.Id, false, default)).Status);
    }

    [Fact]
    public async Task ObjectiveRefundReasonMustMatchBookingAndProviderAmountMustMatch()
    {
        using var f = new Fixture(); var b = await f.Setup(); var o = await f.Order(b.Id); await f.Verify(b.Id, o);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.RefundAsync(f.Admin.Id, o.PaymentId, new(CareerRefundReason.ConsultantNoShow), default));
        f.Gateway.WrongRefundAmount = true;
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.RefundAsync(f.Admin.Id, o.PaymentId, new(CareerRefundReason.AdminCorrection), default));
        Assert.Equal(CareerEarningStatus.Pending, (await f.Db.Set<CareerGuidanceEarning>().SingleAsync()).Status);
    }

    [Fact]
    public async Task RefundWebhookReversesOnceAndDuplicateDoesNotRewriteHistory()
    {
        using var f = new Fixture(); var b = await f.Setup(); var o = await f.Order(b.Id); await f.Verify(b.Id, o);
        f.Gateway.RefundState = "pending";
        await f.Service.RefundAsync(f.Admin.Id, o.PaymentId, new(CareerRefundReason.AdminCorrection), default);
        f.Gateway.Refund = f.Gateway.Refund! with { Status = "processed" };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { @event = "refund.processed", payload = new { refund = new { entity = new { id = "rfnd_test", payment_id = "pay_test" } } } });
        Assert.True(await f.Service.TryWebhookAsync(bytes, "accepted", default));
        Assert.True(await f.Service.TryWebhookAsync(bytes, "accepted", default));
        Assert.Equal(CareerEarningStatus.Reversed, (await f.Db.Set<CareerGuidanceEarning>().SingleAsync()).Status);
        Assert.Single(await f.Db.Set<CareerGuidanceRefund>().ToArrayAsync());
    }

    [Fact]
    public void HmacUsesExactRawBytesAndAuthorizationMetadataIsRestricted()
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var body = Encoding.UTF8.GetBytes("{\"event\":\"test\"}");
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), body));
        Assert.True(CareerRazorpayGateway.VerifyHmac(body, signature, key));
        Assert.False(CareerRazorpayGateway.VerifyHmac(Encoding.UTF8.GetBytes("{ \"event\":\"test\"}"), signature, key));
        Assert.False(CareerRazorpayGateway.VerifyHmac(body, new string('x', 64), key));
        Assert.Equal("Candidate", typeof(CareerPaymentsController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("Administrator", typeof(AdminCareerFinanceController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.NotNull(typeof(CareerEarningsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(ConsultantPublicResponse).GetProperty("Payment"));
        Assert.False(new CareerFinanceOptions { PaymentsEnabled = true }.IsValid());
        Assert.False(new CareerFinanceOptions { PlatformCommissionPercent = 101 }.IsValid());
    }

    [Fact]
    public async Task CompetingFinancialRevisionsCannotBothCommit()
    {
        using var f = new Fixture(); var b = await f.Setup(); var o = await f.Order(b.Id);
        using var a = new JobPortalDbContext(f.Scheduling.Options); using var c = new JobPortalDbContext(f.Scheduling.Options);
        var first = (await new CareerFinanceRepository(a).PaymentAsync(o.PaymentId, default))!;
        var second = (await new CareerFinanceRepository(c).PaymentAsync(o.PaymentId, default))!;
        first.Revision = Guid.NewGuid(); second.Revision = Guid.NewGuid();
        await new CareerFinanceRepository(a).SaveAsync(default);
        await Assert.ThrowsAsync<ConflictException>(() => new CareerFinanceRepository(c).SaveAsync(default));
    }

    private static byte[] Payload(string type, string order) => JsonSerializer.SerializeToUtf8Bytes(new
    { @event = type, payload = new { payment = new { entity = new { id = "pay_test", order_id = order } } } });

    internal sealed class Fixture : IDisposable
    {
        public CareerSchedulingTests.Fixture Scheduling { get; } = new();
        public JobPortalDbContext Db => Scheduling.Db;
        public Guid Candidate => Scheduling.Candidate.Id;
        public User Admin { get; } = new() { Status = UserStatus.Active };
        public FakeGateway Gateway { get; } = new();
        public CareerFinanceOptions Options { get; } = new() { PaymentsEnabled = true, PlatformCommissionPercent = 10 };
        public CareerFinanceService Service { get; }
        public Fixture()
        {
            var role = new Role { Name = "Administrator" }; Admin.Role = role; Admin.RoleId = role.Id;
            Db.AddRange(role, Admin); Db.SaveChanges();
            Service = new(new CareerFinanceRepository(Db), new UserRepository(Db), Gateway, new AuditWriterTestDouble(), Scheduling.Clock,
                Microsoft.Extensions.Options.Options.Create(Options));
        }
        public Task<CareerBookingResponse> Setup() => Scheduling.Book();
        public Task<CareerCheckout> Order(Guid bookingId) => Service.OrderAsync(Candidate, bookingId, default);
        public Task<CareerPaymentResponse> Verify(Guid bookingId, CareerCheckout order) => Service.VerifyAsync(Candidate, bookingId, new(order.OrderId, "pay_test", "accepted"), default);
        public void Dispose() => Scheduling.Dispose();
    }
    internal sealed class FakeGateway : ICareerPaymentGateway
    {
        public string KeyId => "public_test_key";
        public void ValidateConfiguration() { }
        public bool ValidSignature { get; set; } = true;
        public bool LoseOrderResponse { get; set; }
        public bool WrongRefundAmount { get; set; }
        public int OrderCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public string RefundState { get; set; } = "processed";
        public GatewayOrder? Order { get; set; }
        public GatewayPayment? Payment { get; set; }
        public GatewayRefund? Refund { get; set; }
        public bool VerifyCheckout(string orderId, string paymentId, string signature) => ValidSignature;
        public bool VerifyWebhook(ReadOnlyMemory<byte> body, string signature) => ValidSignature;
        public Task<GatewayOrder> CreateOrderAsync(long amount, string currency, string receipt, CancellationToken ct)
        {
            OrderCalls++; Order = new("order_test", amount, currency, receipt); Payment = new("pay_test", Order.Id, amount, currency, "captured");
            if (LoseOrderResponse) throw new ConflictException("Simulated uncertain response.");
            return Task.FromResult(Order);
        }
        public Task<GatewayOrder?> FindOrderAsync(string receipt, CancellationToken ct) => Task.FromResult(Order);
        public Task<GatewayPayment> GetPaymentAsync(string paymentId, CancellationToken ct) => Task.FromResult(Payment!);
        public Task<GatewayPayment?> CapturedPaymentAsync(string orderId, CancellationToken ct) => Task.FromResult(Payment?.Status == "captured" ? Payment : null);
        public Task<GatewayRefund> CreateRefundAsync(string paymentId, long amount, string receipt, CancellationToken ct)
        { RefundCalls++; Refund = new("rfnd_test", paymentId, WrongRefundAmount ? amount + 1 : amount, "INR", RefundState, receipt); return Task.FromResult(Refund); }
        public Task<GatewayRefund?> FindRefundAsync(string paymentId, string receipt, CancellationToken ct) => Task.FromResult(Refund);
        public Task<GatewayRefund> GetRefundAsync(string refundId, CancellationToken ct) => Task.FromResult(Refund!);
    }
}
