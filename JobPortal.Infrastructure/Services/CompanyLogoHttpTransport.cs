using System.Net;
using System.Net.Sockets;

namespace JobPortal.Infrastructure.Services;

internal static class CompanyLogoHttpTransport
{
    private static readonly string[] InternalSuffixes = [".localhost", ".local", ".internal", ".lan", ".home", ".test", ".invalid"];
    private static readonly IPAddress AzurePlatformAddress = IPAddress.Parse("168.63.129.16");
    private static bool IsPublicAddress(IPAddress address) => SuccessFactorsHttpTransport.IsPublicAddress(address) &&
        !AzurePlatformAddress.Equals(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    internal static bool IsSafeUrl(Uri uri) => uri.AbsoluteUri.Length <= 2048 &&
        uri.Scheme is "http" or "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        !uri.Host.EndsWith('.') &&
        (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)
            ? IsPublicAddress(address)
            : uri.Host.Contains('.', StringComparison.Ordinal) &&
              !InternalSuffixes
                  .Any(suffix => uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        ConnectCallback = ConnectAsync,
        MaxResponseHeadersLength = 32,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        if (context.DnsEndPoint.Port is not (80 or 443)) throw new HttpRequestException("Unsupported logo port.");
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        SuccessFactorsHttpTransport.ValidateAddresses(addresses);
        if (addresses.Any(address => !IsPublicAddress(address))) throw new HttpRequestException("Platform endpoints are not public company hosts.");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("Company website could not be reached.");
    }
}
