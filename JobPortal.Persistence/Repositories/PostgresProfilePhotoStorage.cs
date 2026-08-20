using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class PostgresProfilePhotoStorage(JobPortalDbContext context) : IProfilePhotoStorage
{
    public async Task<StoredProfilePhoto?> GetAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        await context.CandidateProfilePhotos.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new StoredProfilePhoto(x.Content, x.ContentType, x.SizeBytes, x.Version))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Guid> StoreAsync(
        Guid userId, byte[] content, string contentType,
        CancellationToken cancellationToken = default)
    {
        var photo = await context.CandidateProfilePhotos
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (photo is null)
        {
            photo = new CandidateProfilePhoto { UserId = userId };
            await context.CandidateProfilePhotos.AddAsync(photo, cancellationToken);
        }
        photo.Content = content;
        photo.ContentType = contentType;
        photo.SizeBytes = content.Length;
        photo.Version = Guid.NewGuid();
        photo.UpdatedAtUtc = DateTime.UtcNow;
        return photo.Version;
    }

    public async Task<bool> DeleteAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var photo = await context.CandidateProfilePhotos
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (photo is null) return false;
        context.CandidateProfilePhotos.Remove(photo);
        return true;
    }
}
