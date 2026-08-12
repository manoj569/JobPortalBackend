namespace JobPortal.Application.Common.Validation;

public static class SafeHttpsUrl
{
    public static bool IsValid(string? value, bool optional = true)
    {
        if (string.IsNullOrWhiteSpace(value)) return optional;
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host) &&
            string.IsNullOrEmpty(uri.UserInfo);
    }
}
