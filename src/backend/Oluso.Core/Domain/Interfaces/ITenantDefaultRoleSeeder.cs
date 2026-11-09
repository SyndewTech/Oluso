using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

public sealed record TenantDefaultRoles(OlusoRole Admin, OlusoRole User);

/// <summary>
/// Creates the Admin and User roles every tenant starts with. Idempotent:
/// rows that already exist are returned untouched, missing permissions are
/// added.
/// </summary>
public interface ITenantDefaultRoleSeeder
{
    Task<TenantDefaultRoles> SeedAsync(string tenantId, CancellationToken cancellationToken = default);
}
