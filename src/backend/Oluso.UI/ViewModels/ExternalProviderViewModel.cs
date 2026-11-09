namespace Oluso.UI.ViewModels;

/// <summary>
/// Minimal input for the shared _ProviderIcon partial. Lets every login
/// surface (journey composite/external, standalone) render the same
/// brand glyph from a provider type, with a custom IconUrl taking
/// precedence when one is configured.
/// </summary>
public class ProviderIconModel
{
    public string? ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public string? DisplayName { get; set; }
}

/// <summary>
/// CSS hook helpers for provider buttons. Type-level classes
/// (<c>provider-{type}</c>) style every provider of a kind; the
/// scheme-level class (<c>provider-scheme-{slug}</c>) targets one
/// specific provider when a tenant has more than one of the same type.
/// </summary>
public static class ProviderCss
{
    public static string TypeToken(string? providerType)
        => string.IsNullOrWhiteSpace(providerType) ? "external" : providerType.Trim().ToLowerInvariant();

    public static string SchemeSlug(string? scheme)
    {
        if (string.IsNullOrWhiteSpace(scheme)) return "unknown";

        var sb = new System.Text.StringBuilder(scheme.Length);
        var lastDash = false;
        foreach (var ch in scheme.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        return sb.ToString().Trim('-') is { Length: > 0 } s ? s : "unknown";
    }
}

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
