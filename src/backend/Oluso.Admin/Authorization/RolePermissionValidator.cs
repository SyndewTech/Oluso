using Oluso.Core.Authorization;

namespace Oluso.Admin.Authorization;

/// <summary>
/// The standing of whoever is writing a role's permissions. Decides which core
/// permissions they may put on a role.
/// </summary>
/// <param name="IsSuperAdmin">Platform SuperAdmin: any core permission.</param>
/// <param name="IsOrgAdmin">Owner or Admin of the tenant's organization: may assign Organizations/Tenants permissions.</param>
/// <param name="IsServiceClient">A machine client: may not assign permissions that are granted by name only.</param>
public readonly record struct RolePermissionGrantor(bool IsSuperAdmin, bool IsOrgAdmin, bool IsServiceClient = false)
{
    /// <summary>
    /// A machine client on the Service API. It acts for one tenant and holds
    /// neither platform nor organization standing.
    /// </summary>
    public static RolePermissionGrantor ServiceClient => new(IsSuperAdmin: false, IsOrgAdmin: false, IsServiceClient: true);
}

/// <summary>
/// The one check for which permissions may be written to a tenant role, shared
/// by the admin Roles API and the Service API so the two can't drift.
/// </summary>
public static class RolePermissionValidator
{
    private static readonly HashSet<string> OrganizationCategories = new(StringComparer.Ordinal)
    {
        AdminPermissions.Categories.Organizations,
        AdminPermissions.Categories.Tenants
    };

    /// <summary>
    /// Returns an error message when any permission can't be assigned by
    /// <paramref name="grantor"/>, or null when all can.
    /// <list type="number">
    /// <item>Only core permissions (no FeatureKey) are accepted. Module permissions go through the module's own roles endpoint.</item>
    /// <item><c>RequiresSuperAdmin</c> permissions need a SuperAdmin.</item>
    /// <item>Organizations/Tenants permissions need an organization admin.</item>
    /// <item><c>RequiresExplicitGrant</c> permissions are assigned by a person, never a service client.</item>
    /// </list>
    /// </summary>
    /// <param name="definitions">Every known permission; <see cref="PermissionRegistry.Instance"/> when null.</param>
    public static string? Validate(
        IEnumerable<string>? permissions,
        RolePermissionGrantor grantor,
        IReadOnlyList<PermissionDefinition>? definitions = null)
    {
        var requested = permissions?.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requested == null || requested.Count == 0)
        {
            return null;
        }

        var allDefinitions = definitions ?? PermissionRegistry.Instance.GetPluginPermissions();

        PermissionDefinition? Find(string name) => allDefinitions.FirstOrDefault(d =>
            d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        var invalid = requested
            .Where(p => Find(p) is not { FeatureKey: null })
            .ToList();

        if (invalid.Count > 0)
        {
            var modulePermissions = invalid.Where(p => Find(p) != null).ToList();
            if (modulePermissions.Count > 0)
            {
                return $"The following are module permissions that must be assigned via the module's dedicated roles page: {string.Join(", ", modulePermissions)}.";
            }

            return $"The following permissions are not valid: {string.Join(", ", invalid)}. Use GET /api/admin/permissions to see available permissions.";
        }

        if (grantor.IsServiceClient)
        {
            var explicitOnly = requested.Where(p => Find(p)!.RequiresExplicitGrant).ToList();
            if (explicitOnly.Count > 0)
            {
                return $"The following permissions can only be assigned by an administrator: {string.Join(", ", explicitOnly)}.";
            }
        }

        if (grantor.IsSuperAdmin)
        {
            return null;
        }

        var superAdminOnly = requested.Where(p => Find(p)!.RequiresSuperAdmin).ToList();
        if (superAdminOnly.Count > 0)
        {
            return $"The following permissions require SuperAdmin to assign: {string.Join(", ", superAdminOnly)}.";
        }

        if (!grantor.IsOrgAdmin)
        {
            var organizationLevel = requested
                .Where(p => OrganizationCategories.Contains(Find(p)!.Category))
                .ToList();
            if (organizationLevel.Count > 0)
            {
                return $"The following permissions require Organization Admin to assign: {string.Join(", ", organizationLevel)}.";
            }
        }

        return null;
    }

    /// <summary>
    /// Returns an error message when a role's free-form claims include a type
    /// that must not be written directly: reserved/system claim types,
    /// <c>permission</c> (use the permissions field), or <c>entitlements</c>
    /// (use the entitlements field, which checks the tenant's catalog).
    /// </summary>
    public static string? ValidateRoleClaims(IEnumerable<string>? claimTypes)
    {
        var types = claimTypes?.ToList();
        if (types == null || types.Count == 0)
        {
            return null;
        }

        var protectedTypes = ReservedClaimTypes.GetProtectedClaimTypes(types.Select(t => (t, string.Empty))).ToList();
        if (protectedTypes.Count > 0)
        {
            return $"The following claim types are protected: {string.Join(", ", protectedTypes)}. Use the 'permissions' field for permission claims.";
        }

        return RawEntitlementClaimError(types);
    }

    /// <summary>
    /// Error when any claim type is the entitlements claim, which may only be
    /// written through the entitlements field.
    /// </summary>
    public static string? RawEntitlementClaimError(IEnumerable<string> claimTypes) =>
        claimTypes.Any(t => string.Equals(t, Core.Domain.Entities.OlusoRole.EntitlementClaimType, StringComparison.OrdinalIgnoreCase))
            ? $"'{Core.Domain.Entities.OlusoRole.EntitlementClaimType}' cannot be set as a raw claim. "
                + "Use the 'entitlements' field, which validates against the tenant's catalog."
            : null;
}
