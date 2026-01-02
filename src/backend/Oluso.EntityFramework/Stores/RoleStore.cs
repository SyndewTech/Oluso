using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IRoleStore using ASP.NET Core Identity
/// </summary>
public class RoleStore : IRoleStore
{
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly UserManager<OlusoUser> _userManager;

    public RoleStore(RoleManager<OlusoRole> roleManager, UserManager<OlusoUser> userManager)
    {
        _roleManager = roleManager;
        _userManager = userManager;
    }

    public async Task<OlusoRole?> GetByIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        return await _roleManager.FindByIdAsync(roleId);
    }

    public async Task<OlusoRole?> GetByNameAsync(string name, string? tenantId, CancellationToken cancellationToken = default)
    {
        // RoleManager doesn't support tenant-aware queries, so we query directly
        return await _roleManager.Roles
            .FirstOrDefaultAsync(r => r.NormalizedName == name.ToUpperInvariant() &&
                (r.TenantId == tenantId || (tenantId == null && r.TenantId == null)), cancellationToken);
    }

    public async Task<IEnumerable<OlusoRole>> GetRolesAsync(string? tenantId, bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        var query = _roleManager.Roles
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
            if (role.IsSystemRole)
            {
                throw new InvalidOperationException("Cannot delete system roles");
            }

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
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null || role.Name == null)
        {
            return 0;
        }

        // Use UserManager to get users in role
        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name);
        return usersInRole.Count;
    }

    public async Task<IEnumerable<RoleUserInfo>> GetUsersByRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null || role.Name == null)
        {
            return Enumerable.Empty<RoleUserInfo>();
        }

        // Use UserManager to get users in role
        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name);
        return usersInRole.Select(u => new RoleUserInfo
        {
            Id = u.Id,
            UserName = u.UserName ?? "",
            Email = u.Email ?? "",
            DisplayName = u.DisplayName
        });
    }
}
