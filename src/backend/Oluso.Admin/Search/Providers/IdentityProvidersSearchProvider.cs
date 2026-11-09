using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Search;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Search.Providers;

/// <summary>Identity providers of the tenant in context, by scheme or display name.</summary>
public sealed class IdentityProvidersSearchProvider : IAdminSearchProvider
{
    private readonly IIdentityProviderStore _providers;

    public IdentityProvidersSearchProvider(IIdentityProviderStore providers)
    {
        _providers = providers;
    }

    public string ProviderId => "platform.identity-providers";
    public string Module => AdminSearchModules.Platform;
    public IReadOnlyList<string> RequiredPermissions { get; } = new[] { AdminPermissions.IdentityProvidersRead };
    public string Category => "identity-provider";
    public string DisplayName => "Identity Providers";
    public string Icon => "key";

    public IReadOnlyList<AdminSearchFilterDefinition> Filters { get; } = new[]
    {
        new AdminSearchFilterDefinition
        {
            Key = "type", Label = "Type", Type = AdminSearchFilterTypes.Select,
            Options = Enum.GetNames<ExternalProviderType>().Select(n => new AdminSearchFilterOption { Value = n, Label = n }).ToList()
        },
        new AdminSearchFilterDefinition { Key = "enabled", Label = "Enabled", Type = AdminSearchFilterTypes.Boolean },
    };

    public async Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default)
    {
        if (context.TenantId == null)
            return AdminSearchProviderResult.Empty;

        ExternalProviderType? type = Enum.TryParse<ExternalProviderType>(context.Filter("type"), ignoreCase: true, out var parsed) ? parsed : null;
        var enabled = ClientsSearchProvider.ParseBool(context.Filter("enabled"));

        var items = (await _providers.GetAllAsync(cancellationToken))
            .Where(p => type == null || p.ProviderType == type)
            .Where(p => enabled == null || p.Enabled == enabled)
            .Where(p => context.Matches(p.Scheme, p.DisplayName, p.ProviderType.ToString()))
            .Select(p => new AdminSearchResultItem
            {
                Id = p.Id.ToString(),
                Title = p.DisplayName ?? p.Scheme,
                Subtitle = p.Scheme,
                Route = $"/identity-providers/{p.Id}",
                ImageUrl = p.IconUrl,
                Badges = new List<AdminSearchBadge>
                {
                    new() { Label = "Type", Value = p.ProviderType.ToString(), Tone = AdminSearchBadgeTones.Info },
                    new() { Label = "Status", Value = p.Enabled ? "Enabled" : "Disabled", Tone = p.Enabled ? AdminSearchBadgeTones.Success : AdminSearchBadgeTones.Warning },
                },
                Score = context.ScoreFor(p.DisplayName ?? p.Scheme, p.Scheme)
            })
            .ToList();

        return new AdminSearchProviderResult
        {
            Items = items,
            TotalCount = items.Count,
            SeeAllRoute = $"/identity-providers?search={Uri.EscapeDataString(context.Query)}"
        };
    }
}
