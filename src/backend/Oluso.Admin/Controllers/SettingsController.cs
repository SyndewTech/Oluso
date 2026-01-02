using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for identity server settings
/// </summary>
[Route("api/admin/settings")]
public class SettingsController : AdminBaseController
{
    private readonly IConfiguration _configuration;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantStore _tenantStore;
    private readonly ILogger<SettingsController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public SettingsController(
        IConfiguration configuration,
        ITenantContext tenantContext,
        ITenantStore tenantStore,
        ILogger<SettingsController> logger) : base(tenantContext)
    {
        _configuration = configuration;
        _tenantContext = tenantContext;
        _tenantStore = tenantStore;
        _logger = logger;
    }

    /// <summary>
    /// Get all settings
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ServerSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var tenantSettings = await GetTenantSettingsAsync(cancellationToken);

        var settings = new ServerSettingsDto
        {
            TokenSettings = tenantSettings?.TokenSettings ?? new TokenSettingsDto
            {
                DefaultAccessTokenLifetime = _configuration.GetValue("Oluso:AccessTokenLifetime", 3600),
                DefaultIdentityTokenLifetime = _configuration.GetValue("Oluso:IdentityTokenLifetime", 300),
                DefaultRefreshTokenLifetime = _configuration.GetValue("Oluso:RefreshTokenLifetime", 2592000)
            },
            SecuritySettings = tenantSettings?.SecuritySettings ?? new SecuritySettingsDto
            {
                RequireHttps = _configuration.GetValue("Oluso:RequireHttpsMetadata", true),
                EmitStaticClaims = _configuration.GetValue("Oluso:EmitStaticAudienceClaim", false),
                EnableBackchannelLogout = _configuration.GetValue("Oluso:EnableBackchannelLogout", true)
            },
            CorsSettings = tenantSettings?.CorsSettings ?? new CorsSettingsDto
            {
                AllowAllOrigins = _configuration.GetValue("Oluso:Cors:AllowAllOrigins", false),
                AllowedOrigins = _configuration.GetSection("Oluso:Cors:AllowedOrigins").Get<List<string>>() ?? new List<string>()
            }
        };

        return Ok(settings);
    }

    /// <summary>
    /// Update settings
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<ServerSettingsDto>> UpdateSettings(
        [FromBody] ServerSettingsDto settings,
        CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
        {
            return BadRequest(new { error = "No tenant context available" });
        }

        var tenant = await _tenantStore.GetByIdAsync(_tenantContext.TenantId, cancellationToken);
        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        // Serialize settings to JSON and store in tenant configuration
        tenant.Configuration = JsonSerializer.Serialize(settings, JsonOptions);
        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Updated settings for tenant {TenantId}", _tenantContext.TenantId);

        return Ok(settings);
    }

    /// <summary>
    /// Get token settings
    /// </summary>
    [HttpGet("tokens")]
    public async Task<ActionResult<TokenSettingsDto>> GetTokenSettings(CancellationToken cancellationToken)
    {
        var tenantSettings = await GetTenantSettingsAsync(cancellationToken);

        var settings = tenantSettings?.TokenSettings ?? new TokenSettingsDto
        {
            DefaultAccessTokenLifetime = _configuration.GetValue("Oluso:AccessTokenLifetime", 3600),
            DefaultIdentityTokenLifetime = _configuration.GetValue("Oluso:IdentityTokenLifetime", 300),
            DefaultRefreshTokenLifetime = _configuration.GetValue("Oluso:RefreshTokenLifetime", 2592000)
        };

        return Ok(settings);
    }

    /// <summary>
    /// Get security settings
    /// </summary>
    [HttpGet("security")]
    public async Task<ActionResult<SecuritySettingsDto>> GetSecuritySettings(CancellationToken cancellationToken)
    {
        var tenantSettings = await GetTenantSettingsAsync(cancellationToken);

        var settings = tenantSettings?.SecuritySettings ?? new SecuritySettingsDto
        {
            RequireHttps = _configuration.GetValue("Oluso:RequireHttpsMetadata", true),
            EmitStaticClaims = _configuration.GetValue("Oluso:EmitStaticAudienceClaim", false),
            EnableBackchannelLogout = _configuration.GetValue("Oluso:EnableBackchannelLogout", true)
        };

        return Ok(settings);
    }

    /// <summary>
    /// Get CORS settings
    /// </summary>
    [HttpGet("cors")]
    public async Task<ActionResult<CorsSettingsDto>> GetCorsSettings(CancellationToken cancellationToken)
    {
        var tenantSettings = await GetTenantSettingsAsync(cancellationToken);

        var settings = tenantSettings?.CorsSettings ?? new CorsSettingsDto
        {
            AllowAllOrigins = _configuration.GetValue("Oluso:Cors:AllowAllOrigins", false),
            AllowedOrigins = _configuration.GetSection("Oluso:Cors:AllowedOrigins").Get<List<string>>() ?? new List<string>()
        };

        return Ok(settings);
    }

    /// <summary>
    /// Get branding settings
    /// </summary>
    [HttpGet("branding")]
    public async Task<ActionResult<BrandingSettingsDto>> GetBrandingSettings(CancellationToken cancellationToken)
    {
        var tenantSettings = await GetTenantSettingsAsync(cancellationToken);
        return Ok(tenantSettings?.BrandingSettings ?? new BrandingSettingsDto());
    }

    /// <summary>
    /// Update branding settings
    /// </summary>
    [HttpPut("branding")]
    public async Task<ActionResult<BrandingSettingsDto>> UpdateBrandingSettings(
        [FromBody] BrandingSettingsDto settings,
        CancellationToken cancellationToken)
    {
        var currentSettings = await GetTenantSettingsAsync(cancellationToken) ?? new ServerSettingsDto();
        currentSettings.BrandingSettings = settings;

        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
        {
            return BadRequest(new { error = "No tenant context available" });
        }

        var tenant = await _tenantStore.GetByIdAsync(_tenantContext.TenantId, cancellationToken);
        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        tenant.Configuration = JsonSerializer.Serialize(currentSettings, JsonOptions);
        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Updated branding settings for tenant {TenantId}", _tenantContext.TenantId);
        return Ok(settings);
    }

    private async Task<ServerSettingsDto?> GetTenantSettingsAsync(CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
        {
            return null;
        }

        var tenant = await _tenantStore.GetByIdAsync(_tenantContext.TenantId, cancellationToken);
        if (tenant == null || string.IsNullOrEmpty(tenant.Configuration))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ServerSettingsDto>(tenant.Configuration, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize tenant configuration for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }
    }
}

#region DTOs

public class ServerSettingsDto
{
    public TokenSettingsDto TokenSettings { get; set; } = new();
    public SecuritySettingsDto SecuritySettings { get; set; } = new();
    public CorsSettingsDto CorsSettings { get; set; } = new();
    public BrandingSettingsDto? BrandingSettings { get; set; }
}

public class TokenSettingsDto
{
    public int DefaultAccessTokenLifetime { get; set; }
    public int DefaultIdentityTokenLifetime { get; set; }
    public int DefaultRefreshTokenLifetime { get; set; }
}

public class SecuritySettingsDto
{
    public bool RequireHttps { get; set; }
    public bool EmitStaticClaims { get; set; }
    public bool EnableBackchannelLogout { get; set; }
}

public class CorsSettingsDto
{
    public bool AllowAllOrigins { get; set; }
    public List<string> AllowedOrigins { get; set; } = new();
}

/// <summary>
/// Branding settings for custom login page appearance
/// </summary>
public class BrandingSettingsDto
{
    /// <summary>Primary brand color (hex)</summary>
    public string? PrimaryColor { get; set; }

    /// <summary>Secondary/accent color (hex)</summary>
    public string? SecondaryColor { get; set; }

    /// <summary>URL to the company logo</summary>
    public string? LogoUrl { get; set; }

    /// <summary>URL to the favicon</summary>
    public string? FaviconUrl { get; set; }

    /// <summary>Company name to display</summary>
    public string? CompanyName { get; set; }

    /// <summary>Support email to display</summary>
    public string? SupportEmail { get; set; }

    /// <summary>Privacy policy URL</summary>
    public string? PrivacyPolicyUrl { get; set; }

    /// <summary>Terms of service URL</summary>
    public string? TermsOfServiceUrl { get; set; }

    /// <summary>Custom CSS to inject</summary>
    public string? CustomCss { get; set; }

    /// <summary>Custom footer text</summary>
    public string? FooterText { get; set; }
}

#endregion
