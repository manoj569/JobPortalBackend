using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ReferralMarketplaceMigrationTests
{
    [Fact]
    public void MigrationAddsOnlyRequestsSlotsAndTheirConstraintsAndIndexes()
    {
        var migration = new AddReferralMarketplacePhase1();
        Assert.All(migration.UpOperations, operation => Assert.True(operation is AddColumnOperation or CreateTableOperation or CreateIndexOperation or AddCheckConstraintOperation));
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal("JobReferrals", column.Table); Assert.Equal("ReferralSlots", column.Name); Assert.Equal(1, column.DefaultValue);
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("ReferralRequests", table.Name);
        Assert.All(table.ForeignKeys, key => Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, key.OnDelete));
        Assert.DoesNotContain(table.Columns, c => c.Name.Contains("Share", StringComparison.OrdinalIgnoreCase));
        Assert.All(migration.UpOperations.OfType<CreateIndexOperation>(), index => Assert.Equal("ReferralRequests", index.Table));
    }
    [Fact]
    public void ModelHasPermanentDuplicateProtectionAndExplicitScopedForeignKeys()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=careerharbor_referral_test").Options);
        var entity = db.Model.FindEntityType(typeof(ReferralRequest))!;
        var unique = Assert.Single(entity.GetIndexes(), x => x.IsUnique);
        Assert.Equal(new[] { "CandidateUserId", "JobReferralId" }, unique.Properties.Select(x => x.Name).ToArray());
        Assert.Null(unique.GetFilter());
        Assert.Equal(4, entity.GetForeignKeys().Count());
        Assert.DoesNotContain(entity.GetProperties(), p => p.IsShadowProperty());
        Assert.True(entity.FindProperty(nameof(ReferralRequest.Status))!.IsConcurrencyToken);
        var design = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ReferralRequest))!;
        Assert.Contains(design.GetCheckConstraints(), x => x.Name == "CK_ReferralRequests_Acceptance" && x.Sql.Contains("\"QuotaPeriodEndUtc\" IS NOT NULL", StringComparison.Ordinal));
    }
}
