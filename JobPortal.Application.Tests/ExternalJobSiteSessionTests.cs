using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIApply;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.API.Health;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ExternalJobSiteSessionTests
{
    private static readonly byte[] State = Encoding.UTF8.GetBytes("{\"cookies\":[{\"name\":\"session\",\"value\":\"secret-cookie\",\"domain\":\".linkedin.com\",\"path\":\"/\"}],\"origins\":[]}");

    [Fact]
    public void ProtectionIsBoundToUserAndSiteAndRejectsTampering()
    {
        var protector = Protector(); var user = Guid.NewGuid();
        var encrypted = protector.Protect(user, JobSiteIdentifier.LinkedIn, State);
        Assert.DoesNotContain("secret-cookie", Encoding.UTF8.GetString(encrypted));
        Assert.Equal(State, protector.Unprotect(user, JobSiteIdentifier.LinkedIn, encrypted));
        Assert.Throws<CryptographicException>(() => protector.Unprotect(Guid.NewGuid(), JobSiteIdentifier.LinkedIn, encrypted));
        Assert.Throws<CryptographicException>(() => protector.Unprotect(user, JobSiteIdentifier.Naukri, encrypted));
        encrypted[^1] ^= 0x1;
        Assert.Throws<CryptographicException>(() => protector.Unprotect(user, JobSiteIdentifier.LinkedIn, encrypted));
    }

    [Fact]
    public async Task StorePersistsOnlyCiphertextAndReturnsOwnedDecryptedState()
    {
        await using var db = Context(); var user = Guid.NewGuid(); var store = Store(db);
        var saved = await store.SaveAsync(user, JobSiteIdentifier.LinkedIn, State, Now.AddDays(60), null, default);
        var row = await db.ExternalJobSiteSessions.SingleAsync();
        Assert.DoesNotContain("secret-cookie", Encoding.UTF8.GetString(row.EncryptedStorageState));
        Assert.Equal(Now.AddDays(30), saved.ExpiresAtUtc);
        var active = await store.GetActiveAsync(user, JobSiteIdentifier.LinkedIn, default);
        Assert.NotNull(active); Assert.Equal(State, active!.StorageState);
        Assert.Null(await store.GetActiveAsync(Guid.NewGuid(), JobSiteIdentifier.LinkedIn, default));
        CryptographicOperations.ZeroMemory(active.StorageState);
    }

    [Fact]
    public async Task PersistedEncryptedSessionSurvivesUnrelatedCaptureOwnerTermination()
    {
        var database = Guid.NewGuid().ToString();
        var user = Guid.NewGuid();
        var protector = Protector();
        Guid sessionId;
        byte[] ciphertext;

        await using (var ownerProcess = Context(database))
        {
            var store = new ExternalJobSiteSessionStore(ownerProcess, protector, Options.Create(EnabledOptions()), new FixedClock(Now));
            var saved = await store.SaveAsync(user, JobSiteIdentifier.LinkedIn, State, Now.AddDays(7), null, default);
            sessionId = saved.Id;
            ciphertext = (await ownerProcess.ExternalJobSiteSessions.AsNoTracking().SingleAsync()).EncryptedStorageState.ToArray();
            Assert.DoesNotContain("secret-cookie", Encoding.UTF8.GetString(ciphertext));
            Assert.Equal(ExternalJobSiteSessionStatus.Active, saved.Status);
        }

        // A capture owner is process-local; terminating it must not mutate durable session state.
        await using var survivingProcess = Context(database);
        var survivingStore = new ExternalJobSiteSessionStore(survivingProcess, protector, Options.Create(EnabledOptions()), new FixedClock(Now));
        var row = await survivingProcess.ExternalJobSiteSessions.AsNoTracking().SingleAsync();
        var metadata = await survivingStore.GetMetadataAsync(user, sessionId, default);
        var restored = await survivingStore.GetActiveAsync(user, JobSiteIdentifier.LinkedIn, default);

        Assert.Equal(ciphertext, row.EncryptedStorageState);
        Assert.Equal(ExternalJobSiteSessionStatus.Active, metadata!.Status);
        Assert.Equal(State, restored!.StorageState);
        CryptographicOperations.ZeroMemory(restored.StorageState);
    }

    [Fact]
    public async Task OversizedStateIsRejectedBeforePersistence()
    {
        await using var db = Context(); var store = Store(db, maximumBytes: 4096);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(Guid.NewGuid(), JobSiteIdentifier.LinkedIn, new byte[4097], Now.AddDays(1), null, default));
        Assert.Empty(db.ExternalJobSiteSessions);
    }

    [Fact]
    public async Task ExpiredAndReauthenticationSessionsAreNeverUsable()
    {
        await using var db = Context(); var user = Guid.NewGuid(); var store = Store(db);
        var saved = await store.SaveAsync(user, JobSiteIdentifier.LinkedIn, State, Now.AddDays(1), null, default);
        Assert.True(await store.RequireReauthenticationAsync(user, saved.Id, saved.Version, default));
        Assert.Null(await store.GetActiveAsync(user, JobSiteIdentifier.LinkedIn, default));
        Assert.Equal(ExternalJobSiteSessionStatus.RequiresReauthentication, (await store.GetMetadataAsync(user, saved.Id, default))!.Status);
    }

    [Fact]
    public async Task ServerExpiredSessionIsClearedAndFailsClosed()
    {
        await using var db = Context(); var user = Guid.NewGuid(); var protector = Protector();
        db.ExternalJobSiteSessions.Add(new ExternalJobSiteSession
        {
            UserId = user, Site = JobSiteIdentifier.LinkedIn, Status = ExternalJobSiteSessionStatus.Active,
            EncryptedStorageState = protector.Protect(user, JobSiteIdentifier.LinkedIn, State),
            ExpiresAtUtc = Now.AddMinutes(-1), CreatedAtUtc = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var store = new ExternalJobSiteSessionStore(db, protector, Options.Create(EnabledOptions()), new FixedClock(Now));
        Assert.Null(await store.GetActiveAsync(user, JobSiteIdentifier.LinkedIn, default));
        var row = await db.ExternalJobSiteSessions.SingleAsync();
        Assert.Equal(ExternalJobSiteSessionStatus.Expired, row.Status); Assert.Empty(row.EncryptedStorageState);
    }

    [Fact]
    public async Task RevokeIsOwnedImmediateAndIdempotent()
    {
        await using var db = Context(); var user = Guid.NewGuid(); var store = Store(db);
        var saved = await store.SaveAsync(user, JobSiteIdentifier.LinkedIn, State, Now.AddDays(1), null, default);
        Assert.False(await store.RevokeAsync(Guid.NewGuid(), saved.Id, default));
        Assert.True(await store.RevokeAsync(user, saved.Id, default));
        Assert.True(await store.RevokeAsync(user, saved.Id, default));
        Assert.Null(await store.GetActiveAsync(user, JobSiteIdentifier.LinkedIn, default));
        var row = await db.ExternalJobSiteSessions.SingleAsync();
        Assert.Empty(row.EncryptedStorageState); Assert.Equal(ExternalJobSiteSessionStatus.Revoked, row.Status);
    }

    [Fact]
    public async Task StaleUpdateCannotResurrectRevokedSession()
    {
        var name = Guid.NewGuid().ToString(); var user = Guid.NewGuid();
        await using var first = Context(name); var saved = await Store(first).SaveAsync(user, JobSiteIdentifier.LinkedIn, State, Now.AddDays(1), null, default);
        await using var stale = Context(name); var staleStore = Store(stale); Assert.NotNull(await staleStore.GetMetadataAsync(user, saved.Id, default));
        await using var revoker = Context(name); Assert.True(await Store(revoker).RevokeAsync(user, saved.Id, default));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => staleStore.SaveAsync(user, JobSiteIdentifier.LinkedIn, State, Now.AddDays(1), saved.Version, default));
        await using var verify = Context(name); Assert.Equal(ExternalJobSiteSessionStatus.Revoked, (await Store(verify).GetMetadataAsync(user, saved.Id, default))!.Status);
    }

    [Theory]
    [InlineData(false, true, null, true)]
    [InlineData(true, true, null, false)]
    [InlineData(true, true, "<absolute>", true)]
    [InlineData(true, false, null, false)]
    public void ConfigurationFailsClosedAsExpected(bool enabled, bool requireKeys, string? path, bool valid)
    {
        if (path == "<absolute>")
            path = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "shared-keys");
        var options = EnabledOptions(); options.ExternalSessions.Enabled = enabled;
        options.ExternalSessions.RequirePersistentDataProtectionKeys = requireKeys;
        options.ExternalSessions.DataProtectionKeysPath = path;
        Assert.Equal(valid, new AIApplyOptionsValidator(false).Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0, 262144)]
    [InlineData(31, 262144)]
    [InlineData(30, 1024)]
    [InlineData(30, 1048577)]
    public void InvalidBoundsFailValidation(int days, int bytes)
    {
        var options = EnabledOptions(); options.ExternalSessions.MaximumLifetimeDays = days;
        options.ExternalSessions.MaximumStorageStateBytes = bytes;
        Assert.False(new AIApplyOptionsValidator(false).Validate(null, options).Succeeded);
    }

    [Fact]
    public void EntityAndPublicDtoContainNoCredentialOrSessionMaterialFields()
    {
        var forbidden = new[] { "Password", "Otp", "Mfa", "Plaintext", "Cookie", "AccessToken", "RefreshToken" };
        var entityNames = typeof(ExternalJobSiteSession).GetProperties().Select(x => x.Name).ToArray();
        var dtoNames = typeof(ExternalJobSiteSessionResponse).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(entityNames, n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(dtoNames, n => forbidden.Append("StorageState").Append("Encrypted").Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ModelHasOwnerSiteUniqueIndexAndByteaCiphertext()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        var type = db.Model.FindEntityType(typeof(ExternalJobSiteSession))!;
        Assert.Contains(type.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["UserId", "Site"]));
        Assert.Equal("bytea", type.FindProperty(nameof(ExternalJobSiteSession.EncryptedStorageState))!.GetColumnType());
    }

    [Fact]
    public async Task ReadinessIgnoresDisabledSessionsAndFailsWhenKeyRepositoryIsMissing()
    {
        var disabled = new AIApplyExternalSessionHealthCheck(Options.Create(new AIApplyOptions()));
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, (await disabled.CheckHealthAsync(new())).Status);
        var enabled = EnabledOptions(); enabled.ExternalSessions.DataProtectionKeysPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var health = new AIApplyExternalSessionHealthCheck(Options.Create(enabled));
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, (await health.CheckHealthAsync(new())).Status);
    }

    private static readonly DateTime Now = new(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);
    private static ExternalJobSiteSessionProtector Protector() => new(new EphemeralDataProtectionProvider());
    private static JobPortalDbContext Context(string? name = null) => new(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options);
    private static ExternalJobSiteSessionStore Store(JobPortalDbContext db, int maximumBytes = 262144) => new(db, Protector(), Options.Create(EnabledOptions(maximumBytes)), new FixedClock(Now));
    private static AIApplyOptions EnabledOptions(int maximumBytes = 262144) => new()
    {
        Enabled = true, MaxConcurrentApplications = 2, PerUserMaxConcurrentApplications = 1,
        LeaseSeconds = 180, LeaseRenewalSeconds = 45,
        ExternalSessions = new() { Enabled = true, MaximumLifetimeDays = 30, MaximumStorageStateBytes = maximumBytes, RequirePersistentDataProtectionKeys = false },
        Sites = new() { LinkedIn = new() { Enabled = true, SupportsPersistentSession = true, PersistentSessionValidatedForProduction = true } }
    };
    private sealed class FixedClock(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
}
