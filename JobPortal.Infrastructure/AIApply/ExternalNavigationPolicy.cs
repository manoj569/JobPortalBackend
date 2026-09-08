using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using JobPortal.Application.Features.AIApply;

namespace JobPortal.Infrastructure.AIApply;

public interface IExternalHostAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}

public sealed class SystemExternalHostAddressResolver : IExternalHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);
}

public sealed class ExternalNavigationPolicy
{
    private readonly IOptions<AIApplyOptions> options;
    private readonly IExternalHostAddressResolver addresses;

    public ExternalNavigationPolicy(IOptions<AIApplyOptions> options) : this(options, new SystemExternalHostAddressResolver()) { }
    public ExternalNavigationPolicy(IOptions<AIApplyOptions> options, IExternalHostAddressResolver addresses) { this.options = options; this.addresses = addresses; }

    public async Task<bool> IsAllowedAsync(Uri uri, CancellationToken ct)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port is <= 0 or > 65535)
            return false;
        // Require a DNS hostname so every navigation is subject to address resolution and
        // private-address rejection. Direct IP destinations are never needed for ATS flows.
        if (IPAddress.TryParse(uri.DnsSafeHost, out _)) return false;
        IPAddress[] addresses;
        try { addresses = await this.addresses.ResolveAsync(uri.DnsSafeHost, ct); }
        catch (SocketException) { return false; }
        return addresses.Length > 0 && addresses.All(IsPublic);
    }

    private bool IsPublic(IPAddress address)
    {
        if (options.Value.Browser.AllowLoopbackForTests && IPAddress.IsLoopback(address)) return true;
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var v6 = address.GetAddressBytes();
            return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal &&
                v6[0] is not 0xfc and not 0xfd and not 0xff;
        }
        var bytes = address.GetAddressBytes();
        return !(bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 ||
            bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168 || bytes[0] >= 224);
    }
}
