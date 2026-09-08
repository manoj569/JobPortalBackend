using System.Security.Cryptography;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPortal.Persistence.Repositories;

public sealed class ExternalJobSiteSessionStore(
    JobPortalDbContext db,
    IExternalJobSiteSessionProtector protector,
    IOptions<AIApplyOptions> options,
    TimeProvider clock) : IExternalJobSiteSessionStore
{
    public async Task<ExternalJobSiteSessionMetadata?> GetMetadataAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct)
    {
        if (!options.Value.ExternalSessions.Enabled) return null;
        var entity = await Owned(userId).SingleOrDefaultAsync(x => x.Site == site, ct);
        if (entity is null) return null;
        await ExpireIfNeededAsync(entity, ct);
        return Map(entity);
    }

    public async Task<ExternalJobSiteSessionMetadata?> GetMetadataAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        if (!options.Value.ExternalSessions.Enabled) return null;
        var entity = await Owned(userId).SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (entity is null) return null;
        await ExpireIfNeededAsync(entity, ct);
        return Map(entity);
    }

    public async Task<UsableExternalJobSiteSession?> GetActiveAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct)
    {
        if (!options.Value.ExternalSessions.Enabled || !Supports(site)) return null;
        var entity = await Owned(userId).SingleOrDefaultAsync(x => x.Site == site, ct);
        if (entity is null) return null;
        await ExpireIfNeededAsync(entity, ct);
        if (entity.Status != ExternalJobSiteSessionStatus.Active || entity.RequiresReauthentication || entity.RevokedAtUtc.HasValue)
            return null;
        try
        {
            var plaintext = protector.Unprotect(userId, site, entity.EncryptedStorageState);
            if (plaintext.Length is 0 || plaintext.Length > options.Value.ExternalSessions.MaximumStorageStateBytes)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                await MarkInvalidAsync(entity, ct);
                return null;
            }
            return new(Map(entity), plaintext);
        }
        catch (CryptographicException)
        {
            await MarkInvalidAsync(entity, ct);
            return null;
        }
    }

    public async Task<ExternalJobSiteSessionMetadata> SaveAsync(Guid userId, JobSiteIdentifier site,
        ReadOnlyMemory<byte> storageState, DateTime expiresAtUtc, long? expectedVersion, CancellationToken ct, bool allowRevokedReplacement = false)
    {
        if (!options.Value.ExternalSessions.Enabled || !Supports(site))
            throw new InvalidOperationException("Persistent sessions are not enabled for this site.");
        var maximumBytes = options.Value.ExternalSessions.MaximumStorageStateBytes;
        if (storageState.Length is 0 || storageState.Length > maximumBytes)
            throw new ArgumentOutOfRangeException(nameof(storageState), $"Storage state must be between 1 and {maximumBytes} bytes.");
        var now = clock.GetUtcNow().UtcDateTime;
        var boundedExpiry = expiresAtUtc <= now ? throw new ArgumentOutOfRangeException(nameof(expiresAtUtc))
            : expiresAtUtc > now.AddDays(options.Value.ExternalSessions.MaximumLifetimeDays)
                ? now.AddDays(options.Value.ExternalSessions.MaximumLifetimeDays) : expiresAtUtc;
        var encrypted = protector.Protect(userId, site, storageState.Span);
        try
        {
            var existing = await Owned(userId).SingleOrDefaultAsync(x => x.Site == site, ct);
            if (existing is null)
            {
                if (expectedVersion.HasValue) throw new DbUpdateConcurrencyException("External session changed.");
                existing = new ExternalJobSiteSession { UserId = userId, Site = site };
                db.ExternalJobSiteSessions.Add(existing);
            }
            else
            {
                if (expectedVersion != existing.Version || existing.Status == ExternalJobSiteSessionStatus.Revoked && !allowRevokedReplacement)
                    throw new DbUpdateConcurrencyException("External session changed or was revoked.");
                existing.Version++;
            }
            existing.EncryptedStorageState = encrypted;
            existing.EncryptionPurposeVersion = "v1";
            existing.Status = ExternalJobSiteSessionStatus.Active;
            existing.RequiresReauthentication = false;
            existing.RevokedAtUtc = null;
            existing.ExpiresAtUtc = boundedExpiry;
            existing.LastValidatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            return Map(existing);
        }
        catch { CryptographicOperations.ZeroMemory(encrypted); throw; }
    }

    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (!db.Database.IsRelational())
        {
            var entity = await Owned(userId).SingleOrDefaultAsync(x => x.Id == sessionId, ct);
            if (entity is null) return false;
            if (entity.Status == ExternalJobSiteSessionStatus.Revoked) return true;
            entity.Status = ExternalJobSiteSessionStatus.Revoked; entity.RevokedAtUtc = now;
            entity.RequiresReauthentication = true; entity.EncryptedStorageState = []; entity.Version++;
            await db.SaveChangesAsync(ct); return true;
        }
        var changed = await Owned(userId).Where(x => x.Id == sessionId && x.Status != ExternalJobSiteSessionStatus.Revoked)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.Status, ExternalJobSiteSessionStatus.Revoked)
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RequiresReauthentication, true)
                .SetProperty(x => x.EncryptedStorageState, Array.Empty<byte>())
                .SetProperty(x => x.Version, x => x.Version + 1)
                .SetProperty(x => x.UpdatedAtUtc, now), ct);
        if (changed > 0) return true;
        return await Owned(userId).AnyAsync(x => x.Id == sessionId && x.Status == ExternalJobSiteSessionStatus.Revoked, ct);
    }

    public async Task<bool> RequireReauthenticationAsync(Guid userId, Guid sessionId, long expectedVersion, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (!db.Database.IsRelational())
        {
            var entity = await Owned(userId).SingleOrDefaultAsync(x => x.Id == sessionId && x.Version == expectedVersion && x.Status == ExternalJobSiteSessionStatus.Active, ct);
            if (entity is null) return false;
            entity.Status = ExternalJobSiteSessionStatus.RequiresReauthentication; entity.RequiresReauthentication = true;
            entity.EncryptedStorageState = []; entity.Version++; await db.SaveChangesAsync(ct); return true;
        }
        return await Owned(userId).Where(x => x.Id == sessionId && x.Version == expectedVersion && x.Status == ExternalJobSiteSessionStatus.Active)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ExternalJobSiteSessionStatus.RequiresReauthentication)
                .SetProperty(x => x.RequiresReauthentication, true).SetProperty(x => x.EncryptedStorageState, Array.Empty<byte>())
                .SetProperty(x => x.Version, x => x.Version + 1).SetProperty(x => x.UpdatedAtUtc, now), ct) == 1;
    }

    private IQueryable<ExternalJobSiteSession> Owned(Guid userId) => db.ExternalJobSiteSessions.Where(x => x.UserId == userId && !x.IsDeleted);
    private bool Supports(JobSiteIdentifier site) => site switch
    {
        JobSiteIdentifier.Workday => Enabled(options.Value.Sites.Workday),
        JobSiteIdentifier.LinkedIn => Enabled(options.Value.Sites.LinkedIn),
        JobSiteIdentifier.Naukri => Enabled(options.Value.Sites.Naukri),
        JobSiteIdentifier.Indeed => Enabled(options.Value.Sites.Indeed),
        JobSiteIdentifier.Foundit => Enabled(options.Value.Sites.Foundit),
        JobSiteIdentifier.Wellfound => Enabled(options.Value.Sites.Wellfound),
        _ => false
    };
    private static bool Enabled(AIApplySiteSetting setting) => setting.Enabled && setting.SupportsPersistentSession && setting.PersistentSessionValidatedForProduction;
    private async Task ExpireIfNeededAsync(ExternalJobSiteSession entity, CancellationToken ct)
    {
        if (entity.Status != ExternalJobSiteSessionStatus.Active || entity.ExpiresAtUtc > clock.GetUtcNow().UtcDateTime) return;
        entity.Status = ExternalJobSiteSessionStatus.Expired; entity.RequiresReauthentication = true;
        entity.EncryptedStorageState = []; entity.Version++;
        await db.SaveChangesAsync(ct);
    }
    private async Task MarkInvalidAsync(ExternalJobSiteSession entity, CancellationToken ct)
    {
        entity.Status = ExternalJobSiteSessionStatus.Invalid; entity.RequiresReauthentication = true;
        entity.EncryptedStorageState = []; entity.Version++;
        await db.SaveChangesAsync(ct);
    }
    private static ExternalJobSiteSessionMetadata Map(ExternalJobSiteSession x) => new(x.Id, x.UserId, x.Site, x.Status,
        x.CreatedAtUtc, x.UpdatedAtUtc ?? x.CreatedAtUtc, x.ExpiresAtUtc, x.LastValidatedAtUtc, x.RequiresReauthentication, x.Version);
}
