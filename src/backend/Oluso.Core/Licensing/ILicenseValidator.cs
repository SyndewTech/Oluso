namespace Oluso.Core.Licensing;

/// <summary>
/// Interface for license validation
/// </summary>
public interface ILicenseValidator
{
    /// <summary>
    /// Gets the current license information
    /// </summary>
    LicenseInfo GetLicenseInfo();

    /// <summary>
    /// Gets the current license (alias for GetLicenseInfo for convenience)
    /// </summary>
    LicenseInfo? GetCurrentLicense() => GetLicenseInfo();

    /// <summary>
    /// Gets all features available in the current license
    /// </summary>
    IEnumerable<string> GetLicensedFeatures() => GetLicenseInfo()?.Features ?? Enumerable.Empty<string>();

    /// <summary>
    /// Validates if a specific feature is licensed
    /// </summary>
    LicenseValidationResult ValidateFeature(string feature);

    /// <summary>
    /// Validates if the license allows the current usage
    /// </summary>
    LicenseValidationResult ValidateLimits(string limitType, int currentCount);

    /// <summary>
    /// Check if the license is valid (not expired, properly signed)
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// Check if currently in grace period
    /// </summary>
    bool IsInGracePeriod { get; }
}

/// <summary>
/// License information
/// </summary>
public class LicenseInfo
{
    /// <summary>
    /// License holder company name
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// License holder contact email
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// License tier
    /// </summary>
    public LicenseTier Tier { get; set; } = LicenseTier.Community;

    /// <summary>
    /// License issue date
    /// </summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>
    /// License expiration date
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Licensed features
    /// </summary>
    public List<string> Features { get; set; } = new();

    /// <summary>
    /// License limits
    /// </summary>
    public LicenseLimits Limits { get; set; } = LicenseLimits.Community;

    /// <summary>
    /// Add-ons included in the license
    /// </summary>
    public List<string> AddOns { get; set; } = new();

    /// <summary>
    /// Whether this is a trial license
    /// </summary>
    public bool IsTrial { get; set; }

    /// <summary>
    /// Unique license ID
    /// </summary>
    public string? LicenseId { get; set; }

    /// <summary>
    /// Check if license is expired
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    /// <summary>
    /// Days until expiration (negative if expired)
    /// </summary>
    public int DaysUntilExpiration => (int)(ExpiresAt - DateTime.UtcNow).TotalDays;
}

/// <summary>
/// Result of a license validation check
/// </summary>
public class LicenseValidationResult
{
    public bool IsValid { get; set; }
    public string? Message { get; set; }
    public string? FeatureRequired { get; set; }
    public LicenseTier? RequiredTier { get; set; }

    public static LicenseValidationResult Valid() => new() { IsValid = true };

    public static LicenseValidationResult Invalid(string message, string? feature = null) => new()
    {
        IsValid = false,
        Message = message,
        FeatureRequired = feature
    };

    public static LicenseValidationResult RequiresUpgrade(LicenseTier requiredTier, string message) => new()
    {
        IsValid = false,
        Message = message,
        RequiredTier = requiredTier
    };
}

/// <summary>
/// License tiers
/// </summary>
public enum LicenseTier
{
    /// <summary>
    /// Community license - free for companies under revenue threshold
    /// </summary>
    Community,

    /// <summary>
    /// Starter license - basic paid license
    /// </summary>
    Starter,

    /// <summary>
    /// Professional license - includes some add-ons
    /// </summary>
    Professional,

    /// <summary>
    /// Enterprise license - all features included
    /// </summary>
    Enterprise,

    /// <summary>
    /// Development/testing license
    /// </summary>
    Development
}

/// <summary>
/// License limits
/// </summary>
public class LicenseLimits
{
    /// <summary>
    /// Maximum number of clients (null = unlimited)
    /// </summary>
    public int? MaxClients { get; set; }

    /// <summary>
    /// Maximum number of tenants (null = unlimited)
    /// </summary>
    public int? MaxTenants { get; set; }

    /// <summary>
    /// Maximum number of users (null = unlimited)
    /// </summary>
    public int? MaxUsers { get; set; }

    /// <summary>
    /// Maximum tokens per hour (null = unlimited)
    /// </summary>
    public int? MaxTokensPerHour { get; set; }

    /// <summary>
    /// Default limits for community license
    /// </summary>
    public static LicenseLimits Community => new()
    {
        MaxClients = 5,
        MaxTenants = 1,
        MaxUsers = 100,
        MaxTokensPerHour = 1000
    };

    /// <summary>
    /// Default limits for starter license
    /// </summary>
    public static LicenseLimits Starter => new()
    {
        MaxClients = 25,
        MaxTenants = 3,
        MaxUsers = 1000,
        MaxTokensPerHour = 10000
    };

    /// <summary>
    /// No limits (professional/enterprise)
    /// </summary>
    public static LicenseLimits Unlimited => new();
}

/// <summary>
/// Licensed feature identifiers for Oluso platform licensing.
///
/// Tier Breakdown:
/// - Community/Starter: Core, MultiTenancy, JourneyEngine, AdminUI, AccountUI
/// - Professional: + Fido2, Scim, Telemetry, AuditLogging, KeyVault, Webhooks, UnlimitedClients
/// - Enterprise: + Saml, Ldap, UnlimitedTenants, CustomBranding, PrioritySupport, Sla
///
/// Add-ons (purchasable at any tier):
/// - Saml, Ldap (for Pro customers who need enterprise protocols)
/// - Telemetry (for Community/Starter who want observability)
/// </summary>
public static class LicensedFeatures
{
    #region Core Features (Community/Starter)

    /// <summary>Core OIDC/OAuth2 functionality</summary>
    public const string Core = "core";

    /// <summary>Multi-tenant architecture support</summary>
    public const string MultiTenancy = "multi-tenancy";

    /// <summary>Authentication journey/flow engine</summary>
    public const string JourneyEngine = "journey-engine";

    /// <summary>Admin dashboard UI</summary>
    public const string AdminUI = "admin-ui";

    /// <summary>End-user account self-service UI</summary>
    public const string AccountUI = "account-ui";

    #endregion

    #region Professional Features

    /// <summary>FIDO2/WebAuthn passkey authentication</summary>
    public const string Fido2 = "fido2";

    /// <summary>SCIM 2.0 user provisioning</summary>
    public const string Scim = "scim";

    /// <summary>OpenTelemetry metrics and tracing</summary>
    public const string Telemetry = "telemetry";

    /// <summary>Advanced audit logging and retention</summary>
    public const string AuditLogging = "audit-logging";

    /// <summary>Azure Key Vault / HSM key management</summary>
    public const string KeyVault = "keyvault";

    /// <summary>Webhook event notifications</summary>
    public const string Webhooks = "webhooks";

    /// <summary>No client application limits</summary>
    public const string UnlimitedClients = "unlimited-clients";

    #endregion

    #region Enterprise Features

    /// <summary>SAML 2.0 Identity Provider</summary>
    public const string Saml = "saml";

    /// <summary>LDAP/Active Directory integration</summary>
    public const string Ldap = "ldap";

    /// <summary>No tenant limits</summary>
    public const string UnlimitedTenants = "unlimited-tenants";

    /// <summary>White-label and custom branding</summary>
    public const string CustomBranding = "custom-branding";

    /// <summary>Priority support response times</summary>
    public const string PrioritySupport = "priority-support";

    /// <summary>Service Level Agreement guarantee</summary>
    public const string Sla = "sla";

    #endregion

    #region Tenant Features (for tenant billing/subscriptions)

    /// <summary>
    /// User subscription module - allows tenants to bill their end-users.
    /// This is an add-on for tenants building SaaS on top of Oluso.
    /// </summary>
    public const string UserSubscriptions = "user-subscriptions";

    /// <summary>Custom domain for tenant's auth endpoints</summary>
    public const string TenantCustomDomain = "tenant-custom-domain";

    /// <summary>Tenant-level custom branding</summary>
    public const string TenantBranding = "tenant-branding";

    #endregion
}

/// <summary>
/// Exception thrown when a licensed feature is used without proper license
/// </summary>
public class LicenseException : Exception
{
    public string? Feature { get; }
    public LicenseTier? RequiredTier { get; }

    public LicenseException(string message) : base(message) { }

    public LicenseException(string message, string feature) : base(message)
    {
        Feature = feature;
    }

    public LicenseException(string message, LicenseTier requiredTier) : base(message)
    {
        RequiredTier = requiredTier;
    }
}
