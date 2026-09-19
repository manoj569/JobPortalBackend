using System.Security.Cryptography;
using System.Text;

namespace JobPortal.Application.Abstractions.Jobs;

public static class ApplicationUrlIdentity
{
    public static string? Hash(string? applicationUrl)
    {
        if (!Uri.TryCreate(applicationUrl?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        var canonical = new UrlCanonicalizer().Canonicalize(applicationUrl);
        return canonical is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
