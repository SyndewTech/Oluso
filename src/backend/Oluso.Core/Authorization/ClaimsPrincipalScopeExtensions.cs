using System.Security.Claims;

namespace Oluso.Core.Authorization;

/// <summary>
/// Reads the OAuth scopes granted to a principal. Scopes arrive as one
/// space-separated <c>scope</c> claim, as one <c>scope</c> claim per value, or
/// as an <c>scp</c> claim (Entra ID style); all are accepted, so the policy that
/// admits a caller and the checks inside the endpoint agree on what it holds.
/// </summary>
public static class ClaimsPrincipalScopeExtensions
{
    private static readonly string[] ScopeClaimTypes = { "scope", "scp" };

    public static IReadOnlyList<string> GetGrantedScopes(this ClaimsPrincipal user) =>
        ScopeClaimTypes
            .SelectMany(user.FindAll)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static bool HasGrantedScope(this ClaimsPrincipal user, string scope) =>
        user.GetGrantedScopes().Contains(scope, StringComparer.Ordinal);
}
