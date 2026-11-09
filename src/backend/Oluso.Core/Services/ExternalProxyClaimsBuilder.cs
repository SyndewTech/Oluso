using System.Security.Claims;

namespace Oluso.Core.Services;

/// <summary>
/// Builds the proxy-mode output claim set used by both the standalone
/// external callback and the journey-driven external login step. Keeps the
/// two flows consistent so a tenant's <c>ProxyIncludeClaims</c> /
/// <c>ProxyExcludeClaims</c> / <c>IncludeExternalAccessToken</c> /
/// <c>IncludeExternalIdToken</c> settings produce the same token shape
/// regardless of which login path the user took.
/// </summary>
public static class ExternalProxyClaimsBuilder
{
    /// <summary>
    /// Build the synthetic subject used in proxy mode. Always
    /// <c>proxy:{provider}:{providerKey}</c> so external subjects can never
    /// collide with local user IDs and downstream code can detect proxy
    /// sessions by prefix.
    /// </summary>
    public static string ProxySubjectFor(string provider, string providerKey)
        => $"proxy:{provider}:{providerKey}";

    /// <summary>
    /// Compose the proxy-mode output claim dictionary, applying admin-supplied
    /// include/exclude filters and claim-type mappings, then short-name
    /// aliasing for the standard ClaimTypes.* WS-Federation URIs.
    /// </summary>
    public static Dictionary<string, object> Build(
        string provider,
        string providerKey,
        IDictionary<string, string>? externalClaims,
        ExternalProviderConfig? config,
        IDictionary<string, string>? claimMappings = null,
        string? email = null,
        string? name = null,
        string? firstName = null,
        string? lastName = null,
        string? accessToken = null,
        string? idToken = null)
    {
        var proxySubject = ProxySubjectFor(provider, providerKey);

        var output = new Dictionary<string, object>
        {
            ["sub"] = proxySubject,
            ["idp"] = provider,
            ["external_sub"] = providerKey,
            ["proxy_mode"] = true,
            ["amr"] = new[] { "external", provider.ToLowerInvariant(), "proxy" }
        };

        if (externalClaims != null)
        {
            var include = config?.ProxyIncludeClaims ?? Array.Empty<string>();
            var exclude = config?.ProxyExcludeClaims ?? Array.Empty<string>();
            var mappings = claimMappings ?? new Dictionary<string, string>();

            foreach (var claim in externalClaims)
            {
                if (exclude.Contains(claim.Key)) continue;
                if (include.Count > 0 && !include.Contains(claim.Key)) continue;

                // Admin-supplied mappings win over the built-in ClaimTypes
                // switch — lets a tenant remap, say, ClaimTypes.Email to
                // "preferred_email" without code changes.
                string claimKey;
                if (mappings.TryGetValue(claim.Key, out var mapped) && !string.IsNullOrEmpty(mapped))
                {
                    claimKey = mapped;
                }
                else
                {
                    claimKey = MapStandardClaimType(claim.Key);
                }

                if (!output.ContainsKey(claimKey))
                {
                    output[claimKey] = claim.Value;
                }
            }
        }

        // Backfill standard short-name claims from typed result fields
        // when the include/mapping pass didn't already populate them.
        if (!output.ContainsKey("email") && !string.IsNullOrEmpty(email))
            output["email"] = email;
        if (!output.ContainsKey("name") && !string.IsNullOrEmpty(name))
            output["name"] = name;
        if (!output.ContainsKey("given_name") && !string.IsNullOrEmpty(firstName))
            output["given_name"] = firstName;
        if (!output.ContainsKey("family_name") && !string.IsNullOrEmpty(lastName))
            output["family_name"] = lastName;

        if (config?.IncludeExternalAccessToken == true && !string.IsNullOrEmpty(accessToken))
            output["external_access_token"] = accessToken;
        if (config?.IncludeExternalIdToken == true && !string.IsNullOrEmpty(idToken))
            output["external_id_token"] = idToken;

        return output;
    }

    private static string MapStandardClaimType(string claimType)
        => claimType switch
        {
            // sub_original (not "sub") so the synthetic proxy subject we
            // generated isn't shadowed by the IdP's NameIdentifier.
            ClaimTypes.NameIdentifier => "sub_original",
            ClaimTypes.Email => "email",
            ClaimTypes.Name => "name",
            ClaimTypes.GivenName => "given_name",
            ClaimTypes.Surname => "family_name",
            _ => claimType
        };
}
