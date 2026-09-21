using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerSessionMigrationTests
{
    [Fact]
    public void MigrationAddsOnlySessionAndReminderTablesWithRestrictiveForeignKeys()
    {
        var operations = new AddCareerGuidanceSessionsAndReminders().UpOperations;
        Assert.All(operations, o => Assert.True(o is CreateTableOperation or CreateIndexOperation));
        Assert.Equal(new[] { "CareerGuidanceSessionReminders", "CareerGuidanceSessions" }, operations.OfType<CreateTableOperation>().Select(t => t.Name).Order().ToArray());
        Assert.All(operations.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys), fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, i => i.IsUnique && i.Table == "CareerGuidanceSessions" && i.Columns.SequenceEqual(new[] { "BookingId" }));
        Assert.Contains(indexes, i => i.IsUnique && i.Columns.SequenceEqual(new[] { "MeetingProvider", "ProviderMeetingId" }) && i.Filter == "\"ProviderMeetingId\" IS NOT NULL");
        Assert.Contains(indexes, i => i.IsUnique && i.Columns.SequenceEqual(new[] { "SessionId", "RecipientUserId", "OffsetMinutes" }));
        var session = operations.OfType<CreateTableOperation>().Single(t => t.Name == "CareerGuidanceSessions");
        Assert.Contains(session.CheckConstraints, c => c.Name == "CK_CGSession_Interval");
        Assert.Contains(session.CheckConstraints, c => c.Name == "CK_CGSession_Status");
        Assert.All(session.Columns.Where(c => c.Name.Contains("Url", StringComparison.Ordinal)), c => Assert.Equal("bytea", c.ColumnType));
    }

    [Fact]
    public void PostgreSqlSqlGeneratesOfflineAndModelHasConcurrencyWithoutShadowProperties()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(new AddCareerGuidanceSessionsAndReminders().UpOperations,
            db.GetService<IDesignTimeModel>().Model).Select(c => c.CommandText));
        Assert.Contains("CREATE UNIQUE INDEX", sql); Assert.Contains("CK_CGSession_Interval", sql);
        foreach (var forbidden in new[] { "DROP ", "ALTER ", "INSERT INTO ", "UPDATE ", "DELETE FROM ", "xmin", "JobId1" }) Assert.DoesNotContain(forbidden, sql);
        foreach (var type in new[] { typeof(CareerGuidanceSession), typeof(CareerGuidanceSessionReminder) })
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.True(entity.FindProperty("Revision")!.IsConcurrencyToken);
            Assert.DoesNotContain(entity.GetProperties(), p => p.IsShadowProperty());
        }
    }
}
