using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Dto.Settings;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Locale;

namespace Oluso.Admin.Controllers;

[Route("api/admin/locale-strings")]
public class LocaleStringsController : AdminBaseController
{
    private readonly ITenantConfigurationService _configService;

    public LocaleStringsController(
        ITenantContext tenantContext,
        ITenantConfigurationService configService)
        : base(tenantContext)
    {
        _configService = configService;
    }

    [HttpGet("locales")]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public ActionResult<IEnumerable<string>> GetLocales()
    {
        return Ok(UiStringDefaults.AvailableLocales);
    }

    [HttpGet("defaults/{locale}")]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public ActionResult<Dictionary<string, string>> GetDefaults(string locale)
    {
        var defaults = UiStringDefaults.GetDefaults(locale);
        if (defaults == null)
            return NotFound(new { error = "locale_not_found", message = $"No system defaults for locale '{locale}'" });

        return Ok(defaults);
    }

    [HttpGet("tenant/{locale}")]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<LocaleStringsEditorResponse>> GetTenantOverrides(
        string locale, CancellationToken ct)
    {
        var defaults = UiStringDefaults.GetDefaults(locale)
            ?? UiStringDefaults.GetDefaults("en")
            ?? new Dictionary<string, string>();

        var section = await _configService.GetSectionAsync<UiTranslationOverrides>(
            TenantId, SectionKey(locale), ct);

        return Ok(new LocaleStringsEditorResponse
        {
            Locale = locale,
            Defaults = defaults,
            Overrides = section?.Overrides ?? new Dictionary<string, string>()
        });
    }

    [HttpPut("tenant/{locale}")]
    [RequirePermission(AdminPermissions.SettingsWrite)]
    public async Task<ActionResult> SaveTenantOverrides(
        string locale,
        [FromBody] SaveLocaleOverridesRequest request,
        CancellationToken ct)
    {
        var cleaned = request.Overrides
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (cleaned.Count == 0)
        {
            await _configService.RemoveSectionAsync(TenantId, SectionKey(locale), ct);
        }
        else
        {
            await _configService.SetSectionAsync(
                TenantId, SectionKey(locale),
                new UiTranslationOverrides { Overrides = cleaned }, ct);
        }

        return NoContent();
    }

    [HttpDelete("tenant/{locale}")]
    [RequirePermission(AdminPermissions.SettingsWrite)]
    public async Task<ActionResult> DeleteTenantOverrides(string locale, CancellationToken ct)
    {
        await _configService.RemoveSectionAsync(TenantId, SectionKey(locale), ct);
        return NoContent();
    }

    private static string SectionKey(string locale) => $"UiTranslations:{locale}";
}

public class UiTranslationOverrides
{
    public Dictionary<string, string> Overrides { get; set; } = new();
}
