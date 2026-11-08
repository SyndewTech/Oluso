using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;

namespace Oluso.UI.Localization;

/// <summary>
/// Resolves locale string overrides from tenant configuration sections
/// and (when a journey is active) from the journey policy's
/// <see cref="JourneyUiConfiguration.LocaleOverrides"/>.
/// Cascade: journey override → tenant override → (system default handled
/// by <see cref="JsonUiLocalizer"/>).
/// </summary>
public sealed class TenantConfigLocaleOverrideProvider : ITenantLocaleOverrideProvider
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantConfigurationService _configService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly ILogger<TenantConfigLocaleOverrideProvider> _logger;

    // Per-request cache — this service is scoped, so the dictionary lives
    // for exactly one HTTP request and is never shared across requests.
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>?> _requestCache = new(StringComparer.OrdinalIgnoreCase);

    public const string JourneyPolicyIdKey = "JourneyPolicyId";

    public TenantConfigLocaleOverrideProvider(
        ITenantContext tenantContext,
        ITenantConfigurationService configService,
        IHttpContextAccessor httpContextAccessor,
        IJourneyPolicyStore policyStore,
        ILogger<TenantConfigLocaleOverrideProvider> logger)
    {
        _tenantContext = tenantContext;
        _configService = configService;
        _httpContextAccessor = httpContextAccessor;
        _policyStore = policyStore;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, string>? GetOverrides(string locale)
    {
        if (_requestCache.TryGetValue(locale, out var cached))
            return cached;

        var result = LoadOverrides(locale);
        _requestCache[locale] = result;
        return result;
    }

    private IReadOnlyDictionary<string, string>? LoadOverrides(string locale)
    {
        if (!_tenantContext.HasTenant || string.IsNullOrEmpty(_tenantContext.TenantId))
            return null;

        Dictionary<string, string>? merged = null;

        // 1. Tenant-level overrides
        try
        {
            var section = _configService
                .GetSectionAsync<TenantUiTranslationOverrides>(_tenantContext.TenantId, $"UiTranslations:{locale}", default)
                .GetAwaiter().GetResult();

            if (section?.Overrides is { Count: > 0 })
            {
                merged = new Dictionary<string, string>(section.Overrides, StringComparer.Ordinal);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tenant locale overrides for {Locale}", locale);
        }

        // 2. Journey-level overrides (layer on top when a journey is active)
        var policyId = _httpContextAccessor.HttpContext?.Items[JourneyPolicyIdKey] as string;
        if (!string.IsNullOrEmpty(policyId))
        {
            try
            {
                var policy = _policyStore.GetByIdAsync(policyId).GetAwaiter().GetResult();
                if (policy?.Ui?.LocaleOverrides != null &&
                    policy.Ui.LocaleOverrides.TryGetValue(locale, out var journeyOverrides) &&
                    journeyOverrides.Count > 0)
                {
                    merged ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (k, v) in journeyOverrides) merged[k] = v;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load journey locale overrides for policy {PolicyId}, locale {Locale}", policyId, locale);
            }
        }

        return merged;
    }
}

internal class TenantUiTranslationOverrides
{
    public Dictionary<string, string> Overrides { get; set; } = new();
}
