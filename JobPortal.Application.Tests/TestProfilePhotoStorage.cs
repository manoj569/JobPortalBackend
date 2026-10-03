using JobPortal.Application.Abstractions.Candidates;

namespace JobPortal.Application.Tests;

internal sealed class TestProfilePhotoStorage : IProfilePhotoStorage
{
    private readonly Dictionary<Guid, StoredProfilePhoto> _photos = [];

    public Task<StoredProfilePhoto?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        _photos.TryGetValue(userId, out var photo);
        return Task.FromResult(photo);
    }

    public Task<Guid> StoreAsync(
        Guid userId,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var version = Guid.NewGuid();

        _photos[userId] = new StoredProfilePhoto(
            content,
            contentType,
            content.Length,
            version);

        return Task.FromResult(version);
    }

    public Task<bool> DeleteAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_photos.Remove(userId));
    }
}
