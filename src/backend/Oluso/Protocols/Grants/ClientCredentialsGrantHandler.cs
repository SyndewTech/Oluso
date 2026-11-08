using Microsoft.Extensions.Logging;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles client_credentials grant type
/// </summary>
public class ClientCredentialsGrantHandler : IGrantHandler
{
    private readonly IResourceStore _resourceStore;
    private readonly IClientStore _clientStore;
    private readonly ILogger<ClientCredentialsGrantHandler> _logger;

    public string GrantType => OidcConstants.GrantTypes.ClientCredentials;

    public ClientCredentialsGrantHandler(
        IResourceStore resourceStore,
        IClientStore clientStore,
        ILogger<ClientCredentialsGrantHandler> logger)
    {
        _resourceStore = resourceStore;
        _clientStore = clientStore;
        _logger = logger;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        // Client credentials grant has no user - just the client
        // Client authentication is already done by TokenRequestValidator

        var scopes = request.RequestedScopes.Any()
            ? request.RequestedScopes.ToList()
            : await ApiScopePrefixes.WithoutPrefixScopesAsync(
                _resourceStore, request.Client?.AllowedScopes ?? new List<string>(), cancellationToken);

        // Filter out identity scopes - client credentials can't request user-related scopes
        // This includes standard OIDC scopes and any custom identity resources
        var identityResources = await _resourceStore.FindIdentityResourcesByScopeNameAsync(scopes, cancellationToken);
        var identityScopeNames = identityResources.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Also exclude offline_access as it doesn't make sense for client credentials
        identityScopeNames.Add(OidcConstants.Scopes.OfflineAccess);

        var validScopes = scopes
            .Where(s => !identityScopeNames.Contains(s))
            .ToList();

        var claims = new Dictionary<string, object>
        {
            [OidcConstants.StandardClaims.ClientId] = request.Client?.ClientId ?? string.Empty
        };

        // Use the client's registered tenant as the token's tenant_id
        if (!string.IsNullOrEmpty(request.Client?.TenantId))
        {
            claims[OidcConstants.StandardClaims.TenantId] = request.Client.TenantId;
        }

        // Emit claims from the client's allowlisted custom request parameters.
        // For client_credentials the values come from the (client-authenticated)
        // token request body rather than an /authorize redirect, but the rules
        // are identical: only allowlisted parameters are read, and a mapping can
        // never target a reserved/system claim.
        if (!string.IsNullOrEmpty(request.Client?.ClientId))
        {
            var fullClient = await _clientStore.FindClientByIdAsync(request.Client.ClientId, cancellationToken);
            if (fullClient?.AllowedAuthorizeParameters is { Count: > 0 } mappings)
            {
                foreach (var mapping in mappings)
                {
                    var hasValue = request.Raw.TryGetValue(mapping.Parameter, out var value) && !string.IsNullOrEmpty(value);

                    if (mapping.Required && !hasValue)
                    {
                        return GrantResult.Failure(
                            "invalid_request",
                            $"Missing required parameter '{mapping.Parameter}'");
                    }

                    if (!hasValue)
                    {
                        continue;
                    }

                    if (ReservedClaimKeys.IsReserved(mapping.ClaimType) || ReservedClaimKeys.IsPermissionEntry(mapping.ClaimType))
                    {
                        // Unreachable on a well-formed client (blocked on write), but
                        // never mint a system-managed claim from a request parameter.
                        _logger.LogWarning(
                            "Refusing to emit reserved claim '{ClaimType}' from request parameter '{Parameter}' for client {ClientId}",
                            mapping.ClaimType, mapping.Parameter, request.Client.ClientId);
                        continue;
                    }

                    claims[mapping.ClaimType] = value!;
                }
            }
        }

        return new GrantResult
        {
            SubjectId = null, // No subject for client credentials
            Scopes = validScopes,
            Claims = claims
        };
    }
}
