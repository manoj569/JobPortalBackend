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

public sealed class SupportTicketMigrationTests
{
    [Fact]
    public void UpCreatesOnlySupportTicketsAndItsIndexes()
    {
        var migration = new AddSupportTickets();
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("SupportTickets", table.Name);
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        Assert.All(migration.UpOperations.OfType<CreateIndexOperation>(), index => Assert.Equal("SupportTickets", index.Table));
        Assert.True(table.Columns.Single(c => c.Name == "UserId").IsNullable);
        Assert.True(table.Columns.Single(c => c.Name == "ScreenshotPath").IsNullable);
        Assert.Equal(3, table.CheckConstraints.Count);
        var foreignKey = Assert.Single(table.ForeignKeys);
        Assert.Equal("Users", foreignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete);
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), i => i.IsUnique && i.Name == "IX_SupportTickets_TicketNumber");
        Assert.DoesNotContain(table.Columns, c => c.Name is "xmin" or "UserId1" or "JobId1");
        Assert.Equal("SupportTickets", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
    }

    [Fact]
    public void DesignerSnapshotAndCurrentModelAgreeAndSqlIsScopedToSupport()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=offline_model_only", o => o.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        Assert.False(db.Database.HasPendingModelChanges());
        var migration = new AddSupportTickets();
        var sql = string.Join('\n', db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, model).Select(c => c.CommandText));
        Assert.Contains("CREATE TABLE \"SupportTickets\"", sql, StringComparison.Ordinal);
        Assert.Contains("REFERENCES \"Users\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("xmin", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JobId1", sql, StringComparison.Ordinal);
        var historical = migration.TargetModel.FindEntityType(typeof(SupportTicket).FullName!)!;
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model.FindEntityType(typeof(SupportTicket).FullName!)!;
        Assert.Equal(historical.GetProperties().Select(p => (p.Name, p.GetColumnType(), p.IsNullable, p.IsConcurrencyToken)),
            snapshot.GetProperties().Select(p => (p.Name, p.GetColumnType(), p.IsNullable, p.IsConcurrencyToken)));
    }
}
