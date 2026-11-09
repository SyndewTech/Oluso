using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Default implementation of ITenantSettingsProvider.
/// Reads settings from TenantConfigurationSections via ITenantConfigurationService,
/// or falls back to IConfiguration.
/// </summary>
public class DefaultTenantSettingsProvider : ITenantSettingsProvider
{
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly ITenantConfigurationService _configService;
    private readonly ITenantStore _tenantStore;
    private readonly ILogger<DefaultTenantSettingsProvider>? _logger;

    public DefaultTenantSettingsProvider(
        ITenantContext tenantContext,
        IConfiguration configuration,
        ITenantConfigurationService configService,
        ITenantStore tenantStore,
        ILogger<DefaultTenantSettingsProvider>? logger = null)
    {
        _tenantStore = tenantStore;
        _tenantContext = tenantContext;
        _configuration = configuration;
        _configService = configService;
        _logger = logger;
    }

    public async Task<T?> GetSettingsAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        return await GetSettingsAsync<T>(typeof(T).Name.Replace("Settings", ""), cancellationToken);
    }

    private async Task<T?> GetSettingsAsync<T>(string sectionKey, CancellationToken cancellationToken) where T : class
    {
        if (_tenantContext.HasTenant && _tenantContext.TenantId != null)
        {
            var result = await _configService.GetSectionAsync<T>(_tenantContext.TenantId, sectionKey, cancellationToken);
            if (result != null) return result;
        }

        // Fall back to IConfiguration
        var configSection = _configuration.GetSection($"Oluso:{sectionKey}");
        if (configSection.Exists())
        {
            return configSection.Get<T>();
        }

        return null;
    }

    public Task<T?> GetValueAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default)
    {
        // Fall back to IConfiguration
        var configValue = _configuration.GetValue<T>($"Oluso:{key}");
        if (configValue != null)
        {
            return Task.FromResult<T?>(configValue);
        }

        // Try IdentityServer prefix for backwards compatibility
        configValue = _configuration.GetValue<T>($"IdentityServer:{key}");
        if (configValue != null)
        {
            return Task.FromResult<T?>(configValue);
        }

        return Task.FromResult(defaultValue);
    }

    public async Task<TenantTokenSettings> GetTokenSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync<TenantTokenSettings>("TokenSettings", cancellationToken);
        if (settings != null)
        {
            return settings;
        }

        // Build from individual configuration values.
        //
        // IssuerUri is intentionally read ONLY from per-tenant TenantConfigurationSections
        // (via _configService), never from server-level Oluso:IssuerUri. This field
        // represents a tenant-specific OVERRIDE — when populated, IssuerResolver tier 1
        // returns it directly and skips the auto-subdomain (tier 3) and CustomDomain
        // (tier 2) branches. Falling back to the server config here would cause every
        // tenant to look like it has an override set to the platform host, breaking the
        // auto-subdomain ({tenant}.{issuerHost}) feature for everyone.
        var perTenantIssuerOverride = _tenantContext.HasTenant && _tenantContext.TenantId != null
            ? await _configService.GetSectionAsync<TenantIssuerOverride>(_tenantContext.TenantId, "TokenSettings", cancellationToken)
            : null;

        return new TenantTokenSettings
        {
            DefaultAccessTokenLifetime = await GetValueAsync("AccessTokenLifetime", 3600, cancellationToken),
            DefaultIdentityTokenLifetime = await GetValueAsync("IdTokenLifetime", 300, cancellationToken),
            DefaultRefreshTokenLifetime = await GetValueAsync("RefreshTokenLifetime", 2592000, cancellationToken),
            IssuerUri = perTenantIssuerOverride?.IssuerUri,
        };
    }

    /// <summary>
    /// Minimal DTO used to extract just the IssuerUri override from a tenant's
    /// TokenSettings configuration row. We don't reuse TenantTokenSettings here
    /// because that type also carries lifetimes, which we want to populate from
    /// IConfiguration fallbacks above — only IssuerUri must be per-tenant only.
    /// </summary>
    private sealed class TenantIssuerOverride
    {
        public string? IssuerUri { get; set; }
    }

    public async Task<TenantPasswordSettings> GetPasswordSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.HasTenant && _tenantContext.Tenant?.PasswordPolicy != null)
        {
            return TenantPasswordSettings.FromPolicy(_tenantContext.Tenant.PasswordPolicy);
        }

        var settings = await GetSettingsAsync<TenantPasswordSettings>(cancellationToken);
        return settings ?? TenantPasswordSettings.Default;
    }

    public async Task<TenantPasswordSettings> GetPasswordSettingsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.HasTenant && string.Equals(_tenantContext.TenantId, tenantId, StringComparison.Ordinal))
        {
            return await GetPasswordSettingsAsync(cancellationToken);
        }

        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant?.PasswordPolicy != null)
        {
            return TenantPasswordSettings.FromPolicy(tenant.PasswordPolicy);
        }

        var sectionKey = nameof(TenantPasswordSettings).Replace("Settings", "");
        return await _configService.GetSectionAsync<TenantPasswordSettings>(tenantId, sectionKey, cancellationToken)
            ?? _configuration.GetSection($"Oluso:{sectionKey}").Get<TenantPasswordSettings>()
            ?? TenantPasswordSettings.Default;
    }

    public async Task<TenantAccountEmailSettings> GetAccountEmailSettingsAsync(CancellationToken cancellationToken = default) =>
        await GetSettingsAsync<TenantAccountEmailSettings>(TenantAccountEmailSettings.SectionKey, cancellationToken)
        ?? new TenantAccountEmailSettings();

    public async Task<TenantAccountEmailSettings> GetAccountEmailSettingsAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await _configService.GetSectionAsync<TenantAccountEmailSettings>(tenantId, TenantAccountEmailSettings.SectionKey, cancellationToken)
        ?? _configuration.GetSection($"Oluso:{TenantAccountEmailSettings.SectionKey}").Get<TenantAccountEmailSettings>()
        ?? new TenantAccountEmailSettings();

    public async Task<TenantBrandingSettings> GetBrandingSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
        {
            return new TenantBrandingSettings();
        }

        return await GetBrandingSettingsAsync(_tenantContext.TenantId, cancellationToken);
    }

    public async Task<TenantBrandingSettings> GetBrandingSettingsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        // Visual branding lives entirely in the BrandingSettings tenant-config
        // section (admin UI writes through /api/admin/settings/branding).
        // HideEmbedBranding still lives on the entity because it's a
        // SuperAdmin-only platform-licensing toggle with its own write path.
        var section = await _configService.GetSectionAsync<TenantBrandingSettings>(tenantId, "BrandingSettings", cancellationToken)
            ?? new TenantBrandingSettings();

        // HideEmbedBranding only lands on the result when reading for the
        // currently-resolved tenant — that's the only path with the entity
        // in scope. For cross-tenant reads (tenant-switcher list, etc.) it
        // defaults to false, which is the correct value for end-user views.
        if (_tenantContext.HasTenant
            && string.Equals(_tenantContext.TenantId, tenantId, StringComparison.Ordinal))
        {
            section.HideEmbedBranding = _tenantContext.Tenant?.Branding?.HideEmbedBranding ?? false;
        }

        return section;
    }

    public async Task<LocaleSettings> GetLocaleSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync<LocaleSettings>(cancellationToken);
        return settings ?? LocaleSettings.Default;
    }

    public async Task<WorkspaceAppsSettings> GetWorkspaceAppsSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync<WorkspaceAppsSettings>(cancellationToken);
        return settings ?? WorkspaceAppsSettings.Default;
    }

    public async Task<TenantProtocolSettings> GetProtocolSettingsAsync(CancellationToken cancellationToken = default)
    {
        // Try tenant-specific protocol configuration first
        if (_tenantContext.HasTenant && _tenantContext.Tenant?.ProtocolConfiguration != null)
        {
            var protocol = _tenantContext.Tenant.ProtocolConfiguration;
            return new TenantProtocolSettings
            {
                AllowedGrantTypes = ParseJsonArray(protocol.AllowedGrantTypesJson),
                AllowedResponseTypes = ParseJsonArray(protocol.AllowedResponseTypesJson),
                AllowedTokenEndpointAuthMethods = ParseJsonArray(protocol.AllowedTokenEndpointAuthMethodsJson),
                SubjectTypesSupported = ParseJsonArray(protocol.SubjectTypesSupportedJson),
                IdTokenSigningAlgValuesSupported = ParseJsonArray(protocol.IdTokenSigningAlgValuesSupportedJson),
                CodeChallengeMethodsSupported = ParseJsonArray(protocol.CodeChallengeMethodsSupportedJson),
                DPoPSigningAlgValuesSupported = ParseJsonArray(protocol.DPoPSigningAlgValuesSupportedJson),
                RequirePushedAuthorizationRequests = protocol.RequirePushedAuthorizationRequests,
                RequirePkce = protocol.RequirePkce,
                AllowPlainPkce = protocol.AllowPlainPkce,
                EnforceOAuth21 = protocol.EnforceOAuth21,
                RequireDPoP = protocol.RequireDPoP,
                ClaimsParameterSupported = protocol.ClaimsParameterSupported,
                RequestParameterSupported = protocol.RequestParameterSupported,
                RequestUriParameterSupported = protocol.RequestUriParameterSupported,
                FrontchannelLogoutSupported = protocol.FrontchannelLogoutSupported,
                BackchannelLogoutSupported = protocol.BackchannelLogoutSupported,
                PrefixScopeSuffixMaxLength = protocol.PrefixScopeSuffixMaxLength,
                RefreshImplyingScopes = ParseJsonArray(protocol.RefreshImplyingScopesJson),
                // Dynamic Client Registration (RFC 7591)
                EnableDynamicClientRegistration = protocol.EnableDynamicClientRegistration,
                AllowOpenDynamicRegistration = protocol.AllowOpenDynamicRegistration,
                DynamicRegistrationAllowedScopes = ParseJsonArray(protocol.DynamicRegistrationAllowedScopesJson),
                DynamicRegistrationAllowedGrantTypes = ParseJsonArray(protocol.DynamicRegistrationAllowedGrantTypesJson),
                DynamicRegistrationRequirePkce = protocol.DynamicRegistrationRequirePkce,
                DynamicRegistrationMaxRedirectUris = protocol.DynamicRegistrationMaxRedirectUris
            };
        }

        // Try configuration
        var settings = await GetSettingsAsync<TenantProtocolSettings>(cancellationToken);
        return settings ?? TenantProtocolSettings.Default;
    }

    private static List<string>? ParseJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }
}
