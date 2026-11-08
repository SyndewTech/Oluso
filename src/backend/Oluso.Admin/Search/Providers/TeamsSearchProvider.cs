using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Search;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Search.Providers;

/// <summary>Teams of the tenant in context, by name or code.</summary>
public sealed class TeamsSearchProvider : IAdminSearchProvider
{
    private readonly ITeamStore _teams;

    public TeamsSearchProvider(ITeamStore teams)
    {
        _teams = teams;
    }

    public string ProviderId => "platform.teams";
    public string Module => AdminSearchModules.Platform;
    public IReadOnlyList<string> RequiredPermissions { get; } = new[] { AdminPermissions.TeamsRead };
    public string Category => "team";
    public string DisplayName => "Teams";
    public string Icon => "user-group";

    public IReadOnlyList<AdminSearchFilterDefinition> Filters { get; } = new[]
    {
        new AdminSearchFilterDefinition
        {
            Key = "type", Label = "Type", Type = AdminSearchFilterTypes.Select,
            Options = Enum.GetNames<TeamType>().Select(n => new AdminSearchFilterOption { Value = n, Label = n }).ToList()
        },
        new AdminSearchFilterDefinition { Key = "active", Label = "Active", Type = AdminSearchFilterTypes.Boolean },
    };

    public async Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default)
    {
        if (context.TenantId == null)
            return AdminSearchProviderResult.Empty;

        TeamType? type = Enum.TryParse<TeamType>(context.Filter("type"), ignoreCase: true, out var parsed) ? parsed : null;
        var (items, totalCount) = await _teams.QueryAsync(context.TenantId, new TeamQuery
        {
            Search = context.Query,
            Type = type,
            IsActive = ClientsSearchProvider.ParseBool(context.Filter("active")),
            Skip = 0,
            Take = context.Limit
        }, cancellationToken);

        return new AdminSearchProviderResult
        {
            Items = items.Select(t => new AdminSearchResultItem
            {
                Id = t.Id,
                Title = t.Name,
                Subtitle = t.Code,
                Description = t.Description,
                Route = $"/teams/{t.Id}",
                Badges = new List<AdminSearchBadge>
                {
                    new() { Label = "Type", Value = t.Type.ToString(), Tone = AdminSearchBadgeTones.Info },
                    new() { Label = "Status", Value = t.IsActive ? "Active" : "Inactive", Tone = t.IsActive ? AdminSearchBadgeTones.Success : AdminSearchBadgeTones.Warning },
                },
                Score = context.ScoreFor(t.Name, t.Code, t.Description)
            }).ToList(),
            TotalCount = totalCount,
            SeeAllRoute = $"/teams?search={Uri.EscapeDataString(context.Query)}"
        };
    }
}
