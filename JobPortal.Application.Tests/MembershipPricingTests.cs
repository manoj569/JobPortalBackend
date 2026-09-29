using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Payments;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class MembershipPricingTests
{
    private static readonly string[] PendingIndexColumns = ["UserId", "PlanCode"];
    [Theory]
    [InlineData("refunded")]
    [InlineData("recipient")]
    [InlineData("pending")]
    public async Task PurchaseDeliveryRequiresVerifiedPaymentAndCorrectRecipient(string change)
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        var user = new User { Status = UserStatus.Active };
        var payment = new Payment { UserId = user.Id, MembershipId = Guid.NewGuid(), Status = PaymentStatus.Paid, PaidAtUtc = now };
        var delivery = new NotificationDelivery { Source = NotificationSource.MembershipPurchase, SourceId = payment.Id,
            UserId = user.Id, Status = NotificationDeliveryStatus.Processing, LeaseOwner = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddMinutes(3) };
        db.AddRange(user, payment, delivery);
        await db.SaveChangesAsync();
        var repository = new NotificationDeliveryRepository(db);
        Assert.True(await repository.IsEligibleAsync(delivery, now, default));
        if (change == "refunded") payment.Status = PaymentStatus.Refunded;
        if (change == "pending") payment.Status = PaymentStatus.Pending;
        if (change == "recipient") payment.UserId = Guid.NewGuid();
        await db.SaveChangesAsync();
        Assert.False(await repository.IsEligibleAsync(delivery, now, default));
    }
    [Theory]
    [InlineData(null, 18)]
    [InlineData("5", 5)]
    [InlineData("0", 0)]
    public void GstRateComesFromBackendConfiguration(string? configured, decimal expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Membership:Tax:GstRate"] = configured
        }).Build();
        Assert.Equal(expected, new ConfigurationMembershipPlanProvider(config).GetGstRate());
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/plain,hello")]
    [InlineData("/dashboard/jobs/referral/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/contact?next=https://evil.example")]
    [InlineData("/dashboard/jobs/referral/../contact")]
    public void ReturnDestinationRejectsExternalAndUnrecognizedPaths(string path)
    {
        Assert.Throws<JobPortal.Application.Common.Exceptions.BadRequestException>(() => PaymentReturnPath.Validate(path));
    }

    [Fact]
    public void PostgreSqlModelFencesConcurrentSamePlanAttemptsAndRetainsConcurrencyTokens()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var payment = db.Model.FindEntityType(typeof(Payment))!;
        var pending = Assert.Single(payment.GetIndexes(), x => x.GetDatabaseName() == "UX_Payments_UnresolvedUserPlan");
        Assert.True(pending.IsUnique);
        Assert.Equal(PendingIndexColumns, pending.Properties.Select(x => x.Name));
        Assert.Contains("IN (1, 2, 7)", pending.GetFilter());
        Assert.True(payment.FindProperty("xmin")!.IsConcurrencyToken);
        Assert.True(db.Model.FindEntityType(typeof(Membership))!.FindProperty("xmin")!.IsConcurrencyToken);
        Assert.True(payment.FindProperty(nameof(Payment.BaseAmount))!.IsNullable);
        Assert.Equal(2, payment.FindProperty(nameof(Payment.TaxAmount))!.GetScale());
        Assert.Equal(4, payment.FindProperty(nameof(Payment.TaxRate))!.GetScale());
    }
}
