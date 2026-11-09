using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;

namespace Oluso.Admin;

/// <summary>
/// Extension methods for admin module startup tasks.
/// </summary>
public static class AdminStartupExtensions
{
    /// <summary>
    /// Ensures all module-registered admin roles exist in the database.
    /// Call after MigrateOlusoDatabaseAsync().
    /// </summary>
    public static async Task SeedModuleRolesAsync(this WebApplication app)
    {
        var moduleRoles = PermissionRegistry.Instance.GetModuleRoles();
        if (moduleRoles.Count == 0) return;

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
        var logger = scope.ServiceProvider.GetService<ILogger<OlusoDbContext>>();

        // Module roles are platform-scoped system rows and may use a reserved
        // name (OrgAdmin), which ReservedAuthInterceptor blocks on every other
        // path. Seed code is the one trusted caller.
        using var seedMode = dbContext.BeginSeedMode();

        foreach (var roleDef in moduleRoles)
        {
            var normalizedName = roleDef.Name.ToUpperInvariant();
            var existing = await dbContext.Roles
                .FirstOrDefaultAsync(r => r.NormalizedName == normalizedName
                    && r.TenantId == null);

            if (existing == null)
            {
                // Create system role
                var role = new OlusoRole
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = roleDef.Name,
                    NormalizedName = normalizedName,
                    DisplayName = roleDef.DisplayName,
                    Description = roleDef.Description,
                    TenantId = null,
                    IsSystemRole = true,
                    Category = roleDef.Category,
                    ManagedByRole = null,
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Roles.Add(role);
                await dbContext.SaveChangesAsync();

                // Add permissions
                foreach (var perm in roleDef.Permissions)
                {
                    dbContext.RoleClaims.Add(new OlusoRoleClaim
                    {
                        RoleId = role.Id,
                        ClaimType = OlusoRole.PermissionClaimType,
                        ClaimValue = perm,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                await dbContext.SaveChangesAsync();

                logger?.LogInformation("Seeded module role {RoleName} with {Count} permissions",
                    roleDef.Name, roleDef.Permissions.Length);
            }
            else
            {
                // Ensure permissions are up to date
                var existingClaims = await dbContext.RoleClaims
                    .Where(c => c.RoleId == existing.Id && c.ClaimType == OlusoRole.PermissionClaimType)
                    .Select(c => c.ClaimValue)
                    .ToListAsync();

                var added = 0;
                foreach (var perm in roleDef.Permissions)
                {
                    if (!existingClaims.Contains(perm, StringComparer.OrdinalIgnoreCase))
                    {
                        dbContext.RoleClaims.Add(new OlusoRoleClaim
                        {
                            RoleId = existing.Id,
                            ClaimType = OlusoRole.PermissionClaimType,
                            ClaimValue = perm,
                            CreatedAt = DateTime.UtcNow
                        });
                        added++;
                    }
                }

                if (added > 0)
                {
                    await dbContext.SaveChangesAsync();
                    logger?.LogInformation("Updated module role {RoleName}: added {Count} missing permissions",
                        roleDef.Name, added);
                }
            }
        }
    }
}
