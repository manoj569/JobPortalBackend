using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerFinanceMigrationTests
{
    [Fact]
    public void PostgreSqlMigrationSqlGeneratesOfflineWithExpectedChecksAndIndexes()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        var migration = new AddCareerGuidancePaymentsAndEarnings();
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.GetService<IDesignTimeModel>().Model).Select(c => c.CommandText));
        Assert.Contains("CREATE UNIQUE INDEX", sql);
        Assert.Contains("CK_CGPayment_Amounts", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("xmin", sql);
        Assert.DoesNotContain("JobId1", sql);
    }
    [Fact]
    public void MigrationAddsOnlyFourFinancialTablesAndLegacySafeBookingFlag()
    {
        var operations = new AddCareerGuidancePaymentsAndEarnings().UpOperations;
        Assert.All(operations, o => Assert.True(o is CreateTableOperation or CreateIndexOperation or AddColumnOperation));
        Assert.Equal(new[] { "CareerGuidanceEarnings", "CareerGuidancePaymentEvents", "CareerGuidancePayments", "CareerGuidanceRefunds" },
            operations.OfType<CreateTableOperation>().Select(t => t.Name).Order().ToArray());
        var column = Assert.Single(operations.OfType<AddColumnOperation>());
        Assert.Equal("CareerGuidanceBookings", column.Table); Assert.Equal("RequiresPayment", column.Name); Assert.Equal(false, column.DefaultValue);
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        foreach (var (table, field) in new[] { ("CareerGuidancePayments", "BookingId"), ("CareerGuidancePayments", "ProviderOrderId"),
            ("CareerGuidancePayments", "ProviderPaymentId"), ("CareerGuidancePaymentEvents", "EventKey"),
            ("CareerGuidanceEarnings", "PaymentId"), ("CareerGuidanceRefunds", "PaymentId"), ("CareerGuidanceRefunds", "ProviderRefundId") })
            Assert.Contains(indexes, i => i.Table == table && i.IsUnique && i.Columns.SequenceEqual(new[] { field }));
        Assert.All(operations.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys), fk => Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, fk.OnDelete));
        Assert.Contains(operations.OfType<CreateTableOperation>().Single(t => t.Name == "CareerGuidancePayments").CheckConstraints,
            c => c.Sql.Contains("\"AmountGross\" = \"PlatformCommissionAmount\" + \"ConsultantNetAmount\"", StringComparison.Ordinal));
    }

    [Fact]
    public void FinancialModelHasRevisionsPrecisionAndNoMembershipCoupling()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        foreach (var type in new[] { typeof(CareerGuidancePayment), typeof(CareerGuidanceEarning), typeof(CareerGuidanceRefund) })
        {
            var model = db.Model.FindEntityType(type)!;
            Assert.True(model.FindProperty("Revision")!.IsConcurrencyToken);
            Assert.DoesNotContain(model.GetProperties(), p => p.IsShadowProperty());
            Assert.DoesNotContain(model.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Membership));
        }
        Assert.Equal(18, db.Model.FindEntityType(typeof(CareerGuidancePayment))!.FindProperty("AmountGross")!.GetPrecision());
    }
}
