using System.Net;
using System.Net.Sockets;

namespace JobPortal.Infrastructure.Services;

// Resolve and connect to the same vetted IP: a preflight DNS check alone permits rebinding.
// Used only by the SuccessFactors named client; TLS certificate validation remains unchanged.
internal static class SuccessFactorsHttpTransport
{
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = ConnectAsync
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        if (context.DnsEndPoint.Port != 443)
            throw new HttpRequestException("SuccessFactors connections require HTTPS on the default port.");
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        ValidateAddresses(addresses);
        SocketException? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception) { socket.Dispose(); lastError = exception; }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("SuccessFactors public host could not be reached.", lastError);
    }

    internal static void ValidateAddresses(IReadOnlyCollection<IPAddress> addresses)
    {
        if (addresses.Count == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new HttpRequestException("SuccessFactors host must resolve only to public addresses.");
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 ||
                bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                bytes[0] == 192 && (bytes[1] is 0 or 168 || bytes[1] == 2 || bytes[1] == 88 && bytes[2] == 99) ||
                bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100) ||
                bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }
        // Global unicast only; reject local, multicast, mapped/transition and documentation ranges.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xe0) == 0x20 &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] < 0x02) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x02);
    }
}
