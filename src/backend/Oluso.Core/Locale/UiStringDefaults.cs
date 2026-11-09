using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Oluso.Core.Locale;

/// <summary>
/// Provides access to the embedded system-default UI locale strings.
/// The JSON files live in <c>Oluso.Core/Locale/Resources/{locale}.json</c>
/// and are embedded at build time so both the Admin API (editor) and
/// the UI runtime (localizer) can read them without cross-referencing
/// each other's assemblies.
/// </summary>
public static class UiStringDefaults
{
    private static readonly Assembly Assembly = typeof(UiStringDefaults).Assembly;

    private static readonly ConcurrentDictionary<string, Dictionary<string, string>?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<string[]> LazyLocales = new(() =>
        Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Oluso.Core.Locale.Resources.") && n.EndsWith(".json"))
            .Select(n => n.Replace("Oluso.Core.Locale.Resources.", "").Replace(".json", ""))
            .OrderBy(c => c)
            .ToArray());

    public static string[] AvailableLocales => LazyLocales.Value;

    /// <summary>
    /// Resolves a UI string key using <see cref="CultureInfo.CurrentUICulture"/>,
    /// walking the culture fallback chain (e.g. fr-FR → fr → en).
    /// Usable from step handlers, controllers, and anywhere outside Razor views.
    /// </summary>
    public static string Resolve(string key, string? fallback = null)
    {
        var culture = CultureInfo.CurrentUICulture;
        for (var c = culture; c != null && c != CultureInfo.InvariantCulture; c = c.Parent)
        {
            var dict = GetDefaults(c.Name);
            if (dict != null && dict.TryGetValue(key, out var value))
                return value;
        }
        var en = GetDefaults("en");
        if (en != null && en.TryGetValue(key, out var enValue))
            return enValue;
        return fallback ?? key;
    }

    /// <summary>
    /// Resolves a UI string key with format arguments.
    /// </summary>
    public static string Resolve(string key, params object[] args)
    {
        var template = Resolve(key, (string?)null);
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentUICulture, template, args);
    }

    public static Dictionary<string, string>? GetDefaults(string locale)
    {
        return Cache.GetOrAdd(locale, static (loc, asm) =>
        {
            var resourceName = $"Oluso.Core.Locale.Resources.{loc}.json";
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        }, Assembly);
    }
}
