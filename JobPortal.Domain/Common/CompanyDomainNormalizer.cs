namespace JobPortal.Domain.Common;

public static class CompanyDomainNormalizer
{
    public static string? Normalize(string? websiteUrl)
    {
        if (string.IsNullOrWhiteSpace(websiteUrl)) return null;
        var value = websiteUrl.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
