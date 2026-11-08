using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Authorization;

/// <summary>
/// Roles whose assignment applies in every tenant, whatever tenant the
/// assignment row is stamped with. Every other assignment applies only in the
/// tenant it names (<see cref="OlusoUserRole.TenantId"/>).
/// <para>
/// Identified by the platform-wide row (<c>TenantId == null</c>) carrying one of
/// these names. <see cref="ReservedRoles"/> stops a tenant from creating its own
/// row under either name, so the name on a platform row is authoritative.
/// </para>
/// </summary>
public static class GlobalRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string SystemAdmin = "SystemAdmin";

    /// <summary>Identity's normalized form (upper invariant) of <see cref="SuperAdmin"/>.</summary>
    public const string SuperAdminNormalized = "SUPERADMIN";

    /// <summary>Identity's normalized form (upper invariant) of <see cref="SystemAdmin"/>.</summary>
    public const string SystemAdminNormalized = "SYSTEMADMIN";

    public static bool IsGlobalName(string? normalizedName) =>
        normalizedName is SuperAdminNormalized or SystemAdminNormalized;

    public static bool IsGlobal(OlusoRole role) =>
        role.TenantId == null
        && IsGlobalName(role.NormalizedName ?? role.Name?.ToUpperInvariant());
}

/// <summary>
/// The tenant a role assignment is made in, or read for, when the caller does
/// not name one: the request's tenant, or the user's home tenant when the
/// request runs outside a real tenant (startup, the platform meta-tenant).
/// </summary>
public static class RoleAssignmentTenant
{
    public static string? Resolve(string? ambientTenantId, string? homeTenantId) =>
        string.IsNullOrEmpty(ambientTenantId) || ambientTenantId == PlatformConstants.TenantId
            ? homeTenantId
            : ambientTenantId;
}
