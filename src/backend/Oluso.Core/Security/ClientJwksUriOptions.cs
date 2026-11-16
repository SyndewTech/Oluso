namespace Oluso.Core.Security;

/// <summary>Options for fetching clients' published keys (a client's jwks_uri).</summary>
public sealed class ClientJwksUriOptions
{
    /// <summary>
    /// Hosts the platform itself configured (an integration's own service, such as a bridge's key set) whose key sets
    /// may be fetched over a private network. Every other jwks_uri is a client's choice and must resolve to public
    /// addresses only. https is required either way.
    /// </summary>
    public HashSet<string> TrustedHosts { get; } = new(StringComparer.OrdinalIgnoreCase);
}
