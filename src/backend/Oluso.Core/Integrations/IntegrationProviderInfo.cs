namespace Oluso.Core.Integrations;

/// <summary>
/// Metadata about an integration provider
/// </summary>
public record IntegrationProviderInfo(
    string ProviderId,
    string DisplayName,
    string ProviderType,
    string[]? SupportedRegions,
    CredentialFieldInfo[] CredentialFields,
    string Description
);

/// <summary>
/// Metadata about a credential field required by a provider
/// </summary>
public record CredentialFieldInfo(
    string FieldName,
    string DisplayName,
    bool IsSecret
);
