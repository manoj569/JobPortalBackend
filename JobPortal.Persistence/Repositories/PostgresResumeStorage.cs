using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;

namespace JobPortal.Persistence.Repositories;

/// <summary>Owner-scoped resume blob storage backed by PostgreSQL, not instance-local disk.</summary>
public sealed class PostgresResumeStorage(IDbContextFactory<JobPortalDbContext> contexts, string? legacyRootPath = null) : IResumeStorage
{
    private const int MaximumDocumentBytes = 10 * 1024 * 1024;
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.Ordinal)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };
    private readonly string? _legacyRoot = ResolveLegacyRoot(legacyRootPath);

    public async Task<string> StoreAsync(Guid ownerUserId, Stream content, string extension, Guid? resumeId = null,
        string? originalFileName = null, string? contentType = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateOwner(ownerUserId);
        extension = extension.ToLowerInvariant();
        if (!ContentTypes.TryGetValue(extension, out var expectedContentType))
            throw new InvalidDataException("Unsupported resume document extension.");
        if (contentType is not null && !string.Equals(contentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Resume document content type does not match its extension.");

        var bytes = await ReadBoundedAsync(content, cancellationToken);
        var safeFileName = SafeFileName(originalFileName, extension);
        var key = $"{Guid.NewGuid():N}{extension}";
        var document = new ResumeDocumentBlob
        {
            OwnerUserId = ownerUserId,
            ResumeId = resumeId,
            StorageKey = key,
            OriginalFileName = safeFileName,
            ContentType = expectedContentType,
            Extension = extension,
            FileLength = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Content = bytes
        };

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.ResumeDocumentBlobs.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public async Task<Stream?> OpenReadAsync(Guid ownerUserId, string storageKey, Guid? resumeId = null,
        string? originalFileName = null, string? contentType = null, CancellationToken cancellationToken = default)
    {
        ValidateOwner(ownerUserId);
        ValidateStorageKey(storageKey);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var document = await db.ResumeDocumentBlobs.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.StorageKey == storageKey)
            .Select(x => new { x.Content, x.FileLength, x.Sha256 })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null)
        {
            if (!await HasOwnerReferenceAsync(db, ownerUserId, storageKey, cancellationToken)) return null;
            var legacyBytes = await TryReadLegacyAsync(storageKey, cancellationToken);
            if (legacyBytes is null) return null;
            var extension = Path.GetExtension(storageKey);
            var migrated = new ResumeDocumentBlob
            {
                OwnerUserId = ownerUserId,
                ResumeId = resumeId,
                StorageKey = storageKey,
                OriginalFileName = SafeFileName(originalFileName, extension),
                ContentType = ContentTypes[extension],
                Extension = extension,
                FileLength = legacyBytes.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(legacyBytes)),
                Content = legacyBytes
            };
            db.ResumeDocumentBlobs.Add(migrated);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new MemoryStream(legacyBytes, writable: false);
            }
            catch (DbUpdateException)
            {
                // A concurrent request may have promoted the same old object. Only return the
                // winning copy when it is owned by this caller; storage keys are never authority.
                await using var retry = await contexts.CreateDbContextAsync(cancellationToken);
                var winner = await retry.ResumeDocumentBlobs.AsNoTracking()
                    .Where(x => x.OwnerUserId == ownerUserId && x.StorageKey == storageKey)
                    .Select(x => new { x.Content, x.FileLength, x.Sha256 })
                    .SingleOrDefaultAsync(cancellationToken);
                if (winner is null || winner.Content.LongLength != winner.FileLength ||
                    !string.Equals(Convert.ToHexString(SHA256.HashData(winner.Content)), winner.Sha256, StringComparison.Ordinal))
                    return null;
                return new MemoryStream(winner.Content, writable: false);
            }
        }
        if (document.Content.LongLength != document.FileLength ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(document.Content)), document.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Stored resume document integrity check failed.");
        return new MemoryStream(document.Content, writable: false);
    }

    public async Task DeleteAsync(Guid ownerUserId, string storageKey, CancellationToken cancellationToken = default)
    {
        ValidateOwner(ownerUserId);
        ValidateStorageKey(storageKey);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var ownedBlobs = db.ResumeDocumentBlobs.Where(x => x.OwnerUserId == ownerUserId && x.StorageKey == storageKey);
        if (db.Database.IsRelational())
            await ownedBlobs.ExecuteDeleteAsync(cancellationToken);
        else
        {
            var blob = await ownedBlobs.SingleOrDefaultAsync(cancellationToken);
            if (blob is not null)
            {
                db.ResumeDocumentBlobs.Remove(blob);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        if (_legacyRoot is not null &&
            await HasOwnerReferenceAsync(db, ownerUserId, storageKey, cancellationToken))
        {
            var legacyPath = Path.GetFullPath(Path.Combine(_legacyRoot, storageKey));
            if (legacyPath.StartsWith(_legacyRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(legacyPath)) File.Delete(legacyPath);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var result = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await content.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (result.Length + count > MaximumDocumentBytes)
                throw new InvalidDataException("Resume document exceeds the storage size limit.");
            await result.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        if (result.Length == 0) throw new InvalidDataException("Resume document cannot be empty.");
        return result.ToArray();
    }

    private static string SafeFileName(string? fileName, string extension)
    {
        var safe = string.IsNullOrWhiteSpace(fileName) ? "resume" + extension : Path.GetFileName(fileName.Trim());
        if (safe.Length > 255 || safe.Any(char.IsControl) || !string.Equals(Path.GetExtension(safe), extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Resume document filename is invalid.");
        return safe;
    }

    private static void ValidateOwner(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty) throw new ArgumentException("A resume document owner is required.", nameof(ownerUserId));
    }

    private static void ValidateStorageKey(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.GetFileName(storageKey) != storageKey ||
            !ContentTypes.ContainsKey(Path.GetExtension(storageKey)) ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(storageKey), "N", out _))
            throw new InvalidOperationException("Invalid resume storage key.");
    }

    private async Task<byte[]?> TryReadLegacyAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (_legacyRoot is null) return null;
        var path = Path.GetFullPath(Path.Combine(_legacyRoot, storageKey));
        if (!path.StartsWith(_legacyRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadBoundedAsync(source, cancellationToken);
    }

    private static async Task<bool> HasOwnerReferenceAsync(JobPortalDbContext db, Guid ownerUserId,
        string storageKey, CancellationToken cancellationToken)
    {
        if (await db.Users.AsNoTracking().AnyAsync(x => x.Id == ownerUserId && x.ResumeStorageKey == storageKey, cancellationToken) ||
            await db.JobApplications.AsNoTracking().AnyAsync(x => x.UserId == ownerUserId && x.ResumeStorageKey == storageKey, cancellationToken) ||
            await db.AIApplyApplications.AsNoTracking().AnyAsync(x => x.UserId == ownerUserId && x.ResumeStorageKey == storageKey, cancellationToken))
            return true;

        // Before durable blob storage, AI Resume master/artifact files were held on the
        // configured disk and referenced only from owner-scoped JSON snapshots.
        if (!db.Database.IsRelational()) return false;
        return await db.Database.SqlQuery<bool>($"""
            SELECT
                EXISTS (
                    SELECT 1 FROM "AIResumeSessions"
                    WHERE "UserId" = {ownerUserId} AND "IsDeleted" = FALSE
                      AND "EvidenceJson" @> jsonb_build_object(
                          'masterDocument', jsonb_build_object('storageKey', {storageKey}))
                )
                OR EXISTS (
                    SELECT 1 FROM "TailoredResumes"
                    WHERE "UserId" = {ownerUserId} AND "IsDeleted" = FALSE
                      AND ("ContentJson" @> jsonb_build_object(
                               'masterDocument', jsonb_build_object('storageKey', {storageKey}))
                           OR "ContentJson" @> jsonb_build_object(
                               'artifact', jsonb_build_object('storageKey', {storageKey})))
                ) AS "Value"
            """).SingleAsync(cancellationToken);
    }

    private static string? ResolveLegacyRoot(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var root = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured));
        if (root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(x => x.Equals("wwwroot", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Resume storage must be outside the public web root.");
        return root;
    }
}
