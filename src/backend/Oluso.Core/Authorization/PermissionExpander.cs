namespace Oluso.Core.Authorization;

/// <summary>
/// Expands wildcard permission grants (<c>*</c>, <c>category.*</c>) into the
/// concrete permissions they cover. Used by any endpoint that returns a
/// permission list a UI is going to do literal-membership checks against —
/// the frontend hasPermission() helpers are plain `includes(string)` checks,
/// so a user granted <c>approvals.*</c> would not satisfy a literal
/// <c>approvals.queue.read</c> gate without this expansion.
///
/// <para>Wildcards never unlock permissions flagged
/// <see cref="PermissionDefinition.RequiresExplicitGrant"/> — those must be
/// granted by literal name (e.g. sensitive financial or audit access). The top-level
/// <c>*</c> additionally skips <see cref="PermissionDefinition.RequiresSuperAdmin"/>
/// since those are reserved for the SuperAdmin role explicitly.</para>
/// </summary>
public static class PermissionExpander
{
    /// <summary>
    /// Expand a raw permission set against the registered permission catalog.
    /// </summary>
    /// <param name="raw">Permissions as stored on roles — may contain wildcards.</param>
    /// <param name="catalog">All registered permissions (typically
    /// <c>PermissionRegistry.Instance.GetPluginPermissions()</c>). Passed in
    /// so callers can filter feature-gated permissions out before expansion.</param>
    public static List<string> Expand(
        IEnumerable<string> raw,
        IReadOnlyList<PermissionDefinition> catalog)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var perm in raw)
        {
            if (string.IsNullOrWhiteSpace(perm))
                continue;

            if (perm == "*")
            {
                foreach (var p in catalog)
                {
                    if (!p.RequiresExplicitGrant && !p.RequiresSuperAdmin)
                        result.Add(p.Name);
                }
            }
            else if (perm.EndsWith(".*", StringComparison.Ordinal))
            {
                var prefix = perm[..^2];
                var match = $"{prefix}.";
                foreach (var p in catalog)
                {
                    if (p.Name.StartsWith(match, StringComparison.OrdinalIgnoreCase)
                        && !p.RequiresExplicitGrant)
                    {
                        result.Add(p.Name);
                    }
                }
            }
            else
            {
                result.Add(perm);
            }
        }

        return result.ToList();
    }

    /// <summary>
    /// Convenience overload using the global permission registry.
    /// </summary>
    public static List<string> Expand(IEnumerable<string> raw)
        => Expand(raw, PermissionRegistry.Instance.GetPluginPermissions());
}
