using Oluso.Core.Domain.Interfaces;

namespace Oluso.Enterprise.Scim.Outbound;

/// <summary>
/// The secret a provider is called with: a static bearer token, or the OAuth
/// client secret when the provider uses client credentials. Stored as an
/// encrypted tenant configuration section keyed by provider id, so it never
/// sits on the provider row and is never returned by the admin API.
/// </summary>
public sealed class ScimProviderCredentials
{
    public const string SectionKeyPrefix = "ScimProvider:";

    public static string BuildSectionKey(string providerId) => $"{SectionKeyPrefix}{providerId}";

    public string? Secret { get; set; }
}

public interface IScimProviderSecretStore
{
    Task<string?> GetSecretAsync(string tenantId, string providerId, CancellationToken cancellationToken = default);
    Task SetSecretAsync(string tenantId, string providerId, string secret, CancellationToken cancellationToken = default);
    Task RemoveAsync(string tenantId, string providerId, CancellationToken cancellationToken = default);
}

public sealed class ScimProviderSecretStore : IScimProviderSecretStore
{
    private readonly ITenantConfigurationService _configuration;

    public ScimProviderSecretStore(ITenantConfigurationService configuration)
    {
        _configuration = configuration;
    }

    public async Task<string?> GetSecretAsync(string tenantId, string providerId, CancellationToken cancellationToken = default)
    {
        var section = await _configuration.GetSectionAsync<ScimProviderCredentials>(
            tenantId, ScimProviderCredentials.BuildSectionKey(providerId), cancellationToken);
        return string.IsNullOrEmpty(section?.Secret) ? null : section.Secret;
    }

    public Task SetSecretAsync(string tenantId, string providerId, string secret, CancellationToken cancellationToken = default) =>
        _configuration.SetSectionAsync(
            tenantId,
            ScimProviderCredentials.BuildSectionKey(providerId),
            new ScimProviderCredentials { Secret = secret },
            cancellationToken);

    public Task RemoveAsync(string tenantId, string providerId, CancellationToken cancellationToken = default) =>
        _configuration.RemoveSectionAsync(tenantId, ScimProviderCredentials.BuildSectionKey(providerId), cancellationToken);
}
