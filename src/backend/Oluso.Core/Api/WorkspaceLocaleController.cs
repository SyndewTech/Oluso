using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Locale;

namespace Oluso.Core.Api;

/// <summary>
/// Workspace locale endpoint — returns the current tenant's locale settings.
/// Accessible by any authenticated workspace user (no tenantId parameter needed,
/// resolved from the token automatically).
/// </summary>
[Route("api/workspace/locale")]
public class WorkspaceLocaleController : WorkspaceBaseController
{
    private readonly ITenantSettingsProvider _settingsProvider;

    public WorkspaceLocaleController(
        ITenantContext tenantContext,
        ITenantSettingsProvider settingsProvider) : base(tenantContext)
    {
        _settingsProvider = settingsProvider;
    }

    /// <summary>
    /// Get the current tenant's locale settings
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<LocaleSettings>> GetLocale(CancellationToken cancellationToken)
    {
        var settings = await _settingsProvider.GetLocaleSettingsAsync(cancellationToken);
        return Ok(settings);
    }

    /// <summary>
    /// Get supported currencies
    /// </summary>
    [HttpGet("currencies")]
    public ActionResult<IEnumerable<CurrencyInfo>> GetSupportedCurrencies()
    {
        return Ok(SupportedCurrencies.All);
    }

    /// <summary>
    /// Get supported locales
    /// </summary>
    [HttpGet("locales")]
    public ActionResult<IEnumerable<LocaleInfo>> GetSupportedLocales()
    {
        return Ok(SupportedLocales.All);
    }

    /// <summary>
    /// Get supported countries with calling codes
    /// </summary>
    [HttpGet("countries")]
    public ActionResult<IEnumerable<CountryInfo>> GetSupportedCountries()
    {
        return Ok(SupportedCountries.All);
    }
}
