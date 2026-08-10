using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PostgresPersistenceConfigurationTests
{
    [Fact]
    public void ModelUsesPostgresCompatiblePartialIndexPredicates()
    {
        using var context = CreateContext();

        var user = context.Model.FindEntityType(typeof(User))
            ?? throw new InvalidOperationException("User metadata was not found.");
        var resetTokenIndex = Assert.Single(user.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(User.PasswordResetTokenHash)]));

        Assert.Equal(
            "\"PasswordResetTokenHash\" IS NOT NULL AND \"IsDeleted\" = FALSE",
            resetTokenIndex.GetFilter());
    }

    [Theory]
    [InlineData(typeof(Membership))]
    [InlineData(typeof(Payment))]
    [InlineData(typeof(ApplicationQuotaUsage))]
    public void ConcurrencyUsesPostgresXminInsteadOfSqlServerRowVersion(Type entityType)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"{entityType.Name} metadata was not found.");

        Assert.Null(entity.FindProperty("RowVersion"));

        var xmin = entity.FindProperty("xmin");
        Assert.NotNull(xmin);
        Assert.True(xmin!.IsConcurrencyToken);
        Assert.True(xmin.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate);
    }

    private static JobPortalDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=jobportal_test;Username=postgres;SSL Mode=Disable")
            .Options;

        return new JobPortalDbContext(options);
    }
}
