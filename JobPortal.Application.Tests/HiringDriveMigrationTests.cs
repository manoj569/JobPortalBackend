using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class HiringDriveMigrationTests
{
    [Fact]
    public void HiringDriveMigrationsAreOrderedAndPresentInTheMigrationAssembly()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=offline_model_only", o => o.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        var migrations = db.Database.GetMigrations().ToArray();
        var original = Array.IndexOf(migrations, "20261023070836_AddHiringDrives");
        var placement = Array.IndexOf(migrations, "20261023110156_AddHiringDriveHomepagePlacement");
        Assert.True(original >= 0);
        Assert.True(placement > original);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void MigrationCreatesOnlyHiringDriveTableWithConstraintsAndRestrictiveReferences()
    {
        var migration = new AddHiringDrives();
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("HiringDrives", table.Name);
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        Assert.All(migration.UpOperations.OfType<CreateIndexOperation>(), index => Assert.Equal("HiringDrives", index.Table));
        Assert.Equal(4, table.CheckConstraints.Count);
        Assert.Equal(4, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.Equal("bigint", table.Columns.Single(x => x.Name == "ClickCount").ColumnType);
        Assert.Equal("bigint", table.Columns.Single(x => x.Name == "ImpressionCount").ColumnType);
        Assert.DoesNotContain(table.Columns, c => c.Name is "UserId1" or "JobId1" or "CompanyId1");
        Assert.Equal("HiringDrives", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
    }
    [Fact]
    public void SnapshotMatchesModelAndSqlOnlyAddsHiringDrives()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=offline_model_only", o => o.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var migration = new AddHiringDrives();
        var sql = string.Join('\n', db.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.UpOperations, db.GetService<IDesignTimeModel>().Model).Select(c => c.CommandText));
        Assert.Contains("CREATE TABLE \"HiringDrives\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void HomepagePlacementMigrationAddsFalseDefaultAndFilteredIndex()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=offline_model_only", o => o.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        var migration = new AddHiringDriveHomepagePlacement();
        var addColumn = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal("ShowOnHome", addColumn.Name);
        Assert.Equal("HiringDrives", addColumn.Table);
        Assert.Equal(false, addColumn.DefaultValue);
        var index = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>());
        Assert.Equal(new[] { "ShowOnHome", "IsActive", "DisplayOrder" }, index.Columns);
        Assert.Equal("\"IsDeleted\" = FALSE", index.Filter);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
