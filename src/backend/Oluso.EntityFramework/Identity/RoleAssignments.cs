using Microsoft.EntityFrameworkCore;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Identity;

/// <summary>
/// The one place that decides which role assignments apply in a tenant, and how
/// an assignment is written and removed. Every reader and writer of
/// <see cref="OlusoUserRole"/> goes through here.
/// <para>
/// An assignment applies in the tenant it is stamped with. The exception is
/// <see cref="GlobalRoles"/>: SuperAdmin and SystemAdmin apply in every tenant.
/// </para>
/// </summary>
public static class RoleAssignments
{
    /// <summary>
    /// Roles <paramref name="userId"/> holds in <paramref name="tenantId"/>.
    /// Query filters are bypassed: the tenant is named explicitly, and the
    /// platform meta-tenant would otherwise switch the role filter off.
    /// </summary>
    public static IQueryable<OlusoRole> HeldIn(
        this IQueryable<OlusoRole> roles,
        IQueryable<OlusoUserRole> userRoles,
        string userId,
        string? tenantId)
    {
        return roles
            .IgnoreQueryFilters()
            .Where(r => userRoles.Any(ur =>
                ur.UserId == userId
                && ur.RoleId == r.Id
                && (ur.TenantId == tenantId
                    || (r.TenantId == null
                        && (r.NormalizedName == GlobalRoles.SuperAdminNormalized
                            || r.NormalizedName == GlobalRoles.SystemAdminNormalized)))));
    }

    /// <summary>
    /// Assignments that apply in <paramref name="tenantId"/>.
    /// </summary>
    public static IQueryable<OlusoUserRole> ApplyingIn(
        this IQueryable<OlusoUserRole> userRoles,
        IQueryable<OlusoRole> roles,
        string tenantId)
    {
        var allRoles = roles.IgnoreQueryFilters();
        return userRoles.Where(ur =>
            ur.TenantId == tenantId
            || allRoles.Any(r => r.Id == ur.RoleId
                && r.TenantId == null
                && (r.NormalizedName == GlobalRoles.SuperAdminNormalized
                    || r.NormalizedName == GlobalRoles.SystemAdminNormalized)));
    }

    /// <summary>
    /// Refuses a tenant-owned role being assigned under another tenant.
    /// </summary>
    public static void EnsureAssignableIn(OlusoRole role, string? tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException(
                $"Role '{role.Name}' can't be assigned without a tenant: the request has no tenant and the user has no home tenant.");
        }

        if (role.TenantId != null && role.TenantId != tenantId)
        {
            throw new InvalidOperationException(
                $"Role '{role.Name}' belongs to tenant '{role.TenantId}' and can't be assigned in tenant '{tenantId}'.");
        }
    }

    /// <summary>
    /// Adds the assignment unless it is already held. Doesn't save.
    /// Returns true when a row was added.
    /// </summary>
    public static async Task<bool> GrantAsync(
        DbSet<OlusoUserRole> userRoles,
        OlusoRole role,
        string userId,
        string tenantId,
        string? assignedBy,
        CancellationToken cancellationToken)
    {
        EnsureAssignableIn(role, tenantId);

        // A global role is held once, wherever it was stamped.
        var global = GlobalRoles.IsGlobal(role);
        var exists = await userRoles.AnyAsync(ur =>
                ur.UserId == userId
                && ur.RoleId == role.Id
                && (global || ur.TenantId == tenantId),
            cancellationToken);
        if (exists)
        {
            return false;
        }

        userRoles.Add(new OlusoUserRole
        {
            UserId = userId,
            RoleId = role.Id,
            TenantId = tenantId,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = string.IsNullOrEmpty(assignedBy) ? "system" : assignedBy
        });
        return true;
    }

    /// <summary>
    /// Removes the assignment in <paramref name="tenantId"/> only; the user's
    /// assignments of the same role in other tenants are untouched. A global
    /// role applies everywhere, so every row of it goes. Doesn't save.
    /// Returns true when anything was removed.
    /// </summary>
    public static async Task<bool> RevokeAsync(
        DbSet<OlusoUserRole> userRoles,
        OlusoRole role,
        string userId,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var global = GlobalRoles.IsGlobal(role);
        var rows = await userRoles
            .Where(ur => ur.UserId == userId
                && ur.RoleId == role.Id
                && (global || ur.TenantId == tenantId))
            .ToListAsync(cancellationToken);
        userRoles.RemoveRange(rows);
        return rows.Count > 0;
    }
}
