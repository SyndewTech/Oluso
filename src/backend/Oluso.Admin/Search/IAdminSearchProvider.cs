using System.Security.Claims;
using Oluso.Admin.Dto.Search;

namespace Oluso.Admin.Search;

/// <summary>
/// One searchable kind of record in the admin app, contributed by the core or
/// by a module. The aggregator only calls a provider when the tenant has its
/// feature and the caller holds its read permission; the provider then applies
/// the same scoping its own admin controllers use.
/// </summary>
public interface IAdminSearchProvider
{
    /// <summary>Stable id, e.g. <c>hr.employees</c>.</summary>
    string ProviderId { get; }

    /// <summary>Module feature key; <see cref="AdminSearchModules.Platform"/> for core records.</summary>
    string Module { get; }

    /// <summary>Feature the tenant must have; null for core records.</summary>
    string? FeatureKey => Module == AdminSearchModules.Platform ? null : Module;

    /// <summary>
    /// Permissions the caller must hold, all of them: the module's admin entry
    /// permission plus the entity's read permission, exactly what the record's
    /// admin controller enforces, so a hit never leads to a page that refuses.
    /// </summary>
    IReadOnlyList<string> RequiredPermissions { get; }

    /// <summary>Kind of record within the module, e.g. <c>employee</c>.</summary>
    string Category { get; }

    string DisplayName { get; }
    string Icon { get; }

    /// <summary>Filters this provider understands; shown when the caller narrows to it.</summary>
    IReadOnlyList<AdminSearchFilterDefinition> Filters => Array.Empty<AdminSearchFilterDefinition>();

    Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default);
}

public static class AdminSearchModules
{
    public const string Platform = "platform";
}

/// <summary>
/// Everything a provider needs to answer a search for one caller.
/// </summary>
public sealed class AdminSearchContext
{
    /// <summary>Tenant in context; null when a SuperAdmin searches at the platform host.</summary>
    public string? TenantId { get; init; }
    public required string UserId { get; init; }
    public required ClaimsPrincipal User { get; init; }
    public required string Query { get; init; }
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();
    public int Limit { get; init; } = AdminSearchDefaults.Limit;

    public string? Filter(string key) => Filters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>Case-insensitive containment test used by in-memory providers.</summary>
    public bool Matches(params string?[] fields)
    {
        foreach (var field in fields)
        {
            if (field != null && field.Contains(Query, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Higher when the query matches at the start of a primary field.</summary>
    public double ScoreFor(string? primary, params string?[] secondary)
    {
        if (primary != null)
        {
            if (primary.Equals(Query, StringComparison.OrdinalIgnoreCase)) return 3;
            if (primary.StartsWith(Query, StringComparison.OrdinalIgnoreCase)) return 2;
            if (primary.Contains(Query, StringComparison.OrdinalIgnoreCase)) return 1.5;
        }
        return Matches(secondary) ? 1 : 0;
    }
}

public sealed class AdminSearchProviderResult
{
    public static readonly AdminSearchProviderResult Empty = new();

    public IReadOnlyList<AdminSearchResultItem> Items { get; init; } = Array.Empty<AdminSearchResultItem>();

    /// <summary>Total matches when known; defaults to the number of items.</summary>
    public int? TotalCount { get; init; }

    /// <summary>Admin route to the list page pre-filtered by the query.</summary>
    public string? SeeAllRoute { get; init; }
}

public static class AdminSearchDefaults
{
    public const int Limit = 5;
    public const int MaxLimit = 50;
    public const int MinQueryLength = 2;
    public static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(8);
}
