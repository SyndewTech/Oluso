namespace Oluso.Core.Integrations;

/// <summary>
/// Integration settings stored in Tenant.Configuration JSON under the "Integrations" section key.
/// Each provider has its own credentials and enabled state.
/// </summary>
public class IntegrationTenantSettings
{
    /// <summary>
    /// Section key used in Tenant.Configuration JSON
    /// </summary>
    public const string SectionKey = "Integrations";

    /// <summary>
    /// Provider configurations keyed by provider ID (e.g., "whatsapp-meta", "smtp", "sendgrid", "twilio-sms")
    /// </summary>
    public Dictionary<string, IntegrationProviderConfig> Providers { get; set; } = new();

    public static IntegrationTenantSettings Default => new();
}

/// <summary>
/// Configuration for a single integration provider within a tenant.
/// </summary>
public class IntegrationProviderConfig
{
    /// <summary>
    /// Whether this provider is enabled for the tenant
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Provider type for categorization (e.g., "messaging")
    /// </summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>
    /// Credential key-value pairs. The whole section is encrypted at rest
    /// (see <c>AddIntegrationTenantSettings</c>); values are never returned
    /// to clients, only which keys are set.
    /// </summary>
    public Dictionary<string, string> Credentials { get; set; } = new();

    /// <summary>
    /// Environment for the integration (e.g., "sandbox", "production")
    /// </summary>
    public string? Environment { get; set; }
}
