using JobPortal.Application.Features.Support;
using Microsoft.Extensions.Configuration;

namespace JobPortal.Infrastructure.Storage;

// Private disk storage follows LocalResumeStorage; keys never become public static-file URLs.
public sealed class LocalSupportScreenshotStorage : ISupportScreenshotStorage
{
    private readonly string root;

    public LocalSupportScreenshotStorage(IConfiguration configuration)
    {
        var configured = configuration[$"{SupportSettings.SectionName}:ScreenshotRootPath"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            var resumeRoot = configuration["ResumeStorage:RootPath"];
            configured = Path.Combine(string.IsNullOrWhiteSpace(resumeRoot) ? "private-storage" : resumeRoot, "support-screenshots");
        }
        root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured));
        if (root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(x => x.Equals("wwwroot", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Support screenshots must be outside the public web root.");
    }

    public async Task<string> StoreAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        _ = SupportScreenshotValidation.ContentType(extension);
        var key = $"{Guid.NewGuid():N}{extension}";
        var path = Resolve(key);
        Directory.CreateDirectory(root);
        var created = false;
        try
        {
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            created = true;
            var block = new byte[81920];
            var written = 0;
            int count;
            while ((count = await content.ReadAsync(block, cancellationToken)) > 0)
            {
                written = checked(written + count);
                if (written > SupportScreenshotValidation.MaximumBytes) throw new IOException("Screenshot exceeds storage limit.");
                await destination.WriteAsync(block.AsMemory(0, count), cancellationToken);
            }
        }
        catch
        {
            // Remove partially written files, including on cancellation.
            if (created && File.Exists(path)) File.Delete(path);
            throw;
        }
        return key;
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('/') || key.Contains('\\') ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(key), "N", out _) ||
            Path.GetExtension(key) is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new InvalidOperationException("Invalid support screenshot storage key.");
        var path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid support screenshot storage key.");
        return path;
    }
}
