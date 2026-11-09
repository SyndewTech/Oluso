using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// An ApiScope can require a tenant feature (module): the property <see cref="RequiredFeatureProperty"/> names it,
/// or several separated by spaces, any one of which suffices. Such a scope is issued only while the feature is
/// enabled for the tenant the request is routed to; the owning module stamps the property on the scopes it declares.
/// This lets one client (a platform client used on every tenant's host) be allowed a product's scopes once, while
/// each tenant only issues those of the products it has switched on.
/// </summary>
public static class ApiScopeFeatures
{
    /// <summary>The ApiScope property naming the tenant feature(s) the scope requires.</summary>
    public const string RequiredFeatureProperty = "required_feature";

    /// <summary>The features a property value names (space or comma separated), in order, without duplicates.</summary>
    public static IReadOnlyList<string> Parse(string? value) =>
        (value ?? string.Empty)
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The features <paramref name="scope"/> requires (any one of them); empty when it requires none.</summary>
    public static IReadOnlyList<string> RequiredFeatures(ApiScope scope) =>
        Parse(scope.Properties?
            .FirstOrDefault(p => string.Equals(p.Key, RequiredFeatureProperty, StringComparison.OrdinalIgnoreCase))?
            .Value);

    /// <summary>The canonical property value for <paramref name="features"/>, or null when there are none.</summary>
    public static string? Format(IEnumerable<string>? features)
    {
        var list = Parse(string.Join(' ', features ?? Array.Empty<string>()));
        return list.Count == 0 ? null : string.Join(' ', list);
    }
}

/// <summary>
/// Decides which scopes the routed tenant may issue now, by the tenant features their ApiScopes require
/// (<see cref="ApiScopeFeatures"/>). Used at the authorize and token endpoints: an explicitly requested scope whose
/// feature is off is refused with <c>invalid_scope</c>; one a grant carries forward (a refresh, an exchange) is left
/// out of the new tokens.
/// </summary>
public interface IScopeFeatureGate
{
    /// <summary>The scopes of <paramref name="scopes"/> whose required feature is off for the routed tenant.</summary>
    Task<IReadOnlyList<string>> FindUnavailableAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ScopeFeatureGate : IScopeFeatureGate
{
    private readonly IResourceStore _resources;
    private readonly ITenantContext _tenant;
    private readonly ITenantFeatureStore? _features;
    private readonly ITenantSettingsProvider? _settings;

    public ScopeFeatureGate(
        IResourceStore resources, ITenantContext tenant, ITenantFeatureStore? features = null, ITenantSettingsProvider? settings = null)
    {
        _resources = resources;
        _tenant = tenant;
        _features = features;
        _settings = settings;
    }

    public async Task<IReadOnlyList<string>> FindUnavailableAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        var names = scopes.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
            return Array.Empty<string>();

        // The ApiScopes of the routed tenant (the store is tenant-filtered): the same rows the scope validator
        // accepts the names against. A prefix scope's instance (name + suffix) needs what the prefix scope needs.
        var defaultSuffixMaxLength = await ApiScopePrefixes.GetDefaultSuffixMaxLengthAsync(_settings, cancellationToken);
        var gated = (await ApiScopePrefixes.ResolveAsync(_resources, names, cancellationToken, defaultSuffixMaxLength))
            .Select(kv => (Name: kv.Key, Features: ApiScopeFeatures.RequiredFeatures(kv.Value)))
            .Where(s => s.Features.Count > 0)
            .ToList();
        if (gated.Count == 0)
            return Array.Empty<string>();

        var tenantId = _tenant.HasTenant && !string.IsNullOrEmpty(_tenant.TenantId) ? _tenant.TenantId! : PlatformConstants.TenantId;
        var enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var unavailable = new List<string>();
        foreach (var (name, features) in gated)
        {
            var available = false;
            foreach (var feature in features)
            {
                if (!enabled.TryGetValue(feature, out var on))
                {
                    // Without a feature store nothing can be shown to be on: a gated scope is not issued.
                    on = _features != null && await _features.IsFeatureEnabledAsync(tenantId, feature, cancellationToken);
                    enabled[feature] = on;
                }
                if (on)
                {
                    available = true;
                    break;
                }
            }
            if (!available)
                unavailable.Add(name);
        }
        return unavailable;
    }
}
