using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class GeneralJobCategoryMigrationTests
{
    [Fact]
    public void CategorySeedIsGuardedDataOnlyAndSnapshotMatchesOffline()
    {
        var migration = new SeedGeneralJobCategories();
        var operation = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations));
        Assert.Contains("WHERE NOT EXISTS", operation.Sql);
        Assert.Contains("ANY(v.\"Aliases\")", operation.Sql);
        Assert.Contains("c.\"Id\" = v.\"Id\"", operation.Sql);
        Assert.Empty(migration.DownOperations);
        foreach (var forbidden in new[] { "ALTER ", "DROP ", "DELETE ", "UPDATE ", "JobId1", "xmin" })
            Assert.DoesNotContain(forbidden, operation.Sql);
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only", o => o.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations).Select(c => c.CommandText));
        Assert.Contains("INSERT INTO \"Categories\"", sql);
        Assert.DoesNotContain("INSERT INTO \"Jobs\"", sql);
    }
}
