using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PostgresResumeStorageTests
{
    [Fact]
    public async Task StoredSourceSurvivesFreshContextAndCannotBeReadByAnotherOwner()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var factory = new InMemoryContextFactory(options);
        var role = new Role { Name = "Candidate", NormalizedName = "CANDIDATE" };
        var owner = new User
        {
            Email = "owner@example.invalid", NormalizedEmail = "OWNER@EXAMPLE.INVALID",
            FirstName = "Owner", LastName = "Candidate", Role = role, RoleId = role.Id,
            Status = UserStatus.Active
        };
        var resumeId = Guid.NewGuid();
        await using (var seed = factory.CreateDbContext())
        {
            seed.Roles.Add(role);
            seed.Users.Add(owner);
            await seed.SaveChangesAsync();
        }

        var storage = new PostgresResumeStorage(factory);
        var bytes = "%PDF-1.7 durable source"u8.ToArray();
        var key = await storage.StoreAsync(owner.Id, new MemoryStream(bytes), ".pdf", resumeId,
            "candidate-source.pdf", "application/pdf");

        await using var freshContext = factory.CreateDbContext();
        var stored = await freshContext.ResumeDocumentBlobs.AsNoTracking().SingleAsync(x => x.StorageKey == key);
        Assert.Equal(owner.Id, stored.OwnerUserId);
        Assert.Equal(resumeId, stored.ResumeId);
        Assert.Equal("candidate-source.pdf", stored.OriginalFileName);
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.Equal(bytes.LongLength, stored.FileLength);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), stored.Sha256);
        Assert.Equal(bytes, stored.Content);

        await using var retrieved = await storage.OpenReadAsync(owner.Id, key, resumeId,
            "candidate-source.pdf", "application/pdf");
        Assert.NotNull(retrieved);
        using var result = new MemoryStream();
        await retrieved!.CopyToAsync(result);
        Assert.Equal(bytes, result.ToArray());
        Assert.Null(await storage.OpenReadAsync(Guid.NewGuid(), key));
    }

    [Fact]
    public async Task StorageRejectsInvalidKeyAndDocumentsAboveBoundedLimit()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var storage = new PostgresResumeStorage(new InMemoryContextFactory(options));

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(Guid.NewGuid(), "../secret.pdf"));
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.StoreAsync(Guid.NewGuid(),
            new MemoryStream(new byte[10 * 1024 * 1024 + 1]), ".pdf"));
    }

    [Fact]
    public async Task RecoverableLegacyFileIsOwnerCheckedAndPromotedToDurableStorage()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var factory = new InMemoryContextFactory(options);
        var role = new Role { Name = "Candidate", NormalizedName = "CANDIDATE" };
        var owner = new User
        {
            Email = "legacy@example.invalid", NormalizedEmail = "LEGACY@EXAMPLE.INVALID",
            FirstName = "Legacy", LastName = "Candidate", Role = role, RoleId = role.Id,
            Status = UserStatus.Active
        };
        var key = $"{Guid.NewGuid():N}.pdf";
        owner.ResumeStorageKey = key;
        var resumeId = Guid.NewGuid();
        var legacyRoot = Path.Combine(Path.GetTempPath(), "careerharbor-resume-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacyRoot);
        var sourceBytes = "%PDF-1.7 recovered"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(legacyRoot, key), sourceBytes);
        try
        {
            await using (var seed = factory.CreateDbContext())
            {
                seed.Roles.Add(role);
                seed.Users.Add(owner);
                await seed.SaveChangesAsync();
            }
            var storage = new PostgresResumeStorage(factory, legacyRoot);

            Assert.Null(await storage.OpenReadAsync(Guid.NewGuid(), key, resumeId, "source.pdf", "application/pdf"));
            await using var recovered = await storage.OpenReadAsync(owner.Id, key, resumeId, "source.pdf", "application/pdf");
            Assert.NotNull(recovered);
            using var bytes = new MemoryStream();
            await recovered!.CopyToAsync(bytes);
            Assert.Equal(sourceBytes, bytes.ToArray());

            await using var verify = factory.CreateDbContext();
            var promoted = await verify.ResumeDocumentBlobs.SingleAsync(x => x.StorageKey == key);
            Assert.Equal(owner.Id, promoted.OwnerUserId);
            Assert.Equal(resumeId, promoted.ResumeId);
            Assert.Equal("source.pdf", promoted.OriginalFileName);
            Assert.Equal("application/pdf", promoted.ContentType);
            Assert.Equal(sourceBytes, promoted.Content);
        }
        finally
        {
            Directory.Delete(legacyRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AnotherOwnerCannotDeleteAnUnreferencedLegacyFile()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var legacyRoot = Path.Combine(Path.GetTempPath(), "careerharbor-resume-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacyRoot);
        var key = $"{Guid.NewGuid():N}.pdf";
        var path = Path.Combine(legacyRoot, key);
        await File.WriteAllBytesAsync(path, "%PDF-1.7 private"u8.ToArray());
        try
        {
            var storage = new PostgresResumeStorage(new InMemoryContextFactory(options), legacyRoot);
            await storage.DeleteAsync(Guid.NewGuid(), key);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(legacyRoot, recursive: true);
        }
    }

    private sealed class InMemoryContextFactory(DbContextOptions<JobPortalDbContext> options)
        : IDbContextFactory<JobPortalDbContext>
    {
        public JobPortalDbContext CreateDbContext() => new(options);
        public Task<JobPortalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
