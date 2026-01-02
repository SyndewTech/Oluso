namespace Oluso.UI.ViewModels;

/// <summary>
/// View model for OAuth/OIDC scopes in consent screen
/// </summary>
public class ScopeViewModel
{
    /// <summary>
    /// The scope name (e.g., "openid", "profile", "email")
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// User-friendly display name
    /// </summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Description of what this scope grants access to
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this scope is required and cannot be unchecked
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Whether this scope is checked by default
    /// </summary>
    public bool Checked { get; set; } = true;

    /// <summary>
    /// Whether to emphasize this scope (e.g., sensitive permissions)
    /// </summary>
    public bool Emphasize { get; set; }
}
