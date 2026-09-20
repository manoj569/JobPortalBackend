using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerSchedulingMigrationTests
{
    [Fact]
    public void MigrationOnlyAddsSchedulingObjectsAndDatabaseOverlapProtection()
    {
        var migration = new AddCareerGuidanceSchedulingAndBookings();
        var operations = migration.UpOperations;
        Assert.All(operations, o => Assert.True(o is AlterDatabaseOperation or AddColumnOperation or CreateTableOperation or CreateIndexOperation or SqlOperation));
        Assert.Equal(new[] { "CareerConsultantAvailability", "CareerConsultantAvailabilityExceptions", "CareerGuidanceBookings" },
            operations.OfType<CreateTableOperation>().Select(o => o.Name).Order().ToArray());
        Assert.All(operations.OfType<AddColumnOperation>(), o => Assert.Equal("CareerConsultants", o.Table));
        Assert.Equal(new[] { "IsAcceptingBookings", "TimeZoneId" }, operations.OfType<AddColumnOperation>().Select(o => o.Name).Order().ToArray());
        Assert.All(operations.OfType<CreateIndexOperation>(), o => Assert.Contains(o.Table,
            new[] { "CareerConsultantAvailability", "CareerConsultantAvailabilityExceptions", "CareerGuidanceBookings" }));
        Assert.NotNull(Assert.Single(operations.OfType<AlterDatabaseOperation>()).FindAnnotation("Npgsql:PostgresExtension:btree_gist"));
        var sql = Assert.Single(operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("EXCLUDE USING gist", sql);
        Assert.Contains("\"ConsultantId\" WITH =", sql);
        Assert.Contains("tstzrange(\"StartUtc\", \"EndUtc\", '[)') WITH &&", sql);
        Assert.Contains("\"IsDeleted\" = FALSE AND \"Status\" IN (1, 2)", sql);
        Assert.Equal(1, (int)CareerBookingStatus.Pending);
        Assert.Equal(2, (int)CareerBookingStatus.Confirmed);
        Assert.DoesNotContain(migration.DownOperations, o => o is AlterDatabaseOperation);
    }

    [Fact]
    public void PostgreSqlModelKeepsRevisionAndExpectedRelationshipsWithoutShadowForeignKeys()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var booking = db.Model.FindEntityType(typeof(CareerGuidanceBooking))!;
        Assert.True(booking.FindProperty(nameof(CareerGuidanceBooking.Revision))!.IsConcurrencyToken);
        Assert.Equal(4, booking.GetForeignKeys().Count());
        Assert.All(booking.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.DoesNotContain(booking.GetProperties(), p => p.IsShadowProperty());
        Assert.Equal(18, booking.FindProperty(nameof(CareerGuidanceBooking.PriceSnapshot))!.GetPrecision());
        Assert.NotNull(booking.GetQueryFilter());
    }
}
