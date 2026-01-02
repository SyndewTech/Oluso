namespace Oluso.UI.ViewModels;

/// <summary>
/// View model for external authentication providers (OAuth/OIDC/SAML)
/// </summary>
public class ExternalProviderViewModel
{
    /// <summary>
    /// The authentication scheme name
    /// </summary>
    public string AuthenticationScheme { get; set; } = null!;

    /// <summary>
    /// Display name shown to users
    /// </summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// URL to provider's icon/logo
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Provider type (e.g., "Google", "Microsoft", "Saml2", "Oidc")
    /// Used to determine authentication flow
    /// </summary>
    public string? ProviderType { get; set; }

    /// <summary>
    /// Whether this provider uses redirect-based flow (SAML uses controller, OAuth uses Challenge)
    /// </summary>
    public bool IsSaml => ProviderType?.Equals("Saml2", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// View model for direct login providers (LDAP, RADIUS, etc.)
/// These providers have their own login pages rather than redirecting to external IdPs.
/// </summary>
public class DirectLoginProviderViewModel
{
    /// <summary>
    /// Provider identifier (e.g., "ldap")
    /// </summary>
    public string ProviderId { get; set; } = null!;

    /// <summary>
    /// Display name shown to users
    /// </summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// URL path to the login page for this provider
    /// </summary>
    public string LoginPath { get; set; } = null!;

    /// <summary>
    /// URL to provider's icon/logo
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Optional description or hint text
    /// </summary>
    public string? Description { get; set; }
}
