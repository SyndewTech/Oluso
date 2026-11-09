using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Search;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Search.Providers;

/// <summary>
/// Organisations, scoped as the organisations list is: all of them for a
/// SuperAdmin, only the caller's own memberships otherwise.
/// </summary>
public sealed class OrganizationsSearchProvider : IAdminSearchProvider
{
    private readonly IOrganizationStore _organizations;
    private readonly IOrganizationMembershipStore _memberships;

    public OrganizationsSearchProvider(IOrganizationStore organizations, IOrganizationMembershipStore memberships)
    {
        _organizations = organizations;
        _memberships = memberships;
    }

    public string ProviderId => "platform.organizations";
    public string Module => AdminSearchModules.Platform;
    public IReadOnlyList<string> RequiredPermissions { get; } = new[] { AdminPermissions.OrganizationsRead };
    public string Category => "organization";
    public string DisplayName => "Organisations";
    public string Icon => "briefcase";

    public IReadOnlyList<AdminSearchFilterDefinition> Filters { get; } = new[]
    {
        new AdminSearchFilterDefinition { Key = "enabled", Label = "Enabled", Type = AdminSearchFilterTypes.Boolean },
    };

    public async Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<string>? organizationIds = null;
        if (!context.User.IsSuperAdmin())
        {
            var memberships = await _memberships.GetByUserAsync(context.UserId, cancellationToken);
            organizationIds = memberships.Select(m => m.OrganizationId).Distinct().ToList();
            if (organizationIds.Count == 0)
                return AdminSearchProviderResult.Empty;
        }

        var (organizations, totalCount) = await _organizations.SearchAsync(new OrganizationSearchQuery
        {
            Search = context.Query,
            OrganizationIds = organizationIds,
            Enabled = ClientsSearchProvider.ParseBool(context.Filter("enabled")),
            Skip = 0,
            Take = context.Limit
        }, cancellationToken);

        return new AdminSearchProviderResult
        {
            Items = organizations.Select(o => new AdminSearchResultItem
            {
                Id = o.Id,
                Title = o.Name,
                Subtitle = o.Slug,
                Description = o.Description,
                Route = $"/organizations/{o.Id}",
                ImageUrl = o.LogoUrl,
                Badges = o.Enabled
                    ? new List<AdminSearchBadge>()
                    : new List<AdminSearchBadge> { new() { Label = "Status", Value = "Disabled", Tone = AdminSearchBadgeTones.Warning } },
                Score = context.ScoreFor(o.Name, o.Slug, o.Description)
            }).ToList(),
            TotalCount = totalCount,
            SeeAllRoute = $"/organizations?search={Uri.EscapeDataString(context.Query)}"
        };
    }
}
