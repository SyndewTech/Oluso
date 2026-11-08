using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Whether the email an external identity provider returned can be taken as proven: enough to mark the address
/// verified and to sign the person in to an existing account at that address without a code to the inbox. Every
/// external sign-in asks this, journey and standalone alike.
/// </summary>
/// <remarks>
/// Only two kinds of provider can vouch for an address:
/// <list type="bullet">
/// <item>At a domain the tenant has verified: one that says the address is verified (<c>email_verified</c>), or one
/// the tenant pinned to its own directory (a SAML IdP, Microsoft with a directory tenant id, Google with a hosted
/// domain that is the address's).</item>
/// <item>Anywhere else: a provider that owns the address, saying it is verified (Google for gmail.com or for its
/// Workspace domain <c>hd</c>, Apple for its own domains).</item>
/// </list>
/// Microsoft's Graph <c>mail</c> is whatever the directory's admin typed, so it vouches for nothing on its own.
/// </remarks>
public static class ExternalEmailTrust
{
    private static readonly string[] GoogleDomains = { "gmail.com", "googlemail.com" };
    private static readonly string[] AppleDomains = { "privaterelay.appleid.com", "icloud.com", "me.com", "mac.com" };
    private static readonly string[] MicrosoftSharedTenants = { "common", "organizations", "consumers" };

    /// <param name="provider">The sign-in's provider (its scheme; <c>saml:</c> or <c>Saml.</c> for a SAML IdP).</param>
    /// <param name="claim">Reads a claim the provider returned, by type.</param>
    public static async Task<bool> IsTrustedAsync(
        string provider,
        Func<string, string?> claim,
        string? email,
        string? tenantId,
        IExternalProviderConfigStore? providers,
        ITenantEmailDomainStore emailDomains,
        CancellationToken cancellationToken = default)
    {
        var domain = TenantEmailDomain.CoveringDomains(email).FirstOrDefault();
        if (domain == null)
        {
            return false;
        }

        var isSaml = provider.StartsWith("saml:", StringComparison.OrdinalIgnoreCase)
            || provider.StartsWith("Saml.", StringComparison.OrdinalIgnoreCase);
        var definition = isSaml || providers == null
            ? null
            : await providers.GetBySchemeAsync(provider, cancellationToken: cancellationToken);
        var type = isSaml ? "saml2" : definition?.ProviderType?.ToLowerInvariant();

        var saysVerified = IsTrue(claim("email_verified")) || IsTrue(claim("xms_edov"));
        var hostedDomain = claim("hd");

        var tenantOwnsDomain = !string.IsNullOrEmpty(tenantId)
            && await emailDomains.IsVerifiedForTenantAsync(tenantId, email, cancellationToken);
        if (tenantOwnsDomain)
        {
            if (saysVerified || type is "saml2" or "ldap")
            {
                return true;
            }

            if (type == "microsoft")
            {
                var directory = Property(definition, "tenantId");
                return !string.IsNullOrWhiteSpace(directory)
                    && !MicrosoftSharedTenants.Contains(directory.Trim(), StringComparer.OrdinalIgnoreCase);
            }

            if (type == "google")
            {
                var pinned = Property(definition, "hostedDomain");
                return !string.IsNullOrWhiteSpace(pinned)
                    && string.Equals(pinned.Trim(), hostedDomain, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(hostedDomain, domain, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        if (!saysVerified)
        {
            return false;
        }

        return type switch
        {
            "google" => GoogleDomains.Contains(domain, StringComparer.OrdinalIgnoreCase)
                || string.Equals(hostedDomain, domain, StringComparison.OrdinalIgnoreCase),
            "apple" => AppleDomains.Contains(domain, StringComparer.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsTrue(string? value) =>
        value != null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

    private static string? Property(ExternalProviderDefinition? definition, string key) =>
        definition?.Properties != null && definition.Properties.TryGetValue(key, out var value) ? value : null;
}
