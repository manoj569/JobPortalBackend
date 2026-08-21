using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidatePhotoStorageSecurityTests
{
    [Fact]
    public async Task StorageAlwaysScopesReadReplaceAndDeleteToAuthenticatedOwnerId()
    {
        await using var context = new JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var storage = new PostgresProfilePhotoStorage(context);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var aBytes = new byte[] { 1, 2, 3 };
        await storage.StoreAsync(userA, aBytes, "image/png");
        await context.SaveChangesAsync();

        Assert.Equal(aBytes, (await storage.GetAsync(userA))!.Content);
        Assert.Null(await storage.GetAsync(userB));
        Assert.False(await storage.DeleteAsync(userB));
        Assert.Equal(aBytes, (await storage.GetAsync(userA))!.Content);

        var bBytes = new byte[] { 9, 8, 7 };
        await storage.StoreAsync(userB, bBytes, "image/webp");
        await context.SaveChangesAsync();
        Assert.Equal(aBytes, (await storage.GetAsync(userA))!.Content);
        Assert.Equal(bBytes, (await storage.GetAsync(userB))!.Content);
    }
}
