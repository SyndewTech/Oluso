using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.EntityFramework.Identity;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IRoleStore using ASP.NET Core Identity
/// </summary>
public class RoleStore : IRoleStore
{
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly UserManager<OlusoUser> _userManager;
    private readonly IOlusoDbContext _context;
    private readonly ITenantContext _tenantContext;

    public RoleStore(
        RoleManager<OlusoRole> roleManager,
        UserManager<OlusoUser> userManager,
        IOlusoDbContext context,
        ITenantContext tenantContext)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<OlusoRole?> GetByIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        return await _roleManager.FindByIdAsync(roleId);
    }

    public async Task<IReadOnlyList<OlusoRole>> GetByIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        var ids = roleIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<OlusoRole>();
        }

        // Ids are explicit, so the ambient tenant filter must not hide rows;
        // callers apply their own tenant check to what comes back.
        return await _roleManager.Roles
            .IgnoreQueryFilters()
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<OlusoRole?> GetByNameAsync(string name, string? tenantId, CancellationToken cancellationToken = default)
    {
        // The caller names the tenant explicitly, so the ambient tenant filter
        // must not hide the row (e.g. seeding one tenant from another's context).
        var normalizedName = name.ToUpperInvariant();
        return await _roleManager.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.NormalizedName == normalizedName &&
                (r.TenantId == tenantId || (tenantId == null && r.TenantId == null)), cancellationToken);
    }

    public async Task<IEnumerable<OlusoRole>> GetRolesAsync(string? tenantId, bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        // The tenant is named explicitly, so the ambient filter must not hide its
        // rows (e.g. listing tenant B's roles while the request runs elsewhere).
        var query = _roleManager.Roles
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId || r.TenantId == null);

        if (!includeSystem)
        {
            query = query.Where(r => !r.IsSystemRole);
        }

        return await query.OrderBy(r => r.Name).ToListAsync(cancellationToken);
    }

    public async Task<OlusoRole> CreateAsync(OlusoRole role, CancellationToken cancellationToken = default)
    {
        role.NormalizedName = role.Name?.ToUpperInvariant();
        role.CreatedAt = DateTime.UtcNow;

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create role: {errors}");
        }

        return role;
    }

    public async Task<OlusoRole> UpdateAsync(OlusoRole role, CancellationToken cancellationToken = default)
    {
        role.NormalizedName = role.Name?.ToUpperInvariant();
        role.UpdatedAt = DateTime.UtcNow;

        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to update role: {errors}");
        }

        return role;
    }

    public async Task DeleteAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role != null)
        {
            var result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to delete role: {errors}");
            }
        }
    }

    public async Task<IEnumerable<RoleClaim>> GetRoleClaimsAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null)
        {
            return Enumerable.Empty<RoleClaim>();
        }

        var claims = await _roleManager.GetClaimsAsync(role);
        return claims.Select(c => new RoleClaim { Type = c.Type, Value = c.Value });
    }

    public async Task AddRoleClaimAsync(string roleId, string type, string value, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null)
        {
            throw new InvalidOperationException($"Role not found: {roleId}");
        }

        var result = await _roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(type, value));
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to add claim: {errors}");
        }
    }

    public async Task RemoveRoleClaimAsync(string roleId, string type, string value, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null)
        {
            throw new InvalidOperationException($"Role not found: {roleId}");
        }

        var result = await _roleManager.RemoveClaimAsync(role, new System.Security.Claims.Claim(type, value));
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to remove claim: {errors}");
        }
    }

    public async Task<int> GetUsersInRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var userRoles = ((DbContext)_context).Set<OlusoUserRole>();
        return await userRoles.CountAsync(ur => ur.RoleId == roleId, cancellationToken);
    }

    public Task<IEnumerable<RoleUserInfo>> GetUsersByRoleAsync(string roleId, CancellationToken cancellationToken = default)
        => ListHoldersAsync(roleId, AmbientTenant(), cancellationToken);

    public Task<IEnumerable<RoleUserInfo>> GetUsersByRoleInTenantAsync(string roleId, string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        return ListHoldersAsync(roleId, tenantId, cancellationToken);
    }

    public async Task<IEnumerable<OlusoRole>> GetUserRolesInTenantAsync(
        string userId, string? tenantId, CancellationToken cancellationToken = default)
    {
        return await _roleManager.Roles
            .HeldIn(UserRoles, userId, tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
    }

    public Task AssignToUserAsync(string roleId, string userId, CancellationToken cancellationToken = default)
        => AssignToUserAsync(roleId, userId, null, cancellationToken);

    public async Task AssignToUserAsync(string roleId, string userId, string? assignedBy, CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveAssignmentTenantAsync(userId, cancellationToken);
        await AssignToUserInTenantAsync(roleId, userId, tenantId!, assignedBy, cancellationToken);
    }

    public async Task AssignToUserInTenantAsync(
        string roleId, string userId, string tenantId, string? assignedBy, CancellationToken cancellationToken = default)
    {
        var role = await RequireRoleAsync(roleId, cancellationToken);
        if (await RoleAssignments.GrantAsync(UserRoles, role, userId, tenantId, assignedBy, cancellationToken))
        {
            await ((DbContext)_context).SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveFromUserAsync(string roleId, string userId, CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveAssignmentTenantAsync(userId, cancellationToken);
        await RemoveFromUserInTenantAsync(roleId, userId, tenantId!, cancellationToken);
    }

    public async Task RemoveFromUserInTenantAsync(
        string roleId, string userId, string tenantId, CancellationToken cancellationToken = default)
    {
        var role = await RequireRoleAsync(roleId, cancellationToken);
        if (await RoleAssignments.RevokeAsync(UserRoles, role, userId, tenantId, cancellationToken))
        {
            await ((DbContext)_context).SaveChangesAsync(cancellationToken);
        }
    }

    private DbSet<OlusoUserRole> UserRoles => ((DbContext)_context).Set<OlusoUserRole>();

    /// <summary>
    /// The request's tenant, or null at platform scope or outside a request.
    /// </summary>
    private string? AmbientTenant()
    {
        var tenantId = _tenantContext.TenantId;
        return string.IsNullOrEmpty(tenantId) || tenantId == PlatformConstants.TenantId ? null : tenantId;
    }

    /// <summary>
    /// Assignments that apply in <paramref name="tenantId"/>; with no tenant
    /// (platform scope, SuperAdmin-gated) every assignment.
    /// </summary>
    private IQueryable<OlusoUserRole> AssignmentsInScope(string? tenantId) =>
        tenantId == null ? UserRoles : UserRoles.ApplyingIn(_roleManager.Roles, tenantId);

    private async Task<IEnumerable<RoleUserInfo>> ListHoldersAsync(string roleId, string? tenantId, CancellationToken cancellationToken)
    {
        var holderIds = AssignmentsInScope(tenantId)
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId);

        // The user filter clamps to the home tenant; the assignment already says
        // where it applies, so holders whose home tenant is elsewhere count too.
        var users = await _userManager.Users
            .IgnoreQueryFilters()
            .Where(u => holderIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName ?? u.Email)
            .Select(u => new { u.Id, u.UserName, u.Email, u.DisplayName, u.FirstName, u.LastName, u.IsActive, u.TenantId })
            .ToListAsync(cancellationToken);

        return users.Select(u =>
        {
            var fullName = $"{u.FirstName} {u.LastName}".Trim();
            return new RoleUserInfo
            {
                Id = u.Id,
                UserName = u.UserName ?? "",
                Email = u.Email ?? "",
                DisplayName = u.DisplayName ?? (fullName.Length > 0 ? fullName : null),
                IsActive = u.IsActive,
                TenantId = u.TenantId
            };
        }).ToList();
    }

    /// <summary>
    /// The request's tenant, or the user's home tenant at platform scope.
    /// </summary>
    private async Task<string?> ResolveAssignmentTenantAsync(string userId, CancellationToken cancellationToken)
    {
        var ambient = AmbientTenant();
        if (ambient != null)
        {
            return ambient;
        }

        var homeTenantId = await _userManager.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        return RoleAssignmentTenant.Resolve(_tenantContext.TenantId, homeTenantId);
    }

    private async Task<OlusoRole> RequireRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        return await _roleManager.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{roleId}' not found");
    }
}
