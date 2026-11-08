using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Search;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Search.Providers;

/// <summary>
/// Roles of the tenant in context, hiding reserved roles the caller may not
/// see, exactly as the roles list does: a SuperAdmin sees all, an organisation
/// admin everything but platform roles, a tenant admin only tenant-assignable
/// reserved roles, anyone else no reserved roles at all.
/// </summary>
public sealed class RolesSearchProvider : IAdminSearchProvider
{
    private readonly IRoleStore _roles;

    public RolesSearchProvider(IRoleStore roles)
    {
        _roles = roles;
    }

    public string ProviderId => "platform.roles";
    public string Module => AdminSearchModules.Platform;
    public IReadOnlyList<string> RequiredPermissions { get; } = new[] { AdminPermissions.RolesRead };
    public string Category => "role";
    public string DisplayName => "Roles";
    public string Icon => "shield-check";

    public async Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default)
    {
        if (context.TenantId == null)
            return AdminSearchProviderResult.Empty;

        var roles = (await _roles.GetRolesAsync(context.TenantId, includeSystem: true, cancellationToken)).ToList();

        if (!context.User.IsSuperAdmin())
        {
            if (context.User.IsCurrentOrgAdmin())
                roles = roles.Where(r => !ReservedClaimTypes.IsSuperAdminOnlyRole(r.Name!)).ToList();
            else if (context.User.HasAdminRole("TenantAdmin") || context.User.HasAdminRole("Admin"))
                roles = roles.Where(r => !ReservedClaimTypes.IsReservedRoleName(r.Name!) || ReservedClaimTypes.IsTenantAdminAssignableRole(r.Name!)).ToList();
            else
                roles = roles.Where(r => !ReservedClaimTypes.IsReservedRoleName(r.Name!)).ToList();
        }

        var items = roles
            .Where(r => context.Matches(r.Name, r.DisplayName))
            .Select(r =>
            {
                var reserved = ReservedClaimTypes.IsReservedRoleName(r.Name!);
                return new AdminSearchResultItem
                {
                    Id = r.Id,
                    Title = r.DisplayName ?? r.Name!,
                    Subtitle = r.DisplayName == null ? null : r.Name,
                    Route = $"/roles?search={Uri.EscapeDataString(r.Name!)}",
                    Badges = reserved
                        ? new List<AdminSearchBadge> { new() { Label = "Kind", Value = "System", Tone = AdminSearchBadgeTones.Info } }
                        : new List<AdminSearchBadge>(),
                    Score = context.ScoreFor(r.DisplayName ?? r.Name, r.Name)
                };
            })
            .ToList();

        return new AdminSearchProviderResult
        {
            Items = items,
            TotalCount = items.Count,
            SeeAllRoute = $"/roles?search={Uri.EscapeDataString(context.Query)}"
        };
    }
}
