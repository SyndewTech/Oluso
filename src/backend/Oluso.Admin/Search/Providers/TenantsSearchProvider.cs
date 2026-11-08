using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Search;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Search.Providers;

/// <summary>
/// Tenants, scoped as the tenants list is: every tenant for a SuperAdmin, the
/// tenants of the caller's organisations for an organisation member, nothing
/// for anyone else.
/// </summary>
public sealed class TenantsSearchProvider : IAdminSearchProvider
{
    private readonly ITenantStore _tenants;

    public TenantsSearchProvider(ITenantStore tenants)
    {
        _tenants = tenants;
    }

    public string ProviderId => "platform.tenants";
    public string Module => AdminSearchModules.Platform;
    public IReadOnlyList<string> RequiredPermissions { get; } = new[] { AdminPermissions.TenantsRead };
    public string Category => "tenant";
    public string DisplayName => "Tenants";
    public string Icon => "building";

    public IReadOnlyList<AdminSearchFilterDefinition> Filters { get; } = new[]
    {
        new AdminSearchFilterDefinition { Key = "enabled", Label = "Enabled", Type = AdminSearchFilterTypes.Boolean },
    };

    public async Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<string>? organizationIds = null;
        if (!context.User.IsSuperAdmin())
        {
            organizationIds = context.User.GetOrganizationIds();
            if (organizationIds.Count == 0)
                return AdminSearchProviderResult.Empty;
        }

        var (tenants, totalCount) = await _tenants.SearchAsync(new TenantSearchQuery
        {
            Search = context.Query,
            OrganizationIds = organizationIds,
            Enabled = ClientsSearchProvider.ParseBool(context.Filter("enabled")),
            Skip = 0,
            Take = context.Limit
        }, cancellationToken);

        return new AdminSearchProviderResult
        {
            Items = tenants.Select(t => new AdminSearchResultItem
            {
                Id = t.Id,
                Title = t.DisplayName ?? t.Name,
                Subtitle = t.Identifier,
                Description = t.Description,
                Route = $"/tenants/{t.Id}",
                Badges = t.Enabled
                    ? new List<AdminSearchBadge>()
                    : new List<AdminSearchBadge> { new() { Label = "Status", Value = "Disabled", Tone = AdminSearchBadgeTones.Warning } },
                Score = context.ScoreFor(t.DisplayName ?? t.Name, t.Name, t.Identifier, t.Description)
            }).ToList(),
            TotalCount = totalCount,
            SeeAllRoute = $"/tenants?search={Uri.EscapeDataString(context.Query)}"
        };
    }
}
