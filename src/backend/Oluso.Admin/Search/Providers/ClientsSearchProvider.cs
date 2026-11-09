using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Search;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Search.Providers;

/// <summary>OAuth clients of the tenant in context, by client id, name or description.</summary>
public sealed class ClientsSearchProvider : IAdminSearchProvider
{
    private readonly IClientStore _clients;

    public ClientsSearchProvider(IClientStore clients)
    {
        _clients = clients;
    }

    public string ProviderId => "platform.clients";
    public string Module => AdminSearchModules.Platform;
    public IReadOnlyList<string> RequiredPermissions { get; } = new[] { AdminPermissions.ClientsRead };
    public string Category => "client";
    public string DisplayName => "Applications";
    public string Icon => "cube";

    public IReadOnlyList<AdminSearchFilterDefinition> Filters { get; } = new[]
    {
        new AdminSearchFilterDefinition { Key = "enabled", Label = "Enabled", Type = AdminSearchFilterTypes.Boolean },
    };

    public async Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default)
    {
        if (context.TenantId == null)
            return AdminSearchProviderResult.Empty;

        var enabled = ParseBool(context.Filter("enabled"));
        var matches = (await _clients.GetAllClientsAsync(cancellationToken))
            .Where(c => enabled == null || c.Enabled == enabled)
            .Where(c => context.Matches(c.ClientId, c.ClientName, c.Description))
            .ToList();

        var items = matches.Select(c => new AdminSearchResultItem
        {
            Id = c.Id.ToString(),
            Title = string.IsNullOrWhiteSpace(c.ClientName) ? c.ClientId : c.ClientName!,
            Subtitle = c.ClientId,
            Description = c.Description,
            Route = $"/clients/{c.Id}",
            ImageUrl = c.LogoUri,
            Badges = c.Enabled
                ? new List<AdminSearchBadge> { new() { Label = "Protocol", Value = c.ProtocolType.ToUpperInvariant(), Tone = AdminSearchBadgeTones.Info } }
                : new List<AdminSearchBadge> { new() { Label = "Status", Value = "Disabled", Tone = AdminSearchBadgeTones.Warning } },
            Score = context.ScoreFor(c.ClientName, c.ClientId, c.Description)
        }).ToList();

        return new AdminSearchProviderResult
        {
            Items = items,
            TotalCount = items.Count,
            SeeAllRoute = $"/clients?search={Uri.EscapeDataString(context.Query)}"
        };
    }

    internal static bool? ParseBool(string? value) => value?.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" => true,
        "false" or "0" or "no" => false,
        _ => null
    };
}
