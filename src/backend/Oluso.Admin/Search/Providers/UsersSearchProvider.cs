using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Search;
using Oluso.Core.Services;

namespace Oluso.Admin.Search.Providers;

/// <summary>Users of the tenant in context, by name, username or email.</summary>
public sealed class UsersSearchProvider : IAdminSearchProvider
{
    private readonly IOlusoUserService _users;

    public UsersSearchProvider(IOlusoUserService users)
    {
        _users = users;
    }

    public string ProviderId => "platform.users";
    public string Module => AdminSearchModules.Platform;
    public IReadOnlyList<string> RequiredPermissions { get; } = new[] { AdminPermissions.UsersRead };
    public string Category => "user";
    public string DisplayName => "Users";
    public string Icon => "users";

    public IReadOnlyList<AdminSearchFilterDefinition> Filters { get; } = new[]
    {
        new AdminSearchFilterDefinition
        {
            Key = "status", Label = "Status", Type = AdminSearchFilterTypes.Select,
            Options = new List<AdminSearchFilterOption>
            {
                new() { Value = "active", Label = "Active" },
                new() { Value = "inactive", Label = "Inactive" },
            }
        },
        new AdminSearchFilterDefinition { Key = "role", Label = "Role", Type = AdminSearchFilterTypes.Text, Placeholder = "Role name" },
    };

    public async Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default)
    {
        if (context.TenantId == null)
            return AdminSearchProviderResult.Empty;

        var status = context.Filter("status");
        var result = await _users.GetUsersAsync(new UsersQuery
        {
            Search = context.Query,
            TenantId = context.TenantId,
            Role = context.Filter("role"),
            IsActive = status switch { "active" => true, "inactive" => false, _ => null },
            Page = 1,
            PageSize = context.Limit
        }, cancellationToken);

        var items = result.Users.Select(u =>
        {
            var name = u.DisplayName ?? string.Join(' ', new[] { u.FirstName, u.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var badges = new List<AdminSearchBadge>();
            if (!u.IsActive) badges.Add(new AdminSearchBadge { Label = "Status", Value = "Inactive", Tone = AdminSearchBadgeTones.Warning });
            if (u.LockoutEnd is { } lockout && lockout > DateTimeOffset.UtcNow)
                badges.Add(new AdminSearchBadge { Label = "Status", Value = "Locked", Tone = AdminSearchBadgeTones.Danger });
            if (u.TwoFactorEnabled) badges.Add(new AdminSearchBadge { Label = "MFA", Value = "On", Tone = AdminSearchBadgeTones.Info });

            return new AdminSearchResultItem
            {
                Id = u.Id,
                Title = string.IsNullOrWhiteSpace(name) ? u.Username : name,
                Subtitle = u.Email ?? u.Username,
                Route = $"/users/{u.Id}",
                ImageUrl = u.Picture,
                Badges = badges,
                Score = context.ScoreFor(name, u.Username, u.Email)
            };
        }).ToList();

        return new AdminSearchProviderResult
        {
            Items = items,
            TotalCount = result.TotalCount,
            SeeAllRoute = $"/users?search={Uri.EscapeDataString(context.Query)}"
        };
    }
}
