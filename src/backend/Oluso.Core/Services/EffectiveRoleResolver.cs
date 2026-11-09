using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// The roles whose permissions a user has in a tenant: the roles assigned to
/// them in that tenant, plus the organization-admin grant.
/// <para>
/// The organization-admin grant is derived, never assigned: a user whose
/// <see cref="OrganizationMembership"/> in the organization owning the tenant is
/// Owner or Admin (the same source as the <c>is_org_admin</c> claim) gets the
/// permissions of the platform-wide OrgAdmin role row in every tenant of that
/// organization, and nowhere else. The row's permission claims stay editable
/// where they are today. OrgAdmin assignment rows are ignored: membership is
/// the only input to who is an organization admin and where.
/// </para>
/// Token minting, the permission handler's database path and the permissions
/// API all read effective permissions from here.
/// </summary>
public interface IEffectiveRoleResolver
{
    Task<IReadOnlyList<OlusoRole>> GetEffectiveRolesAsync(
        string userId, string? tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(
        string userId, string? tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Permissions a user holds because of who they are in a module rather than a role they
/// were assigned (e.g. every employee gets their own self-service). Merged into
/// <see cref="IEffectiveRoleResolver.GetEffectivePermissionsAsync"/> for tenant requests.
/// </summary>
public interface IDerivedPermissionSource
{
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(string userId, string tenantId, CancellationToken cancellationToken = default);
}

public sealed class EffectiveRoleResolver : IEffectiveRoleResolver
{
    /// <summary>Name of the platform-wide role row carrying the organization-admin permission set.</summary>
    public const string OrgAdminRoleName = "OrgAdmin";

    private readonly IRoleStore _roleStore;
    private readonly ITenantStore _tenantStore;
    private readonly IOrganizationMembershipStore? _organizationMembershipStore;
    private readonly IEnumerable<IDerivedPermissionSource> _derivedSources;

    public EffectiveRoleResolver(
        IRoleStore roleStore,
        ITenantStore tenantStore,
        IOrganizationMembershipStore? organizationMembershipStore = null,
        IEnumerable<IDerivedPermissionSource>? derivedSources = null)
    {
        _roleStore = roleStore;
        _tenantStore = tenantStore;
        _organizationMembershipStore = organizationMembershipStore;
        _derivedSources = derivedSources ?? Array.Empty<IDerivedPermissionSource>();
    }

    public async Task<IReadOnlyList<OlusoRole>> GetEffectiveRolesAsync(
        string userId, string? tenantId, CancellationToken cancellationToken = default)
    {
        var orgAdminRole = await _roleStore.GetByNameAsync(OrgAdminRoleName, null, cancellationToken);

        var roles = (await _roleStore.GetUserRolesInTenantAsync(userId, tenantId, cancellationToken))
            .Where(r => orgAdminRole == null || r.Id != orgAdminRole.Id)
            .ToList();

        if (orgAdminRole != null && await LeadsTenantOrganizationAsync(userId, tenantId, cancellationToken))
        {
            roles.Add(orgAdminRole);
        }

        return roles;
    }

    public async Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(
        string userId, string? tenantId, CancellationToken cancellationToken = default)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in await GetEffectiveRolesAsync(userId, tenantId, cancellationToken))
        {
            foreach (var claim in await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken))
            {
                if (claim.Type == OlusoRole.PermissionClaimType)
                {
                    permissions.Add(claim.Value);
                }
            }
        }

        if (!string.IsNullOrEmpty(tenantId) && tenantId != PlatformConstants.TenantId)
        {
            foreach (var source in _derivedSources)
            {
                permissions.UnionWith(await source.GetPermissionsAsync(userId, tenantId, cancellationToken));
            }
        }

        return permissions;
    }

    private async Task<bool> LeadsTenantOrganizationAsync(string userId, string? tenantId, CancellationToken cancellationToken)
    {
        if (_organizationMembershipStore == null
            || string.IsNullOrEmpty(tenantId)
            || tenantId == PlatformConstants.TenantId)
        {
            return false;
        }

        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (string.IsNullOrEmpty(tenant?.OrganizationId))
        {
            return false;
        }

        var membership = await _organizationMembershipStore.GetByUserAndOrganizationAsync(
            userId, tenant.OrganizationId, cancellationToken);
        return membership?.Role is OrganizationRole.Owner or OrganizationRole.Admin;
    }
}
