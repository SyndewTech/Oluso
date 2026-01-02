using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Default implementation of scope validator
/// </summary>
public class ScopeValidator : IScopeValidator
{
    private readonly IResourceStore _resourceStore;

    private static readonly HashSet<string> StandardIdentityScopes = new(StringComparer.Ordinal)
    {
        OidcConstants.Scopes.OpenId,
        OidcConstants.Scopes.Profile,
        OidcConstants.Scopes.Email,
        OidcConstants.Scopes.Address,
        OidcConstants.Scopes.Phone,
        OidcConstants.Scopes.OfflineAccess
    };

    public ScopeValidator(IResourceStore resourceStore)
    {
        _resourceStore = resourceStore;
    }

    public async Task<ScopeValidationResult> ValidateAsync(
        IEnumerable<string> requestedScopes,
        IEnumerable<string> clientAllowedScopes,
        CancellationToken cancellationToken = default)
    {
        var result = new ScopeValidationResult();
        var requested = requestedScopes.ToHashSet(StringComparer.Ordinal);
        var allowed = clientAllowedScopes.ToHashSet(StringComparer.Ordinal);

        // Check for unknown scopes
        var unknownScopes = new List<string>();

        // Get identity resources
        var identityResources = await _resourceStore.GetAllIdentityResourcesAsync(cancellationToken);
        var identityResourceNames = identityResources.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);

        // Get API scopes
        var apiScopes = await _resourceStore.GetAllApiScopesAsync(cancellationToken);
        var apiScopeNames = apiScopes.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        // Get API resources for resource indicators
        var apiResources = await _resourceStore.GetAllApiResourcesAsync(cancellationToken);

        foreach (var scope in requested)
        {
            // Check if client is allowed to request this scope
            if (!allowed.Contains(scope))
            {
                return new ScopeValidationResult
                {
                    Error = OidcConstants.Errors.InvalidScope,
                    ErrorDescription = $"Client is not allowed to request scope '{scope}'"
                };
            }

            // Categorize the scope
            if (StandardIdentityScopes.Contains(scope) || identityResourceNames.Contains(scope))
            {
                result.IdentityScopes.Add(scope);
                result.ValidScopes.Add(scope);
            }
            else if (apiScopeNames.Contains(scope))
            {
                result.ApiScopes.Add(scope);
                result.ValidScopes.Add(scope);

                // Find associated API resources
                var associatedResources = apiResources
                    .Where(r => r.Scopes.Any(s => s.Scope == scope))
                    .Select(r => r.Name);

                foreach (var resource in associatedResources)
                {
                    if (!result.ApiResources.Contains(resource))
                    {
                        result.ApiResources.Add(resource);
                    }
                }
            }
            else
            {
                unknownScopes.Add(scope);
            }
        }

        if (unknownScopes.Any())
        {
            return new ScopeValidationResult
            {
                Error = OidcConstants.Errors.InvalidScope,
                ErrorDescription = $"Unknown scope(s): {string.Join(", ", unknownScopes)}"
            };
        }

        return result;
    }

    public IEnumerable<string> ParseScopes(string? scopeString)
    {
        if (string.IsNullOrWhiteSpace(scopeString))
        {
            return Enumerable.Empty<string>();
        }

        return scopeString
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal);
    }
}
