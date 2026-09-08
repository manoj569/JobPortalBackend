using System.Globalization;

namespace JobPortal.API.Startup;

public static class PlatformPortBinding
{
    public static string? ResolveUrl(string? configuredPort)
    {
        if (string.IsNullOrWhiteSpace(configuredPort))
            return null;

        if (!int.TryParse(configuredPort.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
            throw new InvalidOperationException("PORT must be an integer between 1 and 65535.");

        return $"http://0.0.0.0:{port}";
    }
}
