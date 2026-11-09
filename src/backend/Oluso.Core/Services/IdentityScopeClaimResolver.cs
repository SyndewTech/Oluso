using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Services;

/// <summary>
/// Resolves which claim types the <em>identity</em> scopes in a granted set
/// release — the claims that belong in an ID token and at the userinfo endpoint.
///
/// Deliberately excludes ApiScope and Resource UserClaims: those are
/// authorization attributes for a resource server and belong in the access
/// token (RFC 9068 §2.2.3.1). Keeping this in one place stops the ID token and
/// the profile service drifting apart on what "an identity claim" means.
/// </summary>
public interface IIdentityScopeClaimResolver
{
    Task<HashSet<string>> ResolveAsync(
        IEnumerable<string> scopes,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class IdentityScopeClaimResolver : IIdentityScopeClaimResolver
{
    /// <summary>
    /// Well-known OIDC scope → claim-type defaults, used when a scope has no
    /// configured IdentityResource row. Mirrors the standard scopes advertised
    /// in discovery. A DB row for the same scope name REPLACES the entry here
    /// (override semantics), so an admin can fully redefine what e.g. "profile"
    /// releases.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> StandardScopeClaimTypes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [OidcConstants.Scopes.Profile] = new[]
            {
                OidcConstants.StandardClaims.Name,
                OidcConstants.StandardClaims.GivenName,
                OidcConstants.StandardClaims.FamilyName,
                OidcConstants.StandardClaims.PreferredUsername,
                OidcConstants.StandardClaims.Picture,
            },
            [OidcConstants.Scopes.Email] = new[]
            {
                OidcConstants.StandardClaims.Email,
                OidcConstants.StandardClaims.EmailVerified,
            },
            [OidcConstants.Scopes.Phone] = new[]
            {
                OidcConstants.StandardClaims.PhoneNumber,
                OidcConstants.StandardClaims.PhoneNumberVerified,
            },
        };

    private readonly IResourceStore _resourceStore;

    public IdentityScopeClaimResolver(IResourceStore resourceStore)
    {
        _resourceStore = resourceStore;
    }

    public async Task<HashSet<string>> ResolveAsync(
        IEnumerable<string> scopes,
        CancellationToken cancellationToken = default)
    {
        var scopeList = scopes.ToList();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (scopeList.Count == 0)
        {
            return allowed;
        }

        var identityResources = (await _resourceStore.FindIdentityResourcesByScopeNameAsync(scopeList, cancellationToken))
            .ToDictionary(r => r.Name, r => r.UserClaims.Select(c => c.Type).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var scope in scopeList)
        {
            if (identityResources.TryGetValue(scope, out var configured))
            {
                allowed.UnionWith(configured);
            }
            else if (StandardScopeClaimTypes.TryGetValue(scope, out var standard))
            {
                allowed.UnionWith(standard);
            }
        }

        return allowed;
    }
}
