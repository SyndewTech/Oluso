namespace Oluso.Protocols.Validation;

/// <summary>
/// The client metadata rules a Matrix client's dynamic registration must follow (Matrix spec v1.18, OAuth 2.0 API,
/// "Client registration"; MSC2966):
/// <list type="bullet">
/// <item><c>client_uri</c> is required and https;</item>
/// <item><c>logo_uri</c>, <c>policy_uri</c> and <c>tos_uri</c>, when given, are https on the <c>client_uri</c>
/// host or a subdomain of it;</item>
/// <item><c>application_type</c> is <c>web</c> (the default) or <c>native</c>;</item>
/// <item>web redirect URIs are https on the <c>client_uri</c> host or a subdomain;</item>
/// <item>native redirect URIs are a private-use scheme starting with the reversed <c>client_uri</c> host
/// (<c>app.example:/oauth-callback</c> for <c>https://example.app</c>) with no authority, a loopback
/// <c>http://localhost|127.0.0.1|[::1]</c> without a port (any port is accepted at /authorize), or https as for
/// web.</item>
/// </list>
/// No URI may carry user information.
/// </summary>
public static class MatrixClientMetadata
{
    public const string Web = "web";
    public const string Native = "native";

    private static readonly string[] LoopbackHosts = { "localhost", "127.0.0.1", "[::1]" };

    /// <summary>Whether <paramref name="applicationType"/> is a known application type (web or native).</summary>
    public static bool IsKnownApplicationType(string? applicationType) => applicationType is Web or Native;

    /// <summary>
    /// The first rule the metadata breaks, as an RFC 7591 error (<c>invalid_client_metadata</c> or
    /// <c>invalid_redirect_uri</c>) and description, or null when it follows them all.
    /// </summary>
    public static (string Error, string Description)? Validate(
        string? applicationType,
        string? clientUri,
        string? logoUri,
        string? policyUri,
        string? tosUri,
        IEnumerable<string> redirectUris)
    {
        var type = string.IsNullOrEmpty(applicationType) ? Web : applicationType;
        if (!IsKnownApplicationType(type))
        {
            return ("invalid_client_metadata", "application_type must be web or native");
        }

        if (string.IsNullOrEmpty(clientUri) || !TryHttps(clientUri, out var client))
        {
            return ("invalid_client_metadata", "client_uri is required and must be an https URL");
        }
        var host = client.IdnHost.ToLowerInvariant();

        foreach (var (name, value) in new[] { ("logo_uri", logoUri), ("policy_uri", policyUri), ("tos_uri", tosUri) })
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (!TryHttps(value, out var uri) || !OnHost(uri, host))
            {
                return ("invalid_client_metadata", $"{name} must be an https URL on the client_uri host or a subdomain of it");
            }
        }

        foreach (var redirect in redirectUris)
        {
            if (!IsAllowedRedirect(type, redirect, host))
            {
                return ("invalid_redirect_uri", type == Native
                    ? $"Invalid redirect URI for a native client: {redirect}. Use a private-use scheme starting with the reversed client_uri host and no authority, a loopback http URI without a port, or https on the client_uri host"
                    : $"Invalid redirect URI for a web client: {redirect}. Use https on the client_uri host or a subdomain of it");
            }
        }

        return null;
    }

    /// <summary>The reversed host, as a native client's private-use scheme starts (example.app → app.example).</summary>
    public static string ReversedHost(string host) => string.Join('.', host.Split('.').Reverse());

    private static bool IsAllowedRedirect(string applicationType, string redirect, string host)
    {
        if (!Uri.TryCreate(redirect, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return OnHost(uri, host);
        }

        if (applicationType != Native)
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            // Loopback, registered without a port (the port is chosen at sign-in).
            return LoopbackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) && !HasExplicitPort(redirect, uri);
        }

        // Private-use scheme: starts with the reversed client_uri host; no authority (a single slash or none).
        var reversed = ReversedHost(host);
        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme != reversed && !scheme.StartsWith(reversed + ".", StringComparison.Ordinal))
        {
            return false;
        }
        var rest = redirect[(redirect.IndexOf(':') + 1)..];
        return !rest.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool HasExplicitPort(string raw, Uri uri)
    {
        var authorityStart = raw.IndexOf("//", StringComparison.Ordinal) + 2;
        var authorityEnd = raw.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
        var authority = authorityEnd < 0 ? raw[authorityStart..] : raw[authorityStart..authorityEnd];
        // An IPv6 host is bracketed; a port follows the closing bracket.
        var hostEnd = authority.StartsWith('[') ? authority.IndexOf(']') + 1 : 0;
        return authority.IndexOf(':', hostEnd) >= 0;
    }

    private static bool TryHttps(string value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    private static bool OnHost(Uri uri, string host)
    {
        var candidate = uri.IdnHost.ToLowerInvariant();
        return candidate == host || candidate.EndsWith("." + host, StringComparison.Ordinal);
    }
}
