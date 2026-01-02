namespace Oluso.Licensing;

/// <summary>
/// Configuration options for Oluso platform licensing.
///
/// License Hierarchy:
/// 1. Platform License (this) - Oluso license for running the identity server
/// 2. Tenant Subscription - Your billing of your customers (tenants)
/// 3. User Subscription (optional add-on) - Tenants billing their end-users
/// </summary>
public class OlusoLicenseOptions
{
    /// <summary>
    /// The license key (JWT format, signed by Oluso)
    /// </summary>
    public string? LicenseKey { get; set; }

    /// <summary>
    /// Path to license file (alternative to LicenseKey)
    /// </summary>
    public string? LicenseFilePath { get; set; }

    /// <summary>
    /// Company name for license validation
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// Annual revenue threshold for community license (USD)
    /// Companies under this threshold can use core features for free
    /// </summary>
    public decimal CommunityRevenueThreshold { get; set; } = 1_000_000m;

    /// <summary>
    /// Self-declared annual revenue (for honor-system community license)
    /// </summary>
    public decimal? DeclaredAnnualRevenue { get; set; }

    /// <summary>
    /// Validate the license signature.
    /// Set to false only for development/testing.
    /// </summary>
    public bool ValidateSignature { get; set; } = true;

    /// <summary>
    /// Grace period in days after license expiration
    /// </summary>
    public int GracePeriodDays { get; set; } = 14;

    /// <summary>
    /// Custom public key for license signature validation.
    /// If null, uses the embedded Oluso public key.
    /// </summary>
    public string? PublicKey { get; set; }
}
