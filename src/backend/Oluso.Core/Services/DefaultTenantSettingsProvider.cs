using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Default implementation of ITenantSettingsProvider.
/// Reads settings from tenant configuration JSON or falls back to IConfiguration.
/// </summary>
public class DefaultTenantSettingsProvider : ITenantSettingsProvider
{
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DefaultTenantSettingsProvider>? _logger;

    public DefaultTenantSettingsProvider(
        ITenantContext tenantContext,
        IConfiguration configuration,
        ILogger<DefaultTenantSettingsProvider>? logger = null)
    {
        _tenantContext = tenantContext;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<T?> GetSettingsAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        // Try tenant configuration first
        if (_tenantContext.HasTenant && !string.IsNullOrEmpty(_tenantContext.Tenant?.Configuration))
        {
            try
            {
                var tenantConfig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    _tenantContext.Tenant.Configuration);

                var sectionName = typeof(T).Name.Replace("Settings", "");
                if (tenantConfig != null && tenantConfig.TryGetValue(sectionName, out var section))
                {
                    var result = JsonSerializer.Deserialize<T>(section.GetRawText());
                    return Task.FromResult(result);
                }
            }
            catch
            {
                // Fall through to configuration
            }
        }

        // Fall back to IConfiguration
        var configSection = _configuration.GetSection($"Oluso:{typeof(T).Name.Replace("Settings", "")}");
        if (configSection.Exists())
        {
            var result = configSection.Get<T>();
            return Task.FromResult(result);
        }

        return Task.FromResult<T?>(null);
    }

    public Task<T?> GetValueAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default)
    {
        // Try tenant configuration first
        if (_tenantContext.HasTenant && !string.IsNullOrEmpty(_tenantContext.Tenant?.Configuration))
        {
            try
            {
                var tenantConfig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    _tenantContext.Tenant.Configuration);

                if (tenantConfig != null && TryGetNestedValue(tenantConfig, key, out var value))
                {
                    var result = JsonSerializer.Deserialize<T>(value.GetRawText());
                    return Task.FromResult(result);
                }
            }
            catch
            {
                // Fall through to configuration
            }
        }

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
        var settings = await GetSettingsAsync<TenantTokenSettings>(cancellationToken);
        if (settings != null)
        {
            return settings;
        }

        // Build from individual configuration values
        return new TenantTokenSettings
        {
            DefaultAccessTokenLifetime = await GetValueAsync("AccessTokenLifetime", 3600, cancellationToken),
            DefaultIdentityTokenLifetime = await GetValueAsync("IdTokenLifetime", 300, cancellationToken),
            DefaultRefreshTokenLifetime = await GetValueAsync("RefreshTokenLifetime", 2592000, cancellationToken),
            IssuerUri = await GetValueAsync<string?>("IssuerUri", null, cancellationToken)
        };
    }

    public async Task<TenantPasswordSettings> GetPasswordSettingsAsync(CancellationToken cancellationToken = default)
    {
        // Try tenant-specific password policy first
        if (_tenantContext.HasTenant && _tenantContext.Tenant?.PasswordPolicy != null)
        {
            var policy = _tenantContext.Tenant.PasswordPolicy;
            return new TenantPasswordSettings
            {
                MinimumLength = policy.MinimumLength,
                MaximumLength = policy.MaximumLength,
                RequireDigit = policy.RequireDigit,
                RequireLowercase = policy.RequireLowercase,
                RequireUppercase = policy.RequireUppercase,
                RequireNonAlphanumeric = policy.RequireNonAlphanumeric,
                RequiredUniqueChars = policy.RequiredUniqueChars,
                PasswordHistoryCount = policy.PasswordHistoryCount,
                PasswordExpirationDays = policy.PasswordExpirationDays,
                MaxFailedAttempts = policy.MaxFailedAttempts,
                LockoutDurationMinutes = policy.LockoutDurationMinutes,
                BlockCommonPasswords = policy.BlockCommonPasswords,
                CheckBreachedPasswords = policy.CheckBreachedPasswords,
                CustomRegexPattern = policy.CustomRegexPattern,
                CustomRegexErrorMessage = policy.CustomRegexErrorMessage
            };
        }

        // Try configuration
        var settings = await GetSettingsAsync<TenantPasswordSettings>(cancellationToken);
        return settings ?? TenantPasswordSettings.Default;
    }

    public async Task<TenantBrandingSettings> GetBrandingSettingsAsync(CancellationToken cancellationToken = default)
    {
        // Try tenant-specific branding first
        if (_tenantContext.HasTenant && _tenantContext.Tenant?.Branding != null)
        {
            var branding = _tenantContext.Tenant.Branding;
            return new TenantBrandingSettings
            {
                LogoUrl = branding.LogoUrl,
                FaviconUrl = branding.FaviconUrl,
                PrimaryColor = branding.PrimaryColor,
                SecondaryColor = branding.SecondaryColor,
                BackgroundColor = branding.BackgroundColor,
                CustomCss = branding.CustomCss
            };
        }

        // Try configuration
        var settings = await GetSettingsAsync<TenantBrandingSettings>(cancellationToken);
        return settings ?? TenantBrandingSettings.Default;
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
                RequireDPoP = protocol.RequireDPoP,
                ClaimsParameterSupported = protocol.ClaimsParameterSupported,
                RequestParameterSupported = protocol.RequestParameterSupported,
                RequestUriParameterSupported = protocol.RequestUriParameterSupported,
                FrontchannelLogoutSupported = protocol.FrontchannelLogoutSupported,
                BackchannelLogoutSupported = protocol.BackchannelLogoutSupported
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

    private static bool TryGetNestedValue(
        Dictionary<string, JsonElement> config,
        string key,
        out JsonElement value)
    {
        var parts = key.Split(':');
        var current = config;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var element) ||
                element.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }
            current = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText())!;
        }

        return current.TryGetValue(parts[^1], out value);
    }
}
