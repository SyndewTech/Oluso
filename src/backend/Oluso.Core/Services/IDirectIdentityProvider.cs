using System.Security.Claims;

namespace Oluso.Core.Services;

/// <summary>
/// Interface for identity providers that authenticate credentials directly (server-side)
/// without redirecting to an external IdP. Examples: LDAP, RADIUS, database auth.
///
/// Unlike OAuth/SAML which redirect users to external providers, direct identity
/// providers validate credentials against a backend service in real-time.
/// </summary>
public interface IDirectIdentityProvider
{
    /// <summary>
    /// Provider type identifier (e.g., "ldap", "radius").
    /// Used to match providers to identity provider configurations.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Authenticates a user with the given credentials.
    /// </summary>
    /// <param name="username">Username or identifier</param>
    /// <param name="password">Password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result with claims principal if successful</returns>
    Task<DirectIdentityResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of direct identity provider authentication
/// </summary>
public class DirectIdentityResult
{
    /// <summary>
    /// Whether authentication succeeded
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// Error message if authentication failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Unique identifier from the provider (e.g., LDAP DN, user GUID).
    /// Used as the provider key for external login association.
    /// </summary>
    public string? ProviderKey { get; init; }

    /// <summary>
    /// Username from the provider
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Claims principal containing all user claims from the provider.
    /// This is ready to be used for signing into the external scheme.
    /// </summary>
    public ClaimsPrincipal? Principal { get; init; }

    /// <summary>
    /// Additional properties that may be useful for the caller
    /// </summary>
    public IDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    public static DirectIdentityResult Success(
        string providerKey,
        string username,
        ClaimsPrincipal principal) => new()
    {
        Succeeded = true,
        ProviderKey = providerKey,
        Username = username,
        Principal = principal
    };

    public static DirectIdentityResult Failed(string error) => new()
    {
        Succeeded = false,
        Error = error
    };
}
