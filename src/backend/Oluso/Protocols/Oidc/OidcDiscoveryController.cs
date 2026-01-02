using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OpenID Connect Discovery Endpoint.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcDiscoveryController : ControllerBase
{
    private readonly IGrantHandlerRegistry _grantRegistry;
    private readonly IResourceStore _resourceStore;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly OidcEndpointConfiguration _endpointConfig;

    public OidcDiscoveryController(
        IGrantHandlerRegistry grantRegistry,
        IResourceStore resourceStore,
        IIssuerResolver issuerResolver,
        ITenantSettingsProvider tenantSettings,
        IOptions<OidcEndpointConfiguration> endpointConfig)
    {
        _grantRegistry = grantRegistry;
        _resourceStore = resourceStore;
        _issuerResolver = issuerResolver;
        _tenantSettings = tenantSettings;
        _endpointConfig = endpointConfig.Value;
    }

    [HttpGet]
    public async Task<IActionResult> GetConfiguration(CancellationToken cancellationToken)
    {
        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
        var baseUrl = issuer.TrimEnd('/'); // Use issuer as base URL for consistency
        var scopes = await GetScopesAsync(cancellationToken);
        var claims = await GetSupportedClaimsAsync(cancellationToken);
        var protocolSettings = await _tenantSettings.GetProtocolSettingsAsync(cancellationToken);

        // Get grant types - use tenant config or all registered
        var grantTypes = protocolSettings.AllowedGrantTypes?.Count > 0
            ? protocolSettings.AllowedGrantTypes.Intersect(_grantRegistry.SupportedGrantTypes).ToArray()
            : _grantRegistry.SupportedGrantTypes.ToArray();

        // Get response types - use tenant config or default
        var responseTypes = protocolSettings.AllowedResponseTypes?.Count > 0
            ? protocolSettings.AllowedResponseTypes.ToArray()
            : GetDefaultResponseTypes();

        // Get token endpoint auth methods - use tenant config or default
        var tokenEndpointAuthMethods = protocolSettings.AllowedTokenEndpointAuthMethods?.Count > 0
            ? protocolSettings.AllowedTokenEndpointAuthMethods.ToArray()
            : new[] { "client_secret_basic", "client_secret_post", "client_secret_jwt", "private_key_jwt", "none" };

        // Get subject types - use tenant config or default
        var subjectTypes = protocolSettings.SubjectTypesSupported?.Count > 0
            ? protocolSettings.SubjectTypesSupported.ToArray()
            : new[] { "public", "pairwise" };

        // Get ID token signing algorithms - use tenant config or default
        var idTokenSigningAlgs = protocolSettings.IdTokenSigningAlgValuesSupported?.Count > 0
            ? protocolSettings.IdTokenSigningAlgValuesSupported.ToArray()
            : new[] { "RS256", "ES256" };

        // Get code challenge methods - use tenant config or default
        var codeChallengeMethodsDefault = protocolSettings.AllowPlainPkce
            ? new[] { "S256", "plain" }
            : new[] { "S256" };
        var codeChallengeMethods = protocolSettings.CodeChallengeMethodsSupported?.Count > 0
            ? protocolSettings.CodeChallengeMethodsSupported.ToArray()
            : codeChallengeMethodsDefault;

        // Get DPoP signing algorithms - use tenant config or default
        var dpopSigningAlgs = protocolSettings.DPoPSigningAlgValuesSupported?.Count > 0
            ? protocolSettings.DPoPSigningAlgValuesSupported.ToArray()
            : new[] { "RS256", "ES256" };

        var discovery = new Dictionary<string, object>
        {
            ["issuer"] = issuer,
            ["authorization_endpoint"] = $"{baseUrl}{_endpointConfig.AuthorizeEndpoint}",
            ["token_endpoint"] = $"{baseUrl}{_endpointConfig.TokenEndpoint}",
            ["userinfo_endpoint"] = $"{baseUrl}{_endpointConfig.UserInfoEndpoint}",
            ["jwks_uri"] = $"{baseUrl}{_endpointConfig.JwksEndpoint}",
            ["revocation_endpoint"] = $"{baseUrl}{_endpointConfig.RevocationEndpoint}",
            ["introspection_endpoint"] = $"{baseUrl}{_endpointConfig.IntrospectionEndpoint}",
            ["end_session_endpoint"] = $"{baseUrl}{_endpointConfig.EndSessionEndpoint}",
            ["device_authorization_endpoint"] = $"{baseUrl}{_endpointConfig.DeviceAuthorizationEndpoint}",
            ["pushed_authorization_request_endpoint"] = $"{baseUrl}{_endpointConfig.PushedAuthorizationEndpoint}",
            ["backchannel_authentication_endpoint"] = $"{baseUrl}{_endpointConfig.BackchannelAuthenticationEndpoint}",

            ["scopes_supported"] = scopes,
            ["claims_supported"] = claims,
            ["grant_types_supported"] = grantTypes,
            ["response_types_supported"] = responseTypes,
            ["response_modes_supported"] = new[] { "query", "fragment", "form_post" },
            ["token_endpoint_auth_methods_supported"] = tokenEndpointAuthMethods,
            ["subject_types_supported"] = subjectTypes,
            ["id_token_signing_alg_values_supported"] = idTokenSigningAlgs,
            ["code_challenge_methods_supported"] = codeChallengeMethods,

            // Additional capabilities from tenant protocol settings
            ["request_parameter_supported"] = protocolSettings.RequestParameterSupported,
            ["request_uri_parameter_supported"] = protocolSettings.RequestUriParameterSupported,
            ["require_request_uri_registration"] = false,
            ["claims_parameter_supported"] = protocolSettings.ClaimsParameterSupported,
            ["frontchannel_logout_supported"] = protocolSettings.FrontchannelLogoutSupported,
            ["frontchannel_logout_session_supported"] = protocolSettings.FrontchannelLogoutSupported,
            ["backchannel_logout_supported"] = protocolSettings.BackchannelLogoutSupported,
            ["backchannel_logout_session_supported"] = protocolSettings.BackchannelLogoutSupported,

            // DPoP
            ["dpop_signing_alg_values_supported"] = dpopSigningAlgs,

            // PAR
            ["require_pushed_authorization_requests"] = protocolSettings.RequirePushedAuthorizationRequests,

            // CIBA (Client Initiated Backchannel Authentication)
            ["backchannel_token_delivery_modes_supported"] = new[] { "poll", "ping", "push" },
            ["backchannel_authentication_request_signing_alg_values_supported"] = idTokenSigningAlgs,
            ["backchannel_user_code_parameter_supported"] = true
        };

        return Ok(discovery);
    }

    private async Task<string[]> GetScopesAsync(CancellationToken cancellationToken)
    {
        var scopes = new List<string>
        {
            OidcConstants.Scopes.OpenId,
            OidcConstants.Scopes.Profile,
            OidcConstants.Scopes.Email,
            OidcConstants.Scopes.Address,
            OidcConstants.Scopes.Phone,
            OidcConstants.Scopes.OfflineAccess
        };

        // Add API scopes
        var apiScopes = await _resourceStore.GetAllApiScopesAsync(cancellationToken);
        scopes.AddRange(apiScopes.Where(s => s.ShowInDiscoveryDocument).Select(s => s.Name));

        // Add identity resources
        var identityResources = await _resourceStore.GetAllIdentityResourcesAsync(cancellationToken);
        scopes.AddRange(identityResources.Where(r => r.ShowInDiscoveryDocument).Select(r => r.Name));

        return scopes.Distinct().ToArray();
    }

    /// <summary>
    /// Gets claims supported by the tenant based on enabled identity resources.
    /// Always includes standard OIDC claims (sub, iss, etc.) plus claims from
    /// identity resources that are visible in the discovery document.
    /// </summary>
    private async Task<string[]> GetSupportedClaimsAsync(CancellationToken cancellationToken)
    {
        // Start with mandatory OIDC claims that are always present
        var claims = new HashSet<string>
        {
            OidcConstants.StandardClaims.Subject,
            OidcConstants.StandardClaims.Issuer,
            OidcConstants.StandardClaims.Audience,
            OidcConstants.StandardClaims.IssuedAt,
            OidcConstants.StandardClaims.Expiration,
            OidcConstants.StandardClaims.Nonce,
            OidcConstants.StandardClaims.AuthTime,
            OidcConstants.StandardClaims.Acr,
            OidcConstants.StandardClaims.Amr,
            OidcConstants.StandardClaims.Azp,
            OidcConstants.StandardClaims.SessionId
        };

        // Add claims from enabled identity resources for this tenant
        var identityResources = await _resourceStore.GetAllIdentityResourcesAsync(cancellationToken);
        foreach (var resource in identityResources.Where(r => r.Enabled && r.ShowInDiscoveryDocument))
        {
            foreach (var claim in resource.UserClaims)
            {
                claims.Add(claim.Type);
            }
        }

        return claims.Order().ToArray();
    }

    private static string[] GetDefaultResponseTypes()
    {
        return new[]
        {
            ResponseTypes.Code,
            ResponseTypes.Token,
            ResponseTypes.IdToken,
            ResponseTypes.CodeIdToken,
            ResponseTypes.CodeToken,
            ResponseTypes.IdTokenToken,
            ResponseTypes.CodeIdTokenToken
        };
    }
}
