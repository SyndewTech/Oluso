using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Oluso.UI.Localization;

/// <summary>
/// Loads host-supplied UI string overrides from JSON files in the consuming
/// application's content root. Default location is
/// <c>{ContentRoot}/Resources/Strings/{locale}.json</c>, overridable via the
/// <c>Oluso:Localization:OverridesPath</c> setting (absolute or content-root
/// relative).
///
/// These merge on top of the embedded system defaults but below per-tenant /
/// per-journey overrides, so a self-hosting deployment (consuming Oluso.UI as a
/// NuGet package) can rebrand strings by dropping files next to the app — no
/// library rebuild required. Files are re-read automatically when their
/// last-write time changes.
/// </summary>
public sealed class HostFileLocaleOverrides
{
    private readonly string _baseDir;
    private readonly ILogger<HostFileLocaleOverrides> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(DateTime LastWriteUtc, IReadOnlyDictionary<string, string>? Entries);

    public HostFileLocaleOverrides(
        IHostEnvironment env,
        IConfiguration configuration,
        ILogger<HostFileLocaleOverrides> logger)
    {
        _logger = logger;
        var configured = configuration["Oluso:Localization:OverridesPath"];
        var relative = string.IsNullOrWhiteSpace(configured) ? "Resources/Strings" : configured;
        _baseDir = Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(env.ContentRootPath, relative);
    }

    /// <summary>
    /// Returns the host overrides for a single locale tag (e.g. "en", "fr"),
    /// or null when no override file exists for it.
    /// </summary>
    public IReadOnlyDictionary<string, string>? GetOverrides(string locale)
    {
        string path;
        FileInfo info;
        try
        {
            path = Path.Combine(_baseDir, locale + ".json");
            info = new FileInfo(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid host locale override path for locale {Locale}", locale);
            return null;
        }

        if (!info.Exists)
        {
            _cache.TryRemove(locale, out _);
            return null;
        }

        var lastWrite = info.LastWriteTimeUtc;
        if (_cache.TryGetValue(locale, out var cached) && cached.LastWriteUtc == lastWrite)
            return cached.Entries;

        try
        {
            using var stream = File.OpenRead(path);
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            _cache[locale] = new CacheEntry(lastWrite, entries);
            return entries;
        }
        catch (Exception ex)
        {
            // Cache the failure against this mtime so a malformed file isn't
            // re-parsed on every request; it retries once the file changes.
            _logger.LogWarning(ex, "Failed to read host locale override file {Path}", path);
            _cache[locale] = new CacheEntry(lastWrite, null);
            return null;
        }
    }
}
