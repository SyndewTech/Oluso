using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// An API scope that no API resource owns (a token carrying it keeps its client id as audience).
/// <paramref name="RequiredFeature"/>: the tenant feature(s) the scope is issued under (<see cref="ApiScopeFeatures"/>),
/// space separated when any of several will do; null for a scope every tenant issues.
/// </summary>
public sealed record StandaloneApiScopeDefinition(
    string Name, string? DisplayName = null, string? Description = null, string? RequiredFeature = null);

/// <summary>Declares stand-alone API scopes in the current tenant.</summary>
public static class StandaloneApiScopes
{
    /// <summary>
    /// Creates the scopes of <paramref name="definitions"/> that don't exist yet in the current tenant,
    /// enabled, shown in discovery and not editable (a rename would break every client granted it), and brings the
    /// required feature of the existing ones in line (<see cref="EnsureRequiredFeaturesAsync"/>). Returns the names
    /// created. Idempotent.
    /// </summary>
    public static async Task<IReadOnlyList<string>> EnsureStandaloneApiScopesAsync(
        this IResourceStore resources,
        IEnumerable<StandaloneApiScopeDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        var wanted = Distinct(definitions);
        if (wanted.Count == 0)
            return Array.Empty<string>();

        var existing = (await resources.FindApiScopesByNameAsync(wanted.Select(d => d.Name), cancellationToken))
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);

        await resources.EnsureRequiredFeaturesAsync(wanted.Where(d => existing.Contains(d.Name)), cancellationToken);

        var created = new List<string>();
        foreach (var definition in wanted.Where(d => !existing.Contains(d.Name)))
        {
            var scope = new ApiScope
            {
                Name = definition.Name,
                DisplayName = definition.DisplayName ?? definition.Name,
                Description = definition.Description,
                Enabled = true,
                ShowInDiscoveryDocument = true,
                NonEditable = true,
            };
            if (definition.RequiredFeature is { } feature)
                scope.Properties.Add(new ApiScopeProperty { Key = ApiScopeFeatures.RequiredFeatureProperty, Value = feature });
            await resources.AddApiScopeAsync(scope, cancellationToken);
            created.Add(definition.Name);
        }
        return created;
    }

    /// <summary>
    /// Sets the required feature (<see cref="ApiScopeFeatures.RequiredFeatureProperty"/>) of the existing scopes of
    /// <paramref name="definitions"/> in the current tenant to the definition's, creating nothing. A definition
    /// without a feature leaves its scope as it is. Returns the names changed. Idempotent: this is how scopes declared
    /// before they carried the property (or in tenants that have since switched the feature off) become gated.
    /// </summary>
    public static async Task<IReadOnlyList<string>> EnsureRequiredFeaturesAsync(
        this IResourceStore resources,
        IEnumerable<StandaloneApiScopeDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        var wanted = Distinct(definitions)
            .Where(d => d.RequiredFeature != null)
            .ToDictionary(d => d.Name, d => d.RequiredFeature!, StringComparer.Ordinal);
        if (wanted.Count == 0)
            return Array.Empty<string>();

        var changed = new List<string>();
        foreach (var scope in await resources.FindApiScopesByNameAsync(wanted.Keys, cancellationToken))
        {
            if (!wanted.TryGetValue(scope.Name, out var feature))
                continue;
            var properties = scope.Properties
                .Where(p => string.Equals(p.Key, ApiScopeFeatures.RequiredFeatureProperty, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (properties.Count == 1 && properties[0].Value == feature)
                continue;

            foreach (var extra in properties.Skip(1))
                scope.Properties.Remove(extra);
            if (properties.Count == 0)
                scope.Properties.Add(new ApiScopeProperty { Key = ApiScopeFeatures.RequiredFeatureProperty, Value = feature });
            else
                properties[0].Value = feature;
            await resources.UpdateApiScopeAsync(scope, cancellationToken);
            changed.Add(scope.Name);
        }
        return changed;
    }

    /// <summary>
    /// One definition per name with its required feature in canonical form. A scope two features declare (one
    /// product sold in two editions) requires either of them.
    /// </summary>
    private static List<StandaloneApiScopeDefinition> Distinct(IEnumerable<StandaloneApiScopeDefinition> definitions) =>
        definitions
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .Select(g => g.First() with
            {
                RequiredFeature = ApiScopeFeatures.Format(g.SelectMany(d => ApiScopeFeatures.Parse(d.RequiredFeature)))
            })
            .ToList();
}
