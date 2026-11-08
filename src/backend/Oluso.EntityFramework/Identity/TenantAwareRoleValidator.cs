using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Identity;

/// <summary>
/// Role-name uniqueness scoped to the tenant that owns the role.
/// <para>
/// Identity's <see cref="RoleValidator{TRole}"/> treats names as globally unique:
/// it calls <c>FindByNameAsync</c> and rejects on any hit. Roles here are
/// tenant-scoped — every tenant gets its own "Admin" from
/// <c>SeedDefaultRolesAsync</c> — so the second tenant to be created was refused
/// with "Role name 'Admin' is already taken", even though the database's
/// <c>(TenantId, NormalizedName)</c> index would have accepted it.
/// </para>
/// <para>
/// Query filters are bypassed on purpose: the check must see roles in other
/// tenants to compare against, and it scopes by TenantId itself.
/// </para>
/// </summary>
public class TenantAwareRoleValidator : IRoleValidator<OlusoRole>
{
    public async Task<IdentityResult> ValidateAsync(RoleManager<OlusoRole> manager, OlusoRole role)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(role);

        var errors = new List<IdentityError>();
        var roleName = await manager.GetRoleNameAsync(role);

        if (string.IsNullOrWhiteSpace(roleName))
        {
            errors.Add(manager.ErrorDescriber.InvalidRoleName(roleName));
            return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
        }

        var normalizedName = manager.NormalizeKey(roleName);

        var duplicate = await manager.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.NormalizedName == normalizedName && r.TenantId == role.TenantId);

        if (duplicate != null && duplicate.Id != role.Id)
        {
            errors.Add(manager.ErrorDescriber.DuplicateRoleName(roleName));
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }
}
