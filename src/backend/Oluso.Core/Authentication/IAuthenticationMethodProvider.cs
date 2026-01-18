namespace Oluso.Core.Authentication;

/// <summary>
/// Provides authentication method UI integration for login and account management pages.
/// Implement this interface to add a pluggable authentication method (passkey, TOTP, SMS, etc.)
/// that can be discovered and rendered by the UI without hardcoded dependencies.
/// </summary>
/// <remarks>
/// <para>
/// Authentication methods register themselves via DI and are discovered by the
/// <see cref="IAuthenticationMethodRegistry"/>. The UI queries the registry to
/// render appropriate login options and account management sections.
/// </para>
/// <para>
/// Example registration in DI:
/// <code>
/// services.AddScoped&lt;IAuthenticationMethodProvider, PasskeyAuthenticationMethodProvider&gt;();
/// </code>
/// </para>
/// </remarks>
public interface IAuthenticationMethodProvider
{
    /// <summary>
    /// Unique identifier for this authentication method (e.g., "passkey", "totp", "sms")
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name shown in UI (e.g., "Passkey", "Authenticator App", "SMS")
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Category for grouping and filtering:
    /// - "primary": Primary login methods (password, passkey)
    /// - "passwordless": Passwordless options (passkey, magic link, SMS)
    /// - "mfa": Multi-factor authentication (TOTP, SMS, email)
    /// - "recovery": Recovery options (backup codes)
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Display order within category (lower = first)
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Icon identifier for UI (e.g., "fingerprint", "key", "phone")
    /// </summary>
    string? Icon { get; }

    /// <summary>
    /// Whether this method is currently available/configured.
    /// Return false if the method requires configuration that hasn't been done.
    /// </summary>
    bool IsAvailable { get; }

    #region Login Support

    /// <summary>
    /// Whether this method can be used for login
    /// </summary>
    bool SupportsLogin { get; }

    /// <summary>
    /// URL for the login flow handled by this provider.
    /// The main login page will redirect/link to this URL.
    /// Should accept a returnUrl query parameter.
    /// Example: "/fido2/login"
    /// </summary>
    string? LoginUrl { get; }

    /// <summary>
    /// Text to display on the login button (e.g., "Sign in with Passkey")
    /// If null, uses "Sign in with {DisplayName}"
    /// </summary>
    string? LoginButtonText { get; }

    /// <summary>
    /// Optional SVG icon markup for the login button.
    /// If provided, rendered inline before the button text.
    /// </summary>
    string? LoginButtonIcon { get; }

    /// <summary>
    /// Hint text shown below the login button (e.g., "Use your fingerprint, face, or security key")
    /// </summary>
    string? LoginHint { get; }

    /// <summary>
    /// Partial view name for login UI (e.g., "_PasskeyAssertion")
    /// The view receives the model from <see cref="GetLoginViewModelAsync"/>.
    /// Used when the provider handles login inline rather than via redirect.
    /// </summary>
    string? LoginPartialViewName { get; }

    /// <summary>
    /// Gets the view model for the login partial view.
    /// Returns null if login is not available for this user/context.
    /// </summary>
    /// <param name="username">Optional username for username-first flows</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<object?> GetLoginViewModelAsync(string? username, CancellationToken cancellationToken = default);

    #endregion

    #region Account Management Support

    /// <summary>
    /// Whether this method supports user self-management (view/add/remove credentials)
    /// </summary>
    bool SupportsManagement { get; }

    /// <summary>
    /// Partial view name for account management UI (e.g., "_PasskeyManagement")
    /// The view receives the model from <see cref="GetManagementViewModelAsync"/>
    /// </summary>
    string? ManagementPartialViewName { get; }

    /// <summary>
    /// Gets the view model for the management partial view.
    /// Returns null if management is not available for this user.
    /// </summary>
    /// <param name="userId">The user ID to get credentials for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<object?> GetManagementViewModelAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of configured credentials/methods for a user.
    /// Used to show badge counts in UI.
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<int> GetCredentialCountAsync(string userId, CancellationToken cancellationToken = default);

    #endregion

    #region JavaScript Support

    /// <summary>
    /// JavaScript files required by this method's UI.
    /// Paths should be relative to wwwroot (e.g., "js/passkey.js")
    /// </summary>
    IEnumerable<string> RequiredScripts { get; }

    /// <summary>
    /// Inline JavaScript to execute when the partial is loaded.
    /// Return null if no inline script is needed.
    /// </summary>
    string? InlineScript { get; }

    #endregion
}

/// <summary>
/// Registry for discovering and accessing authentication method providers.
/// The UI uses this to dynamically render login options and account management sections.
/// </summary>
public interface IAuthenticationMethodRegistry
{
    /// <summary>
    /// Gets all registered authentication method providers
    /// </summary>
    IEnumerable<IAuthenticationMethodProvider> GetAll();

    /// <summary>
    /// Gets a specific provider by ID
    /// </summary>
    IAuthenticationMethodProvider? Get(string id);

    /// <summary>
    /// Gets providers by category (e.g., "passwordless", "mfa")
    /// </summary>
    IEnumerable<IAuthenticationMethodProvider> GetByCategory(string category);

    /// <summary>
    /// Gets all providers that support login, ordered by Order property
    /// </summary>
    IEnumerable<IAuthenticationMethodProvider> GetLoginProviders();

    /// <summary>
    /// Gets all providers that support account management, ordered by Order property
    /// </summary>
    IEnumerable<IAuthenticationMethodProvider> GetManagementProviders();

    /// <summary>
    /// Gets available login providers (IsAvailable == true and SupportsLogin == true)
    /// </summary>
    IEnumerable<IAuthenticationMethodProvider> GetAvailableLoginProviders();

    /// <summary>
    /// Gets available management providers (IsAvailable == true and SupportsManagement == true)
    /// </summary>
    IEnumerable<IAuthenticationMethodProvider> GetAvailableManagementProviders();
}

/// <summary>
/// View model for rendering an authentication method in the UI
/// </summary>
public class AuthenticationMethodViewModel
{
    /// <summary>
    /// The provider metadata
    /// </summary>
    public required IAuthenticationMethodProvider Provider { get; init; }

    /// <summary>
    /// The view model for the partial view (from GetLoginViewModelAsync or GetManagementViewModelAsync)
    /// </summary>
    public object? ViewModel { get; set; }

    /// <summary>
    /// Credential count for management views
    /// </summary>
    public int CredentialCount { get; set; }
}

/// <summary>
/// Well-known authentication method categories
/// </summary>
public static class AuthenticationMethodCategories
{
    /// <summary>Primary login methods (password, passkey)</summary>
    public const string Primary = "primary";

    /// <summary>Passwordless options (passkey, magic link)</summary>
    public const string Passwordless = "passwordless";

    /// <summary>Multi-factor authentication (TOTP, SMS, email)</summary>
    public const string Mfa = "mfa";

    /// <summary>Recovery options (backup codes)</summary>
    public const string Recovery = "recovery";
}

/// <summary>
/// Well-known authentication method IDs
/// </summary>
public static class AuthenticationMethodIds
{
    public const string Password = "password";
    public const string Passkey = "passkey";
    public const string Totp = "totp";
    public const string Sms = "sms";
    public const string Email = "email";
    public const string BackupCodes = "backup-codes";
}
