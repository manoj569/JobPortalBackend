using System.Security.Cryptography;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Domain.Enums;
using Microsoft.AspNetCore.DataProtection;

namespace JobPortal.Infrastructure.AIApply;

public sealed class ExternalJobSiteSessionProtector(IDataProtectionProvider provider) : IExternalJobSiteSessionProtector
{
    private const string Version = "v1";

    public byte[] Protect(Guid userId, JobSiteIdentifier site, ReadOnlySpan<byte> plaintext)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User ID is required.", nameof(userId));
        if (plaintext.IsEmpty) throw new ArgumentException("Storage state is required.", nameof(plaintext));
        return Protector(userId, site).Protect(plaintext.ToArray());
    }

    public byte[] Unprotect(Guid userId, JobSiteIdentifier site, ReadOnlySpan<byte> ciphertext)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User ID is required.", nameof(userId));
        if (ciphertext.IsEmpty) throw new CryptographicException("Protected session state is unavailable.");
        return Protector(userId, site).Unprotect(ciphertext.ToArray());
    }

    private IDataProtector Protector(Guid userId, JobSiteIdentifier site) => provider.CreateProtector(
        "CareerHarbor", "AIApply", "ExternalJobSiteSession", Version,
        userId.ToString("N"), ((int)site).ToString(System.Globalization.CultureInfo.InvariantCulture));
}
