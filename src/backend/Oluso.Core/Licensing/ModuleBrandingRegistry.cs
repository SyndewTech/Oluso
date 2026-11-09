namespace Oluso.Core.Licensing;

/// <summary>
/// Static registry for module display name overrides.
/// Allows host applications to rebrand modules via configuration
/// without code changes.
/// </summary>
public static class ModuleBrandingRegistry
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public static void SetDisplayName(string featureKey, string displayName)
        => _overrides[featureKey] = displayName;

    public static string GetDisplayName(string featureKey, string fallback)
        => _overrides.TryGetValue(featureKey, out var name) ? name : fallback;

    public static IReadOnlyDictionary<string, string> GetAllOverrides()
        => _overrides;

    public static void Clear() => _overrides.Clear();
}
