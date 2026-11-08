namespace Oluso.Core.Authorization;

/// <summary>
/// Reserved role names that have special meaning in the authorization system
/// and cannot be created, renamed-into, or assigned through normal role
/// endpoints. Lives in <c>Oluso.Core</c> so every module — including
/// non-Admin modules can see and respect this list.
/// Until this moved here, modules that wrote their own RolesController had
/// no way to import the check, which created a privilege-escalation
/// backdoor: any module-admin could create a role named "SuperAdmin" since
/// authorization elsewhere checks role names by string.
/// </summary>
public static class ReservedRoles
{
    /// <summary>
    /// Display forms of every reserved name. Don't compare directly against
    /// this — use <see cref="IsReserved(string?)"/> which canonicalises
    /// the input first so variants like <c>super-admin</c>,
    /// <c>super admin</c>, <c>super_admin</c>, <c>Super.Admin</c> all
    /// resolve to the same blocked entry. Exposed publicly only as a
    /// reference for UI hints / error messages.
    /// </summary>
    // Mirrors the existing Oluso.Admin.Authorization.ReservedClaimTypes.ReservedRoles
    // list verbatim — same set, now visible to every module via Oluso.Core.
    // Keep the two in sync; the Admin copy will eventually delegate here.
    public static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuperAdmin",
        "SystemAdmin",
        "GlobalAdmin",
        "PlatformAdmin",
        "System",
        "OrgAdmin",
        "TenantAdmin",
        "Admin",
    };

    /// <summary>
    /// Platform-level roles that can ONLY be assigned by SuperAdmin.
    /// Grant cross-tenant or platform-wide access.
    /// </summary>
    public static readonly HashSet<string> SuperAdminOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuperAdmin",
        "SystemAdmin",
        "GlobalAdmin",
        "PlatformAdmin",
        "System",
    };

    /// <summary>
    /// Pre-computed canonical form of every reserved name so checks are
    /// O(1) hash-lookups rather than per-call string-building.
    /// </summary>
    private static readonly HashSet<string> _canonicalNames =
        Names.Select(Canonicalize).ToHashSet();

    private static readonly HashSet<string> _canonicalSuperAdminOnly =
        SuperAdminOnly.Select(Canonicalize).ToHashSet();

    /// <summary>
    /// Returns true when <paramref name="roleName"/> matches a reserved
    /// name under canonical comparison: lowercase + non-alphanumerics
    /// stripped. <c>SuperAdmin</c>, <c>super admin</c>, <c>super-admin</c>,
    /// <c>SUPER_ADMIN</c>, <c>Super.Admin</c>, <c>S/u/p/e/r/A/d/m/i/n</c>
    /// all collapse to <c>superadmin</c>. Empty / null returns false.
    /// </summary>
    public static bool IsReserved(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return false;
        return _canonicalNames.Contains(Canonicalize(roleName));
    }

    /// <summary>
    /// Returns true when the role can only be granted by a SuperAdmin.
    /// Same canonicalisation as <see cref="IsReserved"/>.
    /// </summary>
    public static bool IsSuperAdminOnly(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return false;
        return _canonicalSuperAdminOnly.Contains(Canonicalize(roleName));
    }

    /// <summary>
    /// Lowercase + strip every non-alphanumeric character. Collapses
    /// whitespace, hyphens, underscores, dots, slashes, and miscellaneous
    /// punctuation an admin might use to separate words. Keeps Unicode
    /// letters/digits as-is — combining-character lookalikes (e.g.
    /// "Súper Admin") aren't treated specially; that's an acceptable gap
    /// for the trusted-author threat model.
    /// </summary>
    internal static string Canonicalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

/// <summary>
/// Thrown by the <c>ReservedRoleSaveChangesInterceptor</c> when an entity
/// modification would create or update a role with a reserved name. The
/// interceptor is the last line of defence regardless of which controller
/// or module attempted the write — a mistake the upstream layer missed
/// surfaces here as a clean exception, not a silent privilege escalation.
/// </summary>
public class ReservedRoleException : InvalidOperationException
{
    public string RoleName { get; }

    public ReservedRoleException(string roleName)
        : base($"Cannot persist role '{roleName}': name is reserved by the platform and cannot be created or assigned through this path. Use the platform admin role endpoint with appropriate authorization.")
    {
        RoleName = roleName;
    }
}
