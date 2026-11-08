using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Search;
using Oluso.Core.UserJourneys;

namespace Oluso.Admin.Search.Providers;

/// <summary>User journey policies visible to the tenant: its own plus the global ones, as the journeys list shows them.</summary>
public sealed class JourneysSearchProvider : IAdminSearchProvider
{
    private readonly IJourneyPolicyStore _policies;

    public JourneysSearchProvider(IJourneyPolicyStore policies)
    {
        _policies = policies;
    }

    public string ProviderId => "platform.journeys";
    public string Module => AdminSearchModules.Platform;
    public IReadOnlyList<string> RequiredPermissions { get; } = new[] { AdminPermissions.JourneysRead };
    public string Category => "journey";
    public string DisplayName => "User Journeys";
    public string Icon => "map";

    public IReadOnlyList<AdminSearchFilterDefinition> Filters { get; } = new[]
    {
        new AdminSearchFilterDefinition
        {
            Key = "type", Label = "Type", Type = AdminSearchFilterTypes.Select,
            Options = Enum.GetNames<JourneyType>().Select(n => new AdminSearchFilterOption { Value = n, Label = n }).ToList()
        },
        new AdminSearchFilterDefinition { Key = "enabled", Label = "Enabled", Type = AdminSearchFilterTypes.Boolean },
        new AdminSearchFilterDefinition { Key = "scope", Label = "Scope", Type = AdminSearchFilterTypes.Select,
            Options = new List<AdminSearchFilterOption>
            {
                new() { Value = "tenant", Label = "This tenant" },
                new() { Value = "global", Label = "Global" },
            }
        },
    };

    public async Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default)
    {
        var policies = await _policies.GetByTenantAsync(context.TenantId, cancellationToken);

        JourneyType? type = Enum.TryParse<JourneyType>(context.Filter("type"), ignoreCase: true, out var parsed) ? parsed : null;
        var enabled = ClientsSearchProvider.ParseBool(context.Filter("enabled"));
        var scope = context.Filter("scope");

        var items = policies
            .Where(p => type == null || p.Type == type)
            .Where(p => enabled == null || p.Enabled == enabled)
            .Where(p => scope switch
            {
                "tenant" => p.TenantId == context.TenantId && context.TenantId != null,
                "global" => p.TenantId == null,
                _ => true
            })
            .Where(p => context.Matches(p.Name, p.Description, p.Type.ToString()))
            .Select(p =>
            {
                var isGlobal = p.TenantId == null;
                return new AdminSearchResultItem
                {
                    Id = p.Id,
                    Title = p.Name,
                    Subtitle = p.Type.ToString(),
                    Description = p.Description,
                    Route = $"/experience/journeys/{p.Id}",
                    Badges = new List<AdminSearchBadge>
                    {
                        new() { Label = "Status", Value = p.Enabled ? "Enabled" : "Disabled", Tone = p.Enabled ? AdminSearchBadgeTones.Success : AdminSearchBadgeTones.Warning },
                        new() { Label = "Scope", Value = isGlobal ? "Global" : "Tenant", Tone = isGlobal ? AdminSearchBadgeTones.Neutral : AdminSearchBadgeTones.Info },
                    },
                    Score = context.ScoreFor(p.Name, p.Description, p.Type.ToString())
                };
            })
            .ToList();

        return new AdminSearchProviderResult
        {
            Items = items,
            TotalCount = items.Count,
            SeeAllRoute = $"/experience/journeys?search={Uri.EscapeDataString(context.Query)}"
        };
    }
}
