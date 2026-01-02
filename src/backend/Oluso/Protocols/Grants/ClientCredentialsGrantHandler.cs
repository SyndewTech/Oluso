using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles client_credentials grant type
/// </summary>
public class ClientCredentialsGrantHandler : IGrantHandler
{
    private readonly IResourceStore _resourceStore;

    public string GrantType => OidcConstants.GrantTypes.ClientCredentials;

    public ClientCredentialsGrantHandler(IResourceStore resourceStore)
    {
        _resourceStore = resourceStore;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        // Client credentials grant has no user - just the client
        // Client authentication is already done by TokenRequestValidator

        var scopes = request.RequestedScopes.Any()
            ? request.RequestedScopes.ToList()
            : request.Client?.AllowedScopes.ToList() ?? new List<string>();

        // Filter out identity scopes - client credentials can't request user-related scopes
        // This includes standard OIDC scopes and any custom identity resources
        var identityResources = await _resourceStore.FindIdentityResourcesByScopeNameAsync(scopes, cancellationToken);
        var identityScopeNames = identityResources.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Also exclude offline_access as it doesn't make sense for client credentials
        identityScopeNames.Add(OidcConstants.Scopes.OfflineAccess);

        var validScopes = scopes
            .Where(s => !identityScopeNames.Contains(s))
            .ToList();

        return new GrantResult
        {
            SubjectId = null, // No subject for client credentials
            Scopes = validScopes,
            Claims = new Dictionary<string, object>
            {
                [OidcConstants.StandardClaims.ClientId] = request.Client?.ClientId ?? string.Empty
            }
        };
    }
}
