using System.Net;
using System.Net.Sockets;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Validates plugin network whitelist entries before they're persisted.
/// Catches dangerous values that would let a tenant admin sidestep the
/// "All" gate by smuggling catch-alls or internal addresses into the
/// Whitelist textarea.
/// </summary>
/// <remarks>
/// This is save-time validation only — it does not prevent SSRF via DNS
/// rebinding (where an attacker-controlled hostname resolves to an
/// internal IP at request time). That requires per-request IP checks in
/// a custom Extism host function and is a separate piece of work.
/// </remarks>
public static class PluginNetworkPolicyValidator
{
    /// <summary>
    /// Hostnames that expose cloud metadata services or internal infra.
    /// Match is case-insensitive, exact (no substring match) to avoid
    /// rejecting legitimate hostnames that happen to contain these.
    /// </summary>
    private static readonly HashSet<string> ForbiddenHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        // AWS IMDS
        "169.254.169.254",
        // GCP metadata
        "metadata.google.internal",
        "metadata.goog",
        // Azure IMDS
        "169.254.169.254",
        // Oracle Cloud
        "169.254.169.254",
        // IPv6 link-local for AWS metadata
        "fd00:ec2::254"
    };

    /// <summary>
    /// Validates a single whitelist entry. Returns null when the entry is
    /// acceptable, otherwise a human-readable reason for refusal.
    /// </summary>
    /// <param name="entry">A host or wildcard pattern from the admin's list</param>
    /// <param name="platformHost">
    /// The platform's own issuer host (from <c>Oluso:IssuerUri</c>). Plugins
    /// must never be whitelisted to call back into the platform's admin
    /// surface — those calls should be 1st-party managed plugins.
    /// </param>
    public static string? Validate(string entry, string? platformHost)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return "Empty host entry";
        }

        var trimmed = entry.Trim().ToLowerInvariant();

        // Catch-all
        if (trimmed == "*" || trimmed == "*.*")
        {
            return $"'{entry}' is a catch-all — use specific hosts or set AccessMode=All (which is itself disabled).";
        }

        // Wildcard handling. Allow patterns like "*.foo.example.com" but
        // refuse "*.com" / "*.io" / "*.tld" — those are too broad.
        if (trimmed.StartsWith("*."))
        {
            var suffix = trimmed[2..];
            if (string.IsNullOrEmpty(suffix))
            {
                return $"'{entry}' is malformed — wildcard without suffix";
            }
            if (!suffix.Contains('.'))
            {
                return $"'{entry}' wildcards a single TLD — use at least two domain segments after the wildcard (e.g. *.example.com).";
            }
            // Validate the static suffix against the forbidden set too —
            // *.localhost or *.169.254.169.254 are clearly attempts to
            // smuggle in internal access.
            return ValidateHostString(suffix, platformHost);
        }

        return ValidateHostString(trimmed, platformHost);
    }

    /// <summary>
    /// Validates the full list. Returns the first refusal reason, or null
    /// when every entry is acceptable.
    /// </summary>
    public static string? ValidateAll(IEnumerable<string> entries, string? platformHost)
    {
        foreach (var entry in entries)
        {
            var refusal = Validate(entry, platformHost);
            if (refusal != null) return refusal;
        }
        return null;
    }

    private static string? ValidateHostString(string host, string? platformHost)
    {
        // Forbidden well-known hostnames
        if (ForbiddenHostnames.Contains(host))
        {
            return $"'{host}' is a reserved internal hostname";
        }

        // Platform's own issuer host — refuse so plugins can't call back
        // into the admin API.
        if (!string.IsNullOrEmpty(platformHost) && string.Equals(host, platformHost, StringComparison.OrdinalIgnoreCase))
        {
            return $"'{host}' is the platform's own host — plugins must not call the admin API. Use a managed plugin if 1st-party access is required.";
        }

        // IP literal? Reject loopback, link-local, private, multicast.
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsBlockedIp(ip))
            {
                return $"'{host}' is in a blocked IP range (loopback, link-local, private, or multicast)";
            }
        }

        return null;
    }

    /// <summary>
    /// True for addresses a server must never be made to connect to on an
    /// administrator's behalf: loopback, link-local (including cloud
    /// metadata endpoints), RFC 1918 and carrier-grade NAT ranges, multicast
    /// and reserved space, and their IPv6 equivalents. IPv4-mapped IPv6
    /// addresses are checked as IPv4.
    /// </summary>
    public static bool IsBlockedAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        return IsBlockedIp(ip);
    }

    /// <summary>True for hostnames that name internal infrastructure regardless of what they resolve to.</summary>
    public static bool IsForbiddenHostname(string host) => ForbiddenHostnames.Contains(host);

    private static bool IsBlockedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            // 0.0.0.0/8 — "this" network
            if (bytes[0] == 0) return true;
            // 10.0.0.0/8 — RFC1918
            if (bytes[0] == 10) return true;
            // 100.64.0.0/10 — Carrier-grade NAT
            if (bytes[0] == 100 && (bytes[1] & 0xC0) == 64) return true;
            // 169.254.0.0/16 — link-local + IMDS
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 172.16.0.0/12 — RFC1918
            if (bytes[0] == 172 && (bytes[1] & 0xF0) == 16) return true;
            // 192.168.0.0/16 — RFC1918
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 224.0.0.0/4 — multicast
            if ((bytes[0] & 0xF0) == 224) return true;
            // 240.0.0.0/4 — reserved
            if ((bytes[0] & 0xF0) == 240) return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal) return true;
            if (ip.IsIPv6SiteLocal) return true;
            if (ip.IsIPv6Multicast) return true;
            // ULA: fc00::/7
            var bytes = ip.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC) return true;
        }

        return false;
    }
}
