using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class NotificationRelationshipModelTests
{
    [Fact]
    public void NotificationHasOnlyTheIntendedUserRelationship()
    {
        using var context = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options);

        var notification = context.Model.FindEntityType(typeof(Notification))!;
        var relationship = Assert.Single(notification.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User));
        Assert.Equal(["UserId"], relationship.Properties.Select(property => property.Name));
        Assert.Equal(nameof(Notification.User), relationship.DependentToPrincipal?.Name);
        Assert.Equal(nameof(User.Notifications), relationship.PrincipalToDependent?.Name);
        Assert.DoesNotContain(notification.GetProperties(), property =>
            property.IsShadowProperty() && property.Name.StartsWith("UserId", StringComparison.Ordinal));
    }
}
