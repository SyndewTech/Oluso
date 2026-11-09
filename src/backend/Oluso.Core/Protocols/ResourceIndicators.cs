namespace Oluso.Core.Protocols;

/// <summary>Resource indicator rules (RFC 8707) shared by the authorize and token endpoints.</summary>
public static class ResourceIndicators
{
    /// <summary>
    /// The first requested resource the client may not ask for, or null when all are allowed. A
    /// client with no allowed-resources list may ask for any (registered) resource.
    /// </summary>
    public static string? FirstNotAllowed(IEnumerable<string> allowedResources, IEnumerable<string> requested)
    {
        var allowed = new HashSet<string>(allowedResources, StringComparer.OrdinalIgnoreCase);
        return allowed.Count == 0 ? null : requested.FirstOrDefault(r => !allowed.Contains(r));
    }

    /// <summary>
    /// The resources a token is for: those the token request names, which must all be in the grant
    /// when the grant has any (RFC 8707 §2.2), else the grant's own. Null means the request asked
    /// for a resource outside the grant (<c>invalid_target</c>).
    /// </summary>
    public static IReadOnlyList<string>? Narrow(IReadOnlyCollection<string> granted, IReadOnlyCollection<string> requested)
    {
        if (requested.Count == 0) return granted.ToList();
        if (granted.Count == 0) return requested.ToList();
        var grant = new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase);
        return requested.All(grant.Contains) ? requested.ToList() : null;
    }
}
