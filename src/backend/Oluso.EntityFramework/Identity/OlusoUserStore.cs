using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Identity;

/// <summary>
/// Identity's user store with tenant-scoped role assignments.
/// <para>
/// The stock store keys assignments by (UserId, RoleId) — FindUserRoleAsync
/// calls <c>FindAsync(userId, roleId)</c> — and reads every role a user holds in
/// any tenant. Assignments here also carry the tenant they apply in, so every
/// UserManager role call (GetRolesAsync, IsInRoleAsync, AddToRoleAsync,
/// RemoveFromRoleAsync, GetUsersInRoleAsync) is answered for one tenant: the
/// request's, or the user's home tenant outside a real tenant.
/// </para>
/// </summary>
public class OlusoUserStore<TContext>
    : UserStore<OlusoUser, OlusoRole, TContext, string, OlusoUserClaim, OlusoUserRole, OlusoUserLogin, OlusoUserToken, OlusoRoleClaim>
    where TContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public OlusoUserStore(TContext context, ITenantContext tenantContext, IdentityErrorDescriber? describer = null)
        : base(context, describer)
    {
        _tenantContext = tenantContext;
    }

    private DbSet<OlusoUserRole> UserRoleSet => Context.Set<OlusoUserRole>();
    private DbSet<OlusoRole> RoleSet => Context.Set<OlusoRole>();

    private string? ScopeTenant(OlusoUser user) =>
        RoleAssignmentTenant.Resolve(_tenantContext.TenantId, user.TenantId);

    public override async Task<IList<string>> GetRolesAsync(OlusoUser user, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        return await RoleSet
            .HeldIn(UserRoleSet, user.Id, ScopeTenant(user))
            .Select(r => r.Name!)
            .ToListAsync(cancellationToken);
    }

    public override async Task<bool> IsInRoleAsync(OlusoUser user, string normalizedRoleName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName);

        return await RoleSet
            .HeldIn(UserRoleSet, user.Id, ScopeTenant(user))
            .AnyAsync(r => r.NormalizedName == normalizedRoleName, cancellationToken);
    }

    public override async Task AddToRoleAsync(OlusoUser user, string normalizedRoleName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName);

        var tenantId = ScopeTenant(user);
        var candidates = await RoleSet
            .IgnoreQueryFilters()
            .Where(r => r.NormalizedName == normalizedRoleName && (r.TenantId == tenantId || r.TenantId == null))
            .ToListAsync(cancellationToken);
        var role = candidates.FirstOrDefault(r => r.TenantId != null) ?? candidates.FirstOrDefault()
            ?? throw new InvalidOperationException($"Role {normalizedRoleName} does not exist.");

        // UserManager saves via UpdateAsync afterwards.
        await RoleAssignments.GrantAsync(UserRoleSet, role, user.Id, tenantId!, "system", cancellationToken);
    }

    public override async Task RemoveFromRoleAsync(OlusoUser user, string normalizedRoleName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName);

        var tenantId = ScopeTenant(user);
        var held = await RoleSet
            .HeldIn(UserRoleSet, user.Id, tenantId)
            .Where(r => r.NormalizedName == normalizedRoleName)
            .ToListAsync(cancellationToken);
        foreach (var role in held)
        {
            await RoleAssignments.RevokeAsync(UserRoleSet, role, user.Id, tenantId, cancellationToken);
        }
    }

    /// <summary>
    /// Users holding the named role in the request's tenant; at platform scope
    /// or outside a request, holders of any row by that name.
    /// </summary>
    public override async Task<IList<OlusoUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName);

        var ambient = RoleAssignmentTenant.Resolve(_tenantContext.TenantId, null);
        var roleIds = RoleSet
            .IgnoreQueryFilters()
            .Where(r => r.NormalizedName == normalizedRoleName && (ambient == null || r.TenantId == ambient || r.TenantId == null))
            .Select(r => r.Id);

        IQueryable<OlusoUserRole> assignments = UserRoleSet;
        if (ambient != null)
        {
            assignments = assignments.ApplyingIn(RoleSet, ambient);
        }

        var userIds = assignments.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId);
        return await Users
            .IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The stock implementation is <c>FindAsync(userId, roleId)</c>, which no
    /// longer names a row. Answers for the tenant the user is scoped to.
    /// </summary>
    protected override async Task<OlusoUserRole?> FindUserRoleAsync(string userId, string roleId, CancellationToken cancellationToken)
    {
        var homeTenantId = await Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        var tenantId = RoleAssignmentTenant.Resolve(_tenantContext.TenantId, homeTenantId);

        return await UserRoleSet
            .ApplyingIn(RoleSet, tenantId ?? string.Empty)
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId, cancellationToken);
    }

    protected override OlusoUserRole CreateUserRole(OlusoUser user, OlusoRole role)
    {
        var tenantId = ScopeTenant(user);
        RoleAssignments.EnsureAssignableIn(role, tenantId);
        return new OlusoUserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            TenantId = tenantId!,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "system"
        };
    }
}
