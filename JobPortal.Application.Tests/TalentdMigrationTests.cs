using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class TalentdMigrationTests
{
    [Fact]
    public void RegistrationIsEnabledAutoPublishingAndIdempotentWithoutSchemaChanges()
    {
        var migration = new AddTalentdAcquisitionSource();
        var sql = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations)).Sql;
        Assert.Contains("'talentd'", sql);
        Assert.Contains("'Talentd'", sql);
        Assert.Contains("\"IsEnabled\" = TRUE", sql);
        Assert.Contains("\"AutoPublishEnabled\" = TRUE", sql);
        Assert.Contains("\"IsDeleted\" = FALSE", sql);
        Assert.Contains("WHERE NOT EXISTS", sql);
        Assert.DoesNotContain("website-crawler", sql);
        Assert.DoesNotContain("serpapi", sql);
        Assert.DoesNotContain("DELETE", Assert.IsType<SqlOperation>(Assert.Single(migration.DownOperations)).Sql);
    }
}
