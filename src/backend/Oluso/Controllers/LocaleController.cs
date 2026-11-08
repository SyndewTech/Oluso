using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Locale;

namespace Oluso.Controllers;

/// <summary>
/// Provides locale reference data and tenant locale settings.
/// Reference data (currencies, locales) is public.
/// Tenant settings require authentication (admin or OIDC tokens).
/// </summary>
[ApiController]
[Route("api/locale")]
public class LocaleController : ControllerBase
{
    private readonly ITenantConfigurationService _configService;
    private readonly ITenantContext _tenantContext;

    public LocaleController(ITenantConfigurationService configService, ITenantContext tenantContext)
    {
        _configService = configService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// The tenant whose locale applies: the one the middleware resolved from the
    /// bearer token or the request host (anonymous pages such as hosted quotes
    /// and journeys land on the tenant's host), falling back to the token claim.
    /// The platform tenant is not a locale owner.
    /// </summary>
    private string? TenantId
    {
        get
        {
            if (_tenantContext.HasTenant
                && !string.IsNullOrEmpty(_tenantContext.TenantId)
                && !string.Equals(_tenantContext.TenantId, Oluso.Core.PlatformConstants.TenantId, StringComparison.Ordinal))
                return _tenantContext.TenantId;
            return User.FindFirst("tenant_id")?.Value;
        }
    }

    /// <summary>
    /// Get supported currencies (static reference data, no auth required)
    /// </summary>
    [HttpGet("currencies")]
    [AllowAnonymous]
    public IActionResult GetCurrencies()
    {
        return Ok(SupportedCurrencies.All);
    }

    /// <summary>
    /// Get supported locales (static reference data, no auth required)
    /// </summary>
    [HttpGet("locales")]
    [AllowAnonymous]
    public IActionResult GetLocales()
    {
        return Ok(SupportedLocales.All);
    }

    /// <summary>
    /// Get supported countries with calling codes (static reference data, no auth required)
    /// </summary>
    [HttpGet("countries")]
    [AllowAnonymous]
    public IActionResult GetCountries()
    {
        return Ok(SupportedCountries.All);
    }

    /// <summary>
    /// Get locale settings for the current tenant. Currency, date order and time
    /// zone are display conventions, not secrets, so anonymous requests on a
    /// tenant host get them too; without any tenant context the platform
    /// defaults are returned.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetLocaleSettings(CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        if (string.IsNullOrEmpty(tenantId))
            return Ok(LocaleSettings.Default);

        var locale = await _configService.GetSectionAsync<LocaleSettings>(tenantId, "Locale", cancellationToken);
        return Ok(locale ?? LocaleSettings.Default);
    }
}
