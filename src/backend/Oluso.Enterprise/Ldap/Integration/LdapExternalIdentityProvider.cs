using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Enterprise.Ldap.Authentication;
using Oluso.Enterprise.Ldap.Configuration;

namespace Oluso.Enterprise.Ldap.Integration;

/// <summary>
/// LDAP implementation of IDirectIdentityProvider.
/// Authenticates users against LDAP/Active Directory servers using per-tenant configuration.
/// </summary>
public class LdapExternalIdentityProvider : IDirectIdentityProvider
{
    private readonly ILdapAuthenticator _authenticator;
    private readonly ILogger<LdapExternalIdentityProvider> _logger;

    public LdapExternalIdentityProvider(
        ILdapAuthenticator authenticator,
        ILogger<LdapExternalIdentityProvider> logger)
    {
        _authenticator = authenticator;
        _logger = logger;
    }

    public string ProviderType => "ldap";

    public async Task<DirectIdentityResult> AuthenticateAsync(
        string username,
        string password,
        ExternalProviderDefinition providerDefinition,
        CancellationToken cancellationToken = default)
    {
        // Parse LDAP settings from provider configuration
        var settings = LdapProviderSettings.FromProviderDefinition(providerDefinition);

        if (!settings.IsValid(out var validationError))
        {
            _logger.LogError("Invalid LDAP provider configuration for {Scheme}: {Error}",
                providerDefinition.Scheme, validationError);
            return DirectIdentityResult.Failed(validationError ?? "Invalid LDAP configuration");
        }

        // Use the authenticator with per-tenant settings
        var result = await _authenticator.AuthenticateAsync(username, password, settings, cancellationToken);

        if (!result.Success || result.User == null)
        {
            return DirectIdentityResult.Failed(result.ErrorMessage ?? "Authentication failed");
        }

        _logger.LogInformation("LDAP authentication successful for {Username} via {Scheme}",
            username, providerDefinition.Scheme);

        // Build claims including provider info
        var claims = new List<Claim>(result.Claims)
        {
            new("idp", providerDefinition.Scheme)
        };

        var identity = new ClaimsIdentity(claims, ProviderType);
        var principal = new ClaimsPrincipal(identity);

        return DirectIdentityResult.Success(
            result.User.DistinguishedName,
            result.User.Username,
            principal);
    }
}
