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

public sealed class CareerTrustMigrationTests
{
    [Fact]
    public void MigrationAddsOnlyThreeTrustTablesAndRestrictiveForeignKeys()
    {
        var operations = new AddCareerGuidanceReviewsDisputesAndTrust().UpOperations;
        Assert.All(operations, o => Assert.True(o is CreateTableOperation or CreateIndexOperation));
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "CareerGuidanceDisputeEvidence", "CareerGuidanceDisputes", "CareerGuidanceReviews" }, tables.Select(t => t.Name).Order().ToArray());
        Assert.All(tables.SelectMany(t => t.ForeignKeys), fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.All(tables, t => Assert.NotEmpty(t.CheckConstraints));
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        foreach (var table in new[] { "CareerGuidanceReviews", "CareerGuidanceDisputes" })
            Assert.Contains(indexes, i => i.Table == table && i.IsUnique && i.Columns.SequenceEqual(new[] { "BookingId" }) && i.Filter == null);
        Assert.Contains(indexes, i => i.Table == "CareerGuidanceDisputeEvidence" && i.IsUnique && i.Columns.SequenceEqual(new[] { "DisputeId", "RequestId" }));
        Assert.Contains(tables.Single(t => t.Name == "CareerGuidanceReviews").CheckConstraints, c => c.Name == "CK_CGReview_Rating");
        Assert.Contains(tables.Single(t => t.Name == "CareerGuidanceDisputes").CheckConstraints, c => c.Name == "CK_CGDispute_Resolution");
    }

    [Fact]
    public void OfflineSqlModelAndSnapshotAreConsistentWithoutUnrelatedChanges()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only", o => o.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>()
            .Generate(new AddCareerGuidanceReviewsDisputesAndTrust().UpOperations, model).Select(c => c.CommandText));
        foreach (var forbidden in new[] { "DROP ", "ALTER ", "INSERT INTO ", "UPDATE ", "DELETE FROM ", "xmin", "JobId1" }) Assert.DoesNotContain(forbidden, sql);
        Assert.Contains("CREATE UNIQUE INDEX", sql);
        foreach (var type in new[] { typeof(CareerGuidanceReview), typeof(CareerGuidanceDispute) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.True(entity.FindProperty("Revision")!.IsConcurrencyToken);
            Assert.DoesNotContain(entity.GetProperties(), p => p.IsShadowProperty());
        }
        Assert.DoesNotContain(model.FindEntityType(typeof(CareerGuidanceDisputeEvidence))!.GetProperties(), p => p.IsShadowProperty());
        Assert.False(db.Database.HasPendingModelChanges());
        var designer = new AddCareerGuidanceReviewsDisputesAndTrust().TargetModel;
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        Assert.Equal(snapshot.GetEntityTypes().Select(e => e.Name).Order(), designer.GetEntityTypes().Select(e => e.Name).Order());
    }
}
