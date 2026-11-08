namespace Oluso.Core.Licensing;

/// <summary>
/// Utilities for working with module features.
/// Feature keys are defined by each module's IModuleDescriptor, not here.
/// </summary>
public static class ModuleFeatures
{
    /// <summary>
    /// Get feature definitions from registered modules only.
    /// </summary>
    public static IReadOnlyList<ModuleFeatureDefinition> GetRegistered()
    {
        return ModuleRegistry.Instance.GetRegisteredModules()
            .Select(m => new ModuleFeatureDefinition(m.FeatureKey, m.DisplayName, m.Description))
            .ToList();
    }

    /// <summary>
    /// Maps a webhook/event provider ID to its feature key.
    /// Returns null for core providers (always available).
    /// Uses the registered module descriptors — no hardcoded mapping.
    /// </summary>
    public static string? GetFeatureKeyForProvider(string? providerId)
    {
        if (string.IsNullOrEmpty(providerId))
            return null;

        // Check if providerId matches a registered module's feature key directly
        var module = ModuleRegistry.Instance.GetModule(providerId);
        if (module != null)
            return module.FeatureKey;

        // Check common aliases (e.g., "subscriptions" → billing module)
        foreach (var m in ModuleRegistry.Instance.GetRegisteredModules())
        {
            if (string.Equals(m.Category, providerId, StringComparison.OrdinalIgnoreCase))
                return m.FeatureKey;
        }

        return null;
    }

    /// <summary>
    /// The module a feature key belongs to: the module with that key, or the one whose key it extends
    /// ("crm.marketing.emails" belongs to "crm"). Null when no registered module owns it.
    /// </summary>
    public static IModuleDescriptor? GetModuleForFeature(string featureKey)
    {
        var modules = ModuleRegistry.Instance.GetRegisteredModules();
        return modules.FirstOrDefault(m => string.Equals(m.FeatureKey, featureKey, StringComparison.OrdinalIgnoreCase))
            ?? modules.FirstOrDefault(m => featureKey.StartsWith(m.FeatureKey + ".", StringComparison.OrdinalIgnoreCase));
    }
}

public record ModuleFeatureDefinition(string Key, string DisplayName, string Description);
