using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Services;

/// <summary>
/// "Admin" is a reserved role name and "*" is not a storable permission on the
/// ordinary write path, so <see cref="ReservedAuthInterceptor"/> rejects both.
/// The tenant defaults are platform-owned rows, written here in seed mode, the
/// same trusted path the startup seeders use.
/// </summary>
public sealed class TenantDefaultRoleSeeder : ITenantDefaultRoleSeeder
{
    private const string AdminRoleName = "Admin";
    private const string UserRoleName = "User";

    private readonly OlusoDbContext _db;
    private readonly ILogger<TenantDefaultRoleSeeder> _logger;

    public TenantDefaultRoleSeeder(OlusoDbContext db, ILogger<TenantDefaultRoleSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TenantDefaultRoles> SeedAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        using var seedMode = _db.BeginSeedMode();
        try
        {
            var admin = await EnsureRoleAsync(
                tenantId, AdminRoleName, "Administrator", "Full access within the tenant",
                managedBy: "SuperAdmin", permissions: new[] { "admin.access", "*" }, cancellationToken);

            var user = await EnsureRoleAsync(
                tenantId, UserRoleName, "User", "Standard user access",
                managedBy: AdminRoleName, permissions: new[] { "dashboard.view" }, cancellationToken);

            return new TenantDefaultRoles(admin, user);
        }
        catch
        {
            // The context is shared with the caller. Rows that failed to save stay tracked as Added, so the
            // caller's next save (e.g. deleting the half-made tenant) would try them again outside seed mode,
            // where the reserved-role guard rejects "Admin". Let go of them before handing the failure back.
            foreach (var entry in _db.ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Added && e.Entity is OlusoRole or OlusoRoleClaim)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }
            throw;
        }
    }

    private async Task<OlusoRole> EnsureRoleAsync(
        string tenantId,
        string name,
        string displayName,
        string description,
        string managedBy,
        string[] permissions,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.ToUpperInvariant();

        // The request runs in the creating admin's tenant, so the ambient
        // filter would hide the new tenant's rows.
        var role = await _db.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.NormalizedName == normalizedName, cancellationToken);

        if (role == null)
        {
            role = new OlusoRole
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                NormalizedName = normalizedName,
                DisplayName = displayName,
                Description = description,
                TenantId = tenantId,
                IsSystemRole = false,
                Category = "tenant",
                ManagedByRole = managedBy,
                CreatedAt = DateTime.UtcNow
            };
            _db.Roles.Add(role);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Seeded default role {Role} for tenant {TenantId}", name, tenantId);
        }

        var existing = await _db.RoleClaims
            .Where(c => c.RoleId == role.Id && c.ClaimType == OlusoRole.PermissionClaimType)
            .Select(c => c.ClaimValue)
            .ToListAsync(cancellationToken);

        var added = false;
        foreach (var permission in permissions)
        {
            if (existing.Contains(permission, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _db.RoleClaims.Add(new OlusoRoleClaim
            {
                RoleId = role.Id,
                ClaimType = OlusoRole.PermissionClaimType,
                ClaimValue = permission,
                CreatedAt = DateTime.UtcNow
            });
            added = true;
        }

        if (added)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return role;
    }
}
