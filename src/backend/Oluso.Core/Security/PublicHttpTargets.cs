using System.Net;
using System.Net.Sockets;
using Oluso.Core.UserJourneys;

namespace Oluso.Core.Security;

/// <summary>
/// Server-side request forgery defences for outbound requests to URLs someone else chose (an administrator's icon
/// URL, a client's request_uri). The server is making the request, so the target is hostile until proven otherwise:
/// <list type="bullet">
/// <item>https only, no credentials in the URL, hostnames must be public DNS names or public IP literals;</item>
/// <item>every hostname is resolved first and every resolved address must be public, so an attacker-controlled
/// name cannot point at loopback, link-local metadata endpoints or private ranges;</item>
/// <item>the connection is then pinned to the address that was checked, so a second lookup during connect (DNS
/// rebinding) cannot redirect it; TLS is still validated against the hostname.</item>
/// </list>
/// Use <see cref="CreateHandler"/> for the named HttpClient and <see cref="Pin"/> on every request sent through it;
/// redirects are not followed automatically, so the caller re-validates any hop it chooses to follow.
/// </summary>
public static class PublicHttpTargets
{
    private static readonly HttpRequestOptionsKey<IPAddress> PinnedAddress = new("Oluso.PublicHttpTargets.PinnedAddress");
    private static readonly HttpRequestOptionsKey<bool> PrivateNetworkAllowed = new("Oluso.PublicHttpTargets.PrivateNetworkAllowed");

    /// <summary>
    /// Handler configuration for a named client: no automatic redirects, no cookies, no proxy, and connections go
    /// to the address pinned on the request.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = PinnedConnectAsync,
    };

    /// <summary>
    /// Returns a rejection reason, or null when the URL may be fetched. <paramref name="allowPrivateNetwork"/>
    /// admits private, loopback and link-local targets; only for development and test setups.
    /// </summary>
    public static string? ValidateTarget(Uri url, bool allowPrivateNetwork = false)
    {
        if (!url.IsAbsoluteUri)
            return "URL must be absolute";
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return "only https URLs are fetched";
        if (!string.IsNullOrEmpty(url.UserInfo))
            return "URLs with embedded credentials are not fetched";
        if (url.Port != 443 && url.Port < 1024)
            return $"port {url.Port} is not allowed";

        var host = url.IdnHost;
        if (string.IsNullOrEmpty(host))
            return "URL has no host";
        if (allowPrivateNetwork)
            return null;
        if (PluginNetworkPolicyValidator.IsForbiddenHostname(host))
            return $"'{host}' is a reserved internal hostname";

        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            if (PluginNetworkPolicyValidator.IsBlockedAddress(literal))
                return $"'{host}' is a private, loopback or link-local address";
            return null;
        }

        if (!host.Contains('.'))
            return $"'{host}' is not a public DNS name";

        return null;
    }

    /// <summary>
    /// Resolves the URL's host and returns the address to connect to, or why it may not be used: every address
    /// the name resolves to must be public (unless <paramref name="allowPrivateNetwork"/>).
    /// </summary>
    public static async Task<(IPAddress? Address, string? Error)> ResolvePublicAddressAsync(
        Uri url,
        CancellationToken cancellationToken,
        bool allowPrivateNetwork = false)
    {
        var host = url.IdnHost;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
            return !allowPrivateNetwork && PluginNetworkPolicyValidator.IsBlockedAddress(literal)
                ? (null, "address is not public")
                : (literal, null);

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException ex)
        {
            return (null, $"DNS lookup failed ({ex.SocketErrorCode})");
        }

        if (addresses.Length == 0)
            return (null, "DNS lookup returned no addresses");

        // Every address must be public: a name that resolves to one public
        // and one private address is still an attempt to reach the private one.
        if (!allowPrivateNetwork)
        {
            var blocked = addresses.FirstOrDefault(PluginNetworkPolicyValidator.IsBlockedAddress);
            if (blocked != null)
                return (null, $"resolves to a private, loopback or link-local address ({blocked})");
        }

        var preferred = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
        return (preferred, null);
    }

    /// <summary>Pins the request to the validated address; a request without one is refused at connect.</summary>
    public static void Pin(HttpRequestMessage request, IPAddress address, bool allowPrivateNetwork = false)
    {
        request.Options.Set(PinnedAddress, address);
        request.Options.Set(PrivateNetworkAllowed, allowPrivateNetwork);
    }

    /// <summary>Connects to the address validated for this request rather than re-resolving the host.</summary>
    private static async ValueTask<Stream> PinnedConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        if (context.InitialRequestMessage?.Options.TryGetValue(PinnedAddress, out var address) != true || address == null)
            throw new HttpRequestException("Refusing to connect: no validated address pinned to the request");

        var privateAllowed = context.InitialRequestMessage.Options.TryGetValue(PrivateNetworkAllowed, out var allowed) && allowed;
        if (!privateAllowed && PluginNetworkPolicyValidator.IsBlockedAddress(address))
            throw new HttpRequestException("Refusing to connect to a non-public address");

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
