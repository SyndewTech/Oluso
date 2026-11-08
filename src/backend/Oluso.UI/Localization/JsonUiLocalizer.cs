using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LocalizedString = Microsoft.Extensions.Localization.LocalizedString;

namespace Oluso.UI.Localization;

/// <summary>
/// Loads UI strings from embedded JSON resources (system defaults) and
/// merges per-request tenant/journey overrides on top via
/// <see cref="ITenantLocaleOverrideProvider"/> resolved from the current
/// <see cref="HttpContext"/>. Embedded defaults are cached at the singleton
/// level; overrides are resolved per-request.
/// </summary>
public sealed class JsonUiLocalizer : IStringLocalizer
{
    private readonly ILogger<JsonUiLocalizer> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HostFileLocaleOverrides _hostOverrides;

    public JsonUiLocalizer(
        ILogger<JsonUiLocalizer> logger,
        IHttpContextAccessor httpContextAccessor,
        HostFileLocaleOverrides hostOverrides)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _hostOverrides = hostOverrides;
    }

    public LocalizedString this[string name]
    {
        get
        {
            var value = Resolve(name, CultureInfo.CurrentUICulture);
            return new LocalizedString(name, value ?? name, resourceNotFound: value == null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var template = Resolve(name, CultureInfo.CurrentUICulture) ?? name;
            var formatted = arguments.Length == 0 ? template : string.Format(CultureInfo.CurrentUICulture, template, arguments);
            return new LocalizedString(name, formatted, resourceNotFound: template == name && Resolve(name, CultureInfo.CurrentUICulture) == null);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var entries = GetMergedEntries(CultureInfo.CurrentUICulture.Name);
        foreach (var (key, value) in entries)
        {
            yield return new LocalizedString(key, value);
        }
    }

    private string? Resolve(string key, CultureInfo culture)
    {
        foreach (var tag in CandidateTags(culture))
        {
            var entries = GetMergedEntries(tag);
            if (entries.TryGetValue(key, out var value))
                return value;
        }
        return null;
    }

    private IReadOnlyDictionary<string, string> GetMergedEntries(string locale)
    {
        var embedded = Oluso.Core.Locale.UiStringDefaults.GetDefaults(locale)
            ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();

        // Layered, least- to most-specific:
        //   embedded defaults  <  host files  <  tenant/journey overrides
        var hostOverrides = GetHostOverrides(locale);
        var tenantOverrides = GetTenantOverrides(locale);

        var hasHost = hostOverrides is { Count: > 0 };
        var hasTenant = tenantOverrides is { Count: > 0 };
        if (!hasHost && !hasTenant)
            return embedded;

        var merged = new Dictionary<string, string>(embedded, StringComparer.Ordinal);
        if (hasHost)
            foreach (var (k, v) in hostOverrides!) merged[k] = v;
        if (hasTenant)
            foreach (var (k, v) in tenantOverrides!) merged[k] = v;
        return merged;
    }

    private IReadOnlyDictionary<string, string>? GetHostOverrides(string locale)
    {
        try
        {
            return _hostOverrides.GetOverrides(locale);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load host locale overrides for {Locale}", locale);
            return null;
        }
    }

    private IReadOnlyDictionary<string, string>? GetTenantOverrides(string locale)
    {
        var provider = _httpContextAccessor.HttpContext?.RequestServices
            .GetService(typeof(ITenantLocaleOverrideProvider)) as ITenantLocaleOverrideProvider;
        if (provider == null)
            return null;

        try
        {
            return provider.GetOverrides(locale);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tenant locale overrides for {Locale}", locale);
            return null;
        }
    }

    private static IEnumerable<string> CandidateTags(CultureInfo culture)
    {
        for (var cursor = culture; cursor != null && cursor != CultureInfo.InvariantCulture; cursor = cursor.Parent)
        {
            yield return cursor.Name;
        }
        yield return "en";
    }
}

/// <summary>
/// Optional provider for tenant/journey-specific locale overrides. Registered
/// as scoped; resolved per-request by <see cref="JsonUiLocalizer"/> from
/// <c>HttpContext.RequestServices</c>.
/// </summary>
public interface ITenantLocaleOverrideProvider
{
    IReadOnlyDictionary<string, string>? GetOverrides(string locale);
}

/// <summary>
/// Factory that returns the shared <see cref="JsonUiLocalizer"/> regardless
/// of the resource type. This lets ASP.NET's <c>IViewLocalizer</c> work
/// without per-view resource files.
/// </summary>
public sealed class JsonUiLocalizerFactory : IStringLocalizerFactory
{
    private readonly JsonUiLocalizer _localizer;

    public JsonUiLocalizerFactory(JsonUiLocalizer localizer)
    {
        _localizer = localizer;
    }

    public IStringLocalizer Create(Type resourceSource) => _localizer;
    public IStringLocalizer Create(string baseName, string location) => _localizer;
}
