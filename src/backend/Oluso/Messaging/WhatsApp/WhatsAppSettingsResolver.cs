using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Messaging.WhatsApp;

/// <summary>
/// Credentials and options the Meta client uses for one tenant.
/// </summary>
public sealed record ResolvedWhatsAppSettings(
    string TenantId,
    string AccessToken,
    string PhoneNumberId,
    string ApiVersion,
    string? BusinessAccountId,
    string? AppSecret,
    string? WebhookVerifyToken,
    string? DefaultCountry,
    WhatsAppCredentialSource Source);

public enum WhatsAppCredentialSource
{
    /// <summary>Encrypted "WhatsApp" tenant section.</summary>
    TenantSettings,
    /// <summary>Host-wide Oluso:Messaging:WhatsApp options (platform-owned number).</summary>
    Platform,
}

public interface IWhatsAppSettingsResolver
{
    /// <summary>Null when the tenant has no usable credentials anywhere.</summary>
    Task<ResolvedWhatsAppSettings?> ResolveAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>The tenant's own saved settings, decrypted, without falling back.</summary>
    Task<WhatsAppSettings?> GetTenantSettingsAsync(string tenantId, CancellationToken cancellationToken = default);

    Task SaveTenantSettingsAsync(string tenantId, WhatsAppSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolution order: the tenant's encrypted WhatsApp section, then the host
/// options. A tenant that saved credentials and disabled the channel gets
/// nothing; the host number is only a default for tenants that never set up
/// their own.
/// </summary>
public sealed class WhatsAppSettingsResolver : IWhatsAppSettingsResolver
{
    private readonly ITenantConfigurationService _configuration;
    private readonly MetaWhatsAppSettings _platform;

    public WhatsAppSettingsResolver(ITenantConfigurationService configuration, IOptions<WhatsAppOptions> options)
    {
        _configuration = configuration;
        _platform = options.Value.Meta;
    }

    public Task<WhatsAppSettings?> GetTenantSettingsAsync(string tenantId, CancellationToken cancellationToken = default) =>
        _configuration.GetSectionAsync<WhatsAppSettings>(tenantId, WhatsAppSettings.SectionKey, cancellationToken);

    public Task SaveTenantSettingsAsync(string tenantId, WhatsAppSettings settings, CancellationToken cancellationToken = default) =>
        _configuration.SetSectionAsync(tenantId, WhatsAppSettings.SectionKey, settings, cancellationToken);

    public async Task<ResolvedWhatsAppSettings?> ResolveAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("tenantId is required", nameof(tenantId));

        var own = await GetTenantSettingsAsync(tenantId, cancellationToken);
        if (own is { Enabled: true } && own.HasCredentials)
        {
            return new ResolvedWhatsAppSettings(
                tenantId,
                own.AccessToken!,
                own.PhoneNumberId!,
                string.IsNullOrWhiteSpace(own.ApiVersion) ? _platform.ApiVersion : own.ApiVersion!,
                own.BusinessAccountId,
                string.IsNullOrWhiteSpace(own.AppSecret) ? _platform.AppSecret : own.AppSecret,
                string.IsNullOrWhiteSpace(own.WebhookVerifyToken) ? _platform.WebhookVerifyToken : own.WebhookVerifyToken,
                string.IsNullOrWhiteSpace(own.DefaultCountry) ? _platform.DefaultCountry : own.DefaultCountry,
                WhatsAppCredentialSource.TenantSettings);
        }

        if (own is { Enabled: false } && own.HasCredentials)
            return null;

        if (!string.IsNullOrWhiteSpace(_platform.AccessToken) && !string.IsNullOrWhiteSpace(_platform.PhoneNumberId))
        {
            return new ResolvedWhatsAppSettings(
                tenantId,
                _platform.AccessToken,
                _platform.PhoneNumberId,
                _platform.ApiVersion,
                _platform.BusinessAccountId,
                _platform.AppSecret,
                _platform.WebhookVerifyToken,
                _platform.DefaultCountry,
                WhatsAppCredentialSource.Platform);
        }

        return null;
    }
}
