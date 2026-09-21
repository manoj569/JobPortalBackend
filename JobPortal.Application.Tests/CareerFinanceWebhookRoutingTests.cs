using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Payments;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerFinanceWebhookRoutingTests
{
    [Fact]
    public async Task CareerEventDoesNotResolveMembershipService()
    {
        using var f = new CareerFinanceTests.Fixture(); var b = await f.Setup(); var order = await f.Order(b.Id);
        using var services = new ServiceCollection().BuildServiceProvider();
        var controller = new RazorpayWebhooksController(f.Service, f.Gateway, new ConfigurationBuilder().Build(), services);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { @event = "payment.captured", payload = new { payment = new { entity = new { id = "pay_test", order_id = order.OrderId } } } });
        Context(controller, body, "accepted");
        Assert.IsType<OkObjectResult>((await controller.Webhook(default)).Result);
    }

    [Fact]
    public async Task SignedMembershipEventRoutesToExistingHandlerAndInvalidSignatureDoesNot()
    {
        using var f = new CareerFinanceTests.Fixture(); f.Gateway.ValidSignature = false;
        var payment = new JobPortal.Domain.Entities.Payment { UserId = f.Candidate, ProviderOrderId = "order_membership", Amount = 99 };
        f.Db.Add(payment); await f.Db.SaveChangesAsync();
        var proxy = DispatchProxy.Create<IPaymentService, MembershipSpy>();
        using var services = new ServiceCollection().AddSingleton(proxy).AddSingleton<IPaymentRepository>(new PaymentRepository(f.Db)).BuildServiceProvider();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Razorpay:WebhookSecret"] = secret }).Build();
        var controller = new RazorpayWebhooksController(f.Service, f.Gateway, config, services);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { @event = "payment.captured", payload = new { payment = new { entity = new { id = "pay_member", order_id = "order_membership" } } } });
        Context(controller, body, Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)));
        Assert.IsType<OkObjectResult>((await controller.Webhook(default)).Result);
        Assert.Equal(1, ((MembershipSpy)(object)proxy).Calls);
        Context(controller, body, new string('0', 64));
        await Assert.ThrowsAsync<BadRequestException>(() => controller.Webhook(default));
        Assert.Equal(1, ((MembershipSpy)(object)proxy).Calls);
    }

    [Fact]
    public async Task UnknownSignedEventIsAcknowledgedWithoutMembershipGateway()
    {
        using var f = new CareerFinanceTests.Fixture();
        using var services = new ServiceCollection().AddSingleton<IPaymentRepository>(new PaymentRepository(f.Db)).BuildServiceProvider();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Razorpay:WebhookSecret"] = secret }).Build();
        var controller = new RazorpayWebhooksController(f.Service, f.Gateway, config, services);
        var body = Encoding.UTF8.GetBytes("{\"event\":\"unknown\"}");
        Context(controller, body, Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)));
        Assert.IsType<OkObjectResult>((await controller.Webhook(default)).Result);
    }

    private static void Context(ControllerBase controller, byte[] bytes, string signature)
    {
        var http = new DefaultHttpContext(); http.Request.Body = new MemoryStream(bytes); http.Request.ContentLength = bytes.Length;
        http.Request.Headers["X-Razorpay-Signature"] = signature;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
    }
    public class MembershipSpy : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IPaymentService.ProcessWebhookAsync), targetMethod!.Name);
            Calls++; return Task.FromResult(new RazorpayWebhookResponse("Membership event acknowledged."));
        }
    }
}
