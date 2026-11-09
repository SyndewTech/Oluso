using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Options for tenant configuration encryption.
/// Modules register which section keys should be encrypted at rest.
/// </summary>
public class TenantConfigurationOptions
{
    /// <summary>
    /// Section keys whose values should be encrypted at rest via DataProtection.
    /// Each section uses purpose string "Oluso.TenantConfig.{SectionKey}".
    /// </summary>
    public HashSet<string> EncryptedSections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Section-key prefixes whose matching sections should be encrypted at rest.
    /// Used when section keys carry a per-entity suffix (e.g. "SenderProfile:{Id}")
    /// and the exact keys aren't known at startup.
    /// </summary>
    public HashSet<string> EncryptedSectionPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Centralized service for reading/writing tenant configuration sections.
/// Stores each section as a separate row in TenantConfigurationSections table.
/// Sensitive sections are encrypted at rest using ASP.NET Core DataProtection.
/// </summary>
public class TenantConfigurationService : ITenantConfigurationService
{
    private readonly ITenantConfigurationSectionStore _store;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly TenantConfigurationOptions _options;
    private readonly ILogger<TenantConfigurationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public TenantConfigurationService(
        ITenantConfigurationSectionStore store,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<TenantConfigurationOptions> options,
        ILogger<TenantConfigurationService> logger)
    {
        _store = store;
        _dataProtectionProvider = dataProtectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<T?> GetSectionAsync<T>(string tenantId, string sectionKey, CancellationToken cancellationToken = default) where T : class
    {
        var section = await _store.GetAsync(tenantId, sectionKey, cancellationToken);
        if (section == null)
            return null;

        var json = section.Value;

        if (section.IsEncrypted)
        {
            try
            {
                var protector = CreateProtector(sectionKey);
                json = protector.Unprotect(section.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt configuration section {SectionKey} for tenant {TenantId}", sectionKey, tenantId);
                return null;
            }
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize configuration section {SectionKey} for tenant {TenantId}", sectionKey, tenantId);
            return null;
        }
    }

    public async Task SetSectionAsync<T>(string tenantId, string sectionKey, T value, CancellationToken cancellationToken = default) where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var shouldEncrypt = _options.EncryptedSections.Contains(sectionKey)
            || _options.EncryptedSectionPrefixes.Any(p =>
                sectionKey.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        var storedValue = json;
        if (shouldEncrypt)
        {
            var protector = CreateProtector(sectionKey);
            storedValue = protector.Protect(json);
        }

        var section = new TenantConfigurationSection
        {
            TenantId = tenantId,
            SectionKey = sectionKey,
            Value = storedValue,
            IsEncrypted = shouldEncrypt
        };

        await _store.UpsertAsync(section, cancellationToken);
    }

    public async Task RemoveSectionAsync(string tenantId, string sectionKey, CancellationToken cancellationToken = default)
    {
        await _store.RemoveAsync(tenantId, sectionKey, cancellationToken);
    }

    private IDataProtector CreateProtector(string sectionKey)
    {
        return _dataProtectionProvider.CreateProtector($"Oluso.TenantConfig.{sectionKey}");
    }
}
