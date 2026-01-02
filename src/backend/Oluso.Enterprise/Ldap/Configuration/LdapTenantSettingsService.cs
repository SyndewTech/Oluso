using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Enterprise.Ldap.Configuration;

/// <summary>
/// Default implementation of ILdapTenantSettingsService.
/// Reads/writes settings from tenant Configuration JSON.
/// </summary>
public class LdapTenantSettingsService : ILdapTenantSettingsService
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantStore _tenantStore;
    private readonly ILogger<LdapTenantSettingsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LdapTenantSettingsService(
        ITenantContext tenantContext,
        ITenantStore tenantStore,
        ILogger<LdapTenantSettingsService> logger)
    {
        _tenantContext = tenantContext;
        _tenantStore = tenantStore;
        _logger = logger;
    }

    public Task<TenantLdapSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.HasTenant || string.IsNullOrEmpty(_tenantContext.Tenant?.Configuration))
        {
            return Task.FromResult(TenantLdapSettings.Default);
        }

        return Task.FromResult(ParseSettings(_tenantContext.Tenant.Configuration));
    }

    public async Task<TenantLdapSettings> GetSettingsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null || string.IsNullOrEmpty(tenant.Configuration))
        {
            return TenantLdapSettings.Default;
        }

        return ParseSettings(tenant.Configuration);
    }

    public async Task UpdateSettingsAsync(string tenantId, TenantLdapSettings settings, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} not found");
        }

        // Parse existing configuration or create new
        var config = string.IsNullOrEmpty(tenant.Configuration)
            ? new Dictionary<string, object>()
            : JsonSerializer.Deserialize<Dictionary<string, object>>(tenant.Configuration, JsonOptions)
              ?? new Dictionary<string, object>();

        // Update LDAP settings
        config["ldapServer"] = new
        {
            enabled = settings.Enabled,
            baseDn = settings.BaseDn,
            organization = settings.Organization,
            allowAnonymousBind = settings.AllowAnonymousBind,
            maxSearchResults = settings.MaxSearchResults,
            adminDn = settings.AdminDn
        };

        // Save updated configuration
        tenant.Configuration = JsonSerializer.Serialize(config, JsonOptions);
        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Updated LDAP settings for tenant {TenantId}", tenantId);
    }

    private TenantLdapSettings ParseSettings(string configuration)
    {
        try
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configuration, JsonOptions);

            // Try both PascalCase and camelCase keys
            if (config != null &&
                (config.TryGetValue("LdapServer", out var ldapSection) ||
                 config.TryGetValue("ldapServer", out ldapSection)))
            {
                var settings = JsonSerializer.Deserialize<TenantLdapSettings>(ldapSection.GetRawText(), JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse LDAP settings from tenant configuration");
        }

        return TenantLdapSettings.Default;
    }
}
